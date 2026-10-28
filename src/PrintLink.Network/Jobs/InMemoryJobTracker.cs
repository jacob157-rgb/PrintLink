using System.Collections.Concurrent;
using PrintLink.Core.Enums;
using PrintLink.Core.Models;

namespace PrintLink.Network.Jobs;

public interface IJobTracker
{
    (bool IsNew, PrintJob Job) GetOrRegisterJob(Guid jobId, string printerName, string clientId, string docName, long totalBytes);
    void UpdateJobState(Guid jobId, JobState state, int? spoolerJobId = null, string? errorCode = null, string? errorMessage = null);
    PrintJob? GetJob(Guid jobId);
    IReadOnlyList<PrintJob> GetRecentJobs(int limit = 50);
}

public sealed class InMemoryJobTracker : IJobTracker
{
    private readonly ConcurrentDictionary<Guid, PrintJob> _jobs = new();

    public (bool IsNew, PrintJob Job) GetOrRegisterJob(Guid jobId, string printerName, string clientId, string docName, long totalBytes)
    {
        bool isNew = false;
        var job = _jobs.GetOrAdd(jobId, id =>
        {
            isNew = true;
            return new PrintJob
            {
                JobId = id,
                PrinterName = printerName,
                ClientId = clientId,
                DocumentName = docName,
                TotalBytes = totalBytes,
                State = JobState.Accepted,
                CreatedAt = DateTimeOffset.UtcNow
            };
        });

        return (isNew, job);
    }

    public void UpdateJobState(Guid jobId, JobState state, int? spoolerJobId = null, string? errorCode = null, string? errorMessage = null)
    {
        if (_jobs.TryGetValue(jobId, out var existing))
        {
            _jobs[jobId] = existing with
            {
                State = state,
                SpoolerJobId = spoolerJobId ?? existing.SpoolerJobId,
                ErrorCode = errorCode ?? existing.ErrorCode,
                ErrorMessage = errorMessage ?? existing.ErrorMessage,
                CompletedAt = state is JobState.Completed or JobState.Failed ? DateTimeOffset.UtcNow : existing.CompletedAt
            };
        }
    }

    public PrintJob? GetJob(Guid jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public IReadOnlyList<PrintJob> GetRecentJobs(int limit = 50)
    {
        return _jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToList();
    }
}
