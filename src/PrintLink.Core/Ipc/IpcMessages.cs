using PrintLink.Core.Enums;

namespace PrintLink.Core.Ipc;

public static class IpcConstants
{
    public const string PipeName = "PrintLink_IPC_v1";
}

public record IpcRequest(string Action, string? PayloadJson = null);

public record IpcResponse(bool Success, string? DataJson = null, string? ErrorMessage = null)
{
    public static IpcResponse Ok(string? dataJson = null) => new(true, dataJson);
    public static IpcResponse Error(string message) => new(false, null, message);
}

public record ServiceStatusData(
    OperatingMode Mode,
    bool IsServerRunning,
    int ServerPort,
    TimeSpan Uptime,
    int SharedPrinterCount,
    int RecentJobCount,
    string MachineName,
    string Version);

public record CancelJobIpcPayload(string PrinterName, int SpoolerJobId, Guid? JobId = null);

public record PrintTestPageIpcPayload(string PrinterName);
