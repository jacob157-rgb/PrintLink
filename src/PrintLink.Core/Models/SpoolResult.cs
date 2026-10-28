namespace PrintLink.Core.Models;

/// <summary>
/// Result of a Windows Print Spooler submission operation.
/// </summary>
public record SpoolResult
{
    public bool Success { get; init; }
    public int SpoolerJobId { get; init; }
    public long BytesWritten { get; init; }
    public int Win32ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static SpoolResult Succeeded(int spoolerJobId, long bytesWritten) =>
        new()
        {
            Success = true,
            SpoolerJobId = spoolerJobId,
            BytesWritten = bytesWritten,
            Win32ErrorCode = 0,
            ErrorMessage = null
        };

    public static SpoolResult Failed(int win32ErrorCode, string errorMessage, int spoolerJobId = 0, long bytesWritten = 0) =>
        new()
        {
            Success = false,
            SpoolerJobId = spoolerJobId,
            BytesWritten = bytesWritten,
            Win32ErrorCode = win32ErrorCode,
            ErrorMessage = errorMessage
        };
}
