using PrintLink.Core.Enums;

namespace PrintLink.Core.Models;

/// <summary>
/// Domain model representing a print job and its observable state.
/// </summary>
public record PrintJob
{
    /// <summary>
    /// Stable client-generated unique identifier (UUIDv7 recommended for time-ordered sorting).
    /// </summary>
    public Guid JobId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Target printer name on the server.
    /// </summary>
    public required string PrinterName { get; init; }

    /// <summary>
    /// Originating client identifier.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Title or document name reported to the Windows Spooler.
    /// </summary>
    public required string DocumentName { get; init; }

    /// <summary>
    /// Total payload size in bytes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Current lifecycle state.
    /// </summary>
    public JobState State { get; init; } = JobState.Queued;

    /// <summary>
    /// Native job ID assigned by the Windows Print Spooler (winspool.drv).
    /// </summary>
    public int? SpoolerJobId { get; init; }

    /// <summary>
    /// Diagnostic error reference code if the job failed.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// User-friendly error message.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp when job was created on client or received by server.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp when job spooling finalized.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }
}
