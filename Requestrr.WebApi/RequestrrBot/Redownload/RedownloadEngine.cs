using System;
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
        // flag, when there's no grab history) -> trigger search -> persist job. The blocklist
        // and search calls are each individually wrapped: once the file is gone, a failure in
        // either of those steps must never leave the deletion un-tracked, so the job is always
        // persisted at the end regardless of what happened after the delete. Failures are
        // logged (not surfaced in the already-posted public message, which was written before
        // either call ran and doesn't get edited again until CheckJobAsync's next outcome).
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

            var content = BuildBaseMessage(job);
            var (postedChannelId, postedMessageId) = await _messenger.PostPublicAsync(channelId, content);
            job.ChannelId = postedChannelId;
            job.MessageId = postedMessageId;

            var fileIdToDelete = currentFile?.Id ?? target.FileId;
            await client.DeleteFileAsync(fileIdToDelete);

            if (historyId.HasValue)
            {
                try
                {
                    await client.MarkHistoryFailedAsync(historyId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Redownload: blocklisting grab history {historyId.Value} failed for '{job.DisplayTitle}' (the deleted file is NOT orphaned — the job still persists and will resolve via search or timeout restore): {ex.Message}");
                }
            }

            try
            {
                await client.TriggerSearchAsync(target);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Redownload: triggering search failed for '{job.DisplayTitle}' (the deleted file is NOT orphaned — the job still persists and will resolve via timeout restore if no manual/automatic search succeeds): {ex.Message}");
            }

            job.State = RedownloadJobState.Searching;
            job.DeadlineUtc = _utcNow().AddMinutes(timeoutMinutes);

            _repo.Add(job);

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

            await HandleDeadlineAsync(job, client, target);
        }

        private async Task HandleSuccessAsync(RedownloadJob job, MediaFileInfo currentFile)
        {
            var elapsedMinutes = (int)(_utcNow() - job.CreatedUtc).TotalMinutes;
            var sizeGb = currentFile.SizeBytes / 1_000_000_000.0;
            var content = BuildBaseMessage(job) +
                $"\n✅ Replaced with {currentFile.Quality} · {sizeGb.ToString("F1", CultureInfo.InvariantCulture)} GB · {currentFile.ReleaseGroup} · {elapsedMinutes}m";

            await _messenger.EditPublicAsync(job.ChannelId, job.MessageId, content);

            job.State = RedownloadJobState.Completed;
            _repo.Update(job);
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
            // edit -- this is what makes the state-gate above effective at preventing a second
            // restore attempt.
            job.State = restored ? RedownloadJobState.Restored : RedownloadJobState.RestoreFailed;
            _repo.Update(job);

            var suffix = restored
                ? "\n↩️ Nothing better found — original restored"
                : "\n⚠️ No replacement found and restoring the original failed — an admin needs to check the recycle bin.";

            var content = BuildBaseMessage(job) + suffix;
            await _messenger.EditPublicAsync(job.ChannelId, job.MessageId, content);
        }

        // ---- Start / StopAsync -------------------------------------------------------

        // Resume: loads repo.GetActive(), starts polling loop (Task.Run + CancellationToken,
        // MusicNotificationEngine pattern).
        public void Start()
        {
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
