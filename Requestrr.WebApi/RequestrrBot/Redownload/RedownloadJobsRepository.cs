using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public class RedownloadJobsRepository
    {
        private readonly object _lock = new object();
        private readonly string _filePath;
        private List<RedownloadJob> _jobs;

        public RedownloadJobsRepository()
            : this(Path.Combine(SettingsFile.SettingsFolder, "redownload-jobs.json"))
        {
        }

        public RedownloadJobsRepository(string filePath)
        {
            _filePath = filePath;
            _jobs = Load(_filePath);
        }

        private static List<RedownloadJob> Load(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<RedownloadJob>();

            try
            {
                var text = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(text))
                    return new List<RedownloadJob>();

                var jobs = JsonConvert.DeserializeObject<List<RedownloadJob>>(text);
                return jobs ?? new List<RedownloadJob>();
            }
            catch (JsonException)
            {
                return new List<RedownloadJob>();
            }
        }

        private void Save()
        {
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(_jobs));
        }

        public IReadOnlyList<RedownloadJob> GetActive()
        {
            lock (_lock)
            {
                return _jobs.Where(j => j.State == RedownloadJobState.Searching).ToList();
            }
        }

        public bool HasActiveFor(bool isMovie, int arrId)
        {
            lock (_lock)
            {
                return _jobs.Any(j => j.State == RedownloadJobState.Searching && j.IsMovie == isMovie && j.ArrId == arrId);
            }
        }

        public void Add(RedownloadJob job)
        {
            lock (_lock)
            {
                _jobs.Add(job);
                Save();
            }
        }

        public void Update(RedownloadJob job)
        {
            lock (_lock)
            {
                var index = _jobs.FindIndex(j => j.Id == job.Id);
                if (index == -1)
                    throw new InvalidOperationException($"RedownloadJob with Id '{job.Id}' was not found; Update only replaces jobs already tracked by the repository.");

                _jobs[index] = job;
                Save();
            }
        }
    }
}
