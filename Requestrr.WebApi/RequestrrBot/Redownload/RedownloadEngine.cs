using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    // The destructive state machine: delete-then-search, with restore-on-timeout if nothing
    // better ever arrives. BeginAsync is called by the workflow AFTER the user has confirmed —
    // everything in it either has already happened to real files, or is about to.
    public class RedownloadEngine
    {
        private readonly Func<bool, IRedownloadMediaClient> _clientSelector;
        private readonly RedownloadJobsRepository _repo;
        private readonly IRedownloadMessenger _messenger;
        private readonly ILogger _logger;
        private readonly TimeSpan _pollInterval;
        private readonly Func<DateTime> _utcNow;

        private CancellationTokenSource _cts;
        private Task _pollTask;

        // Guards against a job's restore ever being attempted twice, including under genuine
        // concurrency (overlapping poll ticks, or a poll tick racing a direct CheckJobAsync
        // call) where the job.State field read at the top of CheckJobAsync can be stale for one
        // of the racing callers. A job's Id is claimed here immediately before the restore
        // attempt and is never released for the lifetime of this engine instance.
        private readonly object _restoreLock = new object();
        private readonly HashSet<Guid> _inFlightRestores = new HashSet<Guid>();

        public RedownloadEngine(Func<bool, IRedownloadMediaClient> clientSelector, RedownloadJobsRepository repo,
                                 IRedownloadMessenger messenger, ILogger logger, TimeSpan pollInterval,
                                 Func<DateTime> utcNow = null)
        {
            _clientSelector = clientSelector;
            _repo = repo;
            _messenger = messenger;
            _logger = logger;
            _pollInterval = pollInterval;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        // ---- BeginAsync -------------------------------------------------------

        // Sequence is BINDING (spec Section 3): capture current file -> ensure monitored ->
        // find grab history id -> post public message -> DELETE -> blocklist (skipped, with a
        // flag, when there's no grab history) -> trigger search -> persist job.
        //
        // The job is persisted (State=Searching, DeadlineUtc already computed) BEFORE the
        // delete, not after: once the file is gone, the persisted job record is the ONLY thing
        // that can ever trigger a restore. Deferring persistence to the end of the method would
        // leave a crash window (process death between delete and the final Add/Update) where a
        // real file is destroyed with zero record of it -- permanent, silent data loss. DeleteFile,
        // MarkHistoryFailed, and TriggerSearchAsync are each individually wrapped so a failure in
        // any one of them never stops the others from running and never leaves the (already
        // persisted) job un-updated; the job is always re-persisted at the end regardless of what
        // happened after the delete.
        public async Task<RedownloadJob> BeginAsync(RedownloadTarget target, ulong requesterId, string requesterName,
                                                      ulong channelId, int timeoutMinutes)
        {
            var client = _clientSelector(target.IsMovie);

            var currentFile = await client.GetCurrentFileAsync(target);
            await client.EnsureMonitoredAsync(target);
            var historyId = await client.GetLastGrabHistoryIdAsync(target);

            var job = new RedownloadJob
            {
                Id = Guid.NewGuid(),
                IsMovie = target.IsMovie,
                ArrId = target.ArrId,
                SeriesId = target.SeriesId,
                OldFileId = currentFile?.Id ?? target.FileId,
                DisplayTitle = target.DisplayTitle,
                OldFilePath = currentFile?.Path ?? target.FilePath,
                OldFileName = Path.GetFileName(currentFile?.Path ?? target.FilePath ?? string.Empty),
                OldQuality = currentFile?.Quality ?? target.Quality,
                OldSizeBytes = currentFile?.SizeBytes ?? target.SizeBytes,
                OldReleaseGroup = currentFile?.ReleaseGroup ?? target.ReleaseGroup,
                BlocklistedHistoryId = historyId,
                RequestedByUserId = requesterId,
                RequestedByUsername = requesterName,
                CreatedUtc = _utcNow(),
            };

            // S-2: structured entry log for the destructive flow, BEFORE anything destructive has
            // happened -- the fields here are exactly what an admin needs to correlate a Discord
            // report ("I clicked redownload and now X is gone") back to this job.
            _logger.LogInformation(
                "Redownload: BeginAsync requested by {RequesterId} ({RequesterName}) for '{DisplayTitle}' (IsMovie={IsMovie}, ArrId={ArrId}, OldFileId={OldFileId}, OldFilePath={OldFilePath})",
                requesterId, requesterName, job.DisplayTitle, job.IsMovie, job.ArrId, job.OldFileId, job.OldFilePath);

            var content = BuildBaseMessage(job);
            var (postedChannelId, postedMessageId) = await _messenger.PostPublicAsync(channelId, content);
            job.ChannelId = postedChannelId;
            job.MessageId = postedMessageId;

            // Persist BEFORE the delete. DeadlineUtc MUST be computed here, not left at its
            // default: an unset deadline would make the very next poll tick treat this job as
            // already past-deadline and attempt a restore while BeginAsync is still mid-flight.
            job.State = RedownloadJobState.Searching;
            job.DeadlineUtc = _utcNow().AddMinutes(timeoutMinutes);
            _repo.Add(job);

            var fileIdToDelete = currentFile?.Id ?? target.FileId;
            try
            {
                await client.DeleteFileAsync(fileIdToDelete);
                _logger.LogInformation("Redownload: deleted old file {FileId} for job {JobId} ('{DisplayTitle}')", fileIdToDelete, job.Id, job.DisplayTitle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Redownload: deleting the old file failed for '{job.DisplayTitle}' (the job is already persisted -- worst case the deadline passes, restore finds nothing to import, and the job lands on RestoreFailed rather than being lost): {ex.Message}");
            }

            if (historyId.HasValue)
            {
                try
                {
                    await client.MarkHistoryFailedAsync(historyId.Value);
                    _logger.LogInformation("Redownload: blocklisted grab history {HistoryId} for job {JobId} ('{DisplayTitle}')", historyId.Value, job.Id, job.DisplayTitle);
                }
                catch (Exception ex)
                {
                    // The already-posted public message optimistically said "blocklisted" (it
                    // was written before this call ran). Correct the persisted record so every
                    // later rewrite (success/restore/restore-failed edit) tells the truth
                    // instead of repeating a claim that never actually happened.
                    job.BlocklistedHistoryId = null;
                    _logger.LogError(ex, $"Redownload: blocklisting grab history {historyId.Value} failed for '{job.DisplayTitle}' (the deleted file is NOT orphaned — the job still persists and will resolve via search or timeout restore): {ex.Message}");
                }
            }

            try
            {
                await client.TriggerSearchAsync(target);
                _logger.LogInformation("Redownload: triggered search for job {JobId} ('{DisplayTitle}', ArrId={ArrId})", job.Id, job.DisplayTitle, job.ArrId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Redownload: triggering search failed for '{job.DisplayTitle}' (the deleted file is NOT orphaned — the job still persists and will resolve via timeout restore if no manual/automatic search succeeds): {ex.Message}");
            }

            _repo.Update(job);

            return job;
        }

        // ---- CheckJobAsync -------------------------------------------------------

        public async Task CheckJobAsync(RedownloadJob job)
        {
            // State-gate BEFORE anything else: a job that already resolved (Completed/
            // Restored/RestoreFailed) must never be re-processed, even if CheckJobAsync is
            // called again for it directly. This is a superset of the brief's "restore at most
            // once" rule -- it also protects against a restored file's freshly assigned fileId
            // being misread as a "new file arrived" success on a stale/duplicate call.
            if (job.State != RedownloadJobState.Searching)
            {
                return;
            }

            var client = _clientSelector(job.IsMovie);
            var target = TargetFromJob(job);

            var currentFile = await client.GetCurrentFileAsync(target);

            if (currentFile != null && currentFile.Id != 0 && currentFile.Id != job.OldFileId)
            {
                await HandleSuccessAsync(job, currentFile);
                return;
            }

            if (_utcNow() < job.DeadlineUtc)
            {
                return;
            }

            // Claim this job's restore slot atomically before doing anything else. The
            // State-gate at the top of this method reads a possibly-stale State off a
            // snapshot/shared job reference and is NOT sufficient on its own to prevent two
            // concurrent callers (overlapping poll ticks, or a poll tick racing a direct
            // CheckJobAsync call) from both reaching this point before either has persisted a
            // state change. Once claimed here, this job's restore is never attempted again for
            // the lifetime of this engine instance.
            lock (_restoreLock)
            {
                if (!_inFlightRestores.Add(job.Id))
                {
                    return;
                }
            }

            await HandleDeadlineAsync(job, client, target);
        }

        private async Task HandleSuccessAsync(RedownloadJob job, MediaFileInfo currentFile)
        {
            var elapsedMinutes = (int)(_utcNow() - job.CreatedUtc).TotalMinutes;
            var sizeGb = currentFile.SizeBytes / 1_000_000_000.0;
            var content = BuildBaseMessage(job) +
                $"\n✅ Replaced with {currentFile.Quality} · {sizeGb.ToString("F1", CultureInfo.InvariantCulture)} GB · {currentFile.ReleaseGroup} · {elapsedMinutes}m";

            // State is persisted BEFORE the edit is attempted: a permanently-failing edit (e.g.
            // the Discord message was deleted) must not leave this job stuck in Searching
            // forever, polling the arr API on every tick indefinitely.
            job.State = RedownloadJobState.Completed;
            _repo.Update(job);

            _logger.LogInformation("Redownload: job {JobId} ('{DisplayTitle}') Completed", job.Id, job.DisplayTitle);

            await EditPublicSafeAsync(job, content);
        }

        private async Task HandleDeadlineAsync(RedownloadJob job, IRedownloadMediaClient client, RedownloadTarget target)
        {
            bool restored;
            try
            {
                restored = await client.RestoreFromRecycleBinAsync(target, job.OldFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Redownload: restore from recycle bin threw for '{job.DisplayTitle}': {ex.Message}");
                restored = false;
            }

            // State-write immediately after the attempt, before the (possibly failing) message
            // edit -- this is what makes the state-gate/claim above effective at preventing a
            // second restore attempt.
            job.State = restored ? RedownloadJobState.Restored : RedownloadJobState.RestoreFailed;
            _repo.Update(job);

            _logger.LogInformation("Redownload: job {JobId} ('{DisplayTitle}') {State}", job.Id, job.DisplayTitle, job.State);

            var suffix = restored
                ? "\n↩️ Nothing better found — original restored"
                : "\n⚠️ No replacement found and restoring the original failed — an admin needs to check the recycle bin.";

            var content = BuildBaseMessage(job) + suffix;
            await EditPublicSafeAsync(job, content);
        }

        // Both success and restore paths persist State BEFORE attempting the edit (see above);
        // this helper makes sure a permanently-failing edit (e.g. Discord message deleted) can
        // never surface as an unhandled exception either -- the job is already durably resolved,
        // so a stale/missing status message is an acceptable, logged degradation, not a retry
        // trigger.
        private async Task EditPublicSafeAsync(RedownloadJob job, string content)
        {
            try
            {
                await _messenger.EditPublicAsync(job.ChannelId, job.MessageId, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Redownload: status message edit failed for '{job.DisplayTitle}' (job state already persisted; message may be stale): {ex.Message}");
            }
        }

        // ---- Start / StopAsync -------------------------------------------------------

        // Resume: loads repo.GetActive(), starts polling loop (Task.Run + CancellationToken,
        // MusicNotificationEngine pattern).
        public void Start()
        {
            // Re-entrancy guard: a second Start() call while a poll loop is already running
            // (e.g. ChatBot-style stop/start around a settings save racing the engine's own
            // lifecycle, or simple caller error) must be a no-op, not a second concurrent loop
            // double-processing every active job.
            if (_pollTask != null && !_pollTask.IsCompleted)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _pollTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var active = _repo.GetActive();

                        foreach (var job in active)
                        {
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            try
                            {
                                await CheckJobAsync(job);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Redownload: error checking job {job.Id} ('{job.DisplayTitle}'): {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Redownload: error polling active jobs: " + ex.Message);
                    }

                    try
                    {
                        await Task.Delay(_pollInterval, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }, token);
        }

        public async Task StopAsync()
        {
            if (_cts == null)
            {
                return;
            }

            _cts.Cancel();

            var task = _pollTask;
            if (task == null)
            {
                return;
            }

            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed == task)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Redownload: polling task ended with an error: " + ex.Message);
                }
            }
            else
            {
                _logger.LogError("Redownload: polling task did not stop within the shutdown timeout.");
            }
        }

        // ---- helpers -------------------------------------------------------

        // Edits (and the initial post) always rewrite the full message from the job's persisted
        // fields, never from in-memory string state — this keeps every edit idempotent and
        // restart-safe (Start() resuming a job has never seen the original in-memory text).
        private static string BuildBaseMessage(RedownloadJob job)
        {
            var sizeGb = job.OldSizeBytes / 1_000_000_000.0;
            var blocklistNote = job.BlocklistedHistoryId.HasValue
                ? "blocklisted"
                : "⚠️ couldn't blocklist (no grab history) — the same release may return";

            return $"🔁 **{job.RequestedByUsername}** is replacing **{job.DisplayTitle}**\n" +
                   $"deleted {job.OldQuality} · {sizeGb.ToString("F1", CultureInfo.InvariantCulture)} GB · {job.OldReleaseGroup} — {blocklistNote} — searching…";
        }

        private static RedownloadTarget TargetFromJob(RedownloadJob job)
        {
            return new RedownloadTarget
            {
                IsMovie = job.IsMovie,
                ArrId = job.ArrId,
                SeriesId = job.SeriesId,
                FileId = job.OldFileId,
                DisplayTitle = job.DisplayTitle,
                FilePath = job.OldFilePath,
                Quality = job.OldQuality,
                SizeBytes = job.OldSizeBytes,
                ReleaseGroup = job.OldReleaseGroup,
            };
        }
    }
}
