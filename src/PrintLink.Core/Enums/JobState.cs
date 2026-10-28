namespace PrintLink.Core.Enums;

/// <summary>
/// Deterministic lifecycle states for a PrintLink print job.
/// </summary>
public enum JobState
{
    /// <summary>
    /// Job is queued locally on the client awaiting network transmission.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Payload bytes are currently streaming over the secure TLS connection.
    /// </summary>
    Sending = 1,

    /// <summary>
    /// Server received the complete payload, verified client permissions, and persisted metadata.
    /// </summary>
    Accepted = 2,

    /// <summary>
    /// Server is actively writing job data into the Windows Print Spooler (winspool.drv).
    /// </summary>
    Spooling = 3,

    /// <summary>
    /// Windows Spooler accepted all bytes and successfully closed the document.
    /// Note: Does not guarantee physical paper exit if the printer lacks hardware feedback.
    /// </summary>
    Completed = 4,

    /// <summary>
    /// Job failed due to spooler rejection, network abort, or authorization error.
    /// </summary>
    Failed = 5,

    /// <summary>
    /// Outcome is uncertain (e.g. connection lost mid-acknowledgement).
    /// Requires status reconciliation; MUST NEVER be blindly resent.
    /// </summary>
    Unknown = 6,

    /// <summary>
    /// Job was cancelled by user or administrator prior to completion.
    /// </summary>
    Cancelled = 7
}
