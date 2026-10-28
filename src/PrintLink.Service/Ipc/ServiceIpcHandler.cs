using System.Text.Json;
using PrintLink.Core.Enums;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Ipc;
using PrintLink.Storage.Repositories;

namespace PrintLink.Service.Ipc;

public sealed class ServiceIpcHandler : IIpcRequestHandler
{
    private readonly IConfigRepository _configRepo;
    private readonly IPrintJobRepository _jobRepo;
    private readonly IPrinterEnumerator _printerEnumerator;
    private readonly IPrinterSpooler _printerSpooler;
    private readonly Func<bool> _isServerRunning;
    private readonly int _serverPort;
    private readonly Func<OperatingMode, Task>? _onModeChange;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public ServiceIpcHandler(
        IConfigRepository configRepo,
        IPrintJobRepository jobRepo,
        IPrinterEnumerator printerEnumerator,
        IPrinterSpooler printerSpooler,
        Func<bool> isServerRunning,
        int serverPort = 8270,
        Func<OperatingMode, Task>? onModeChange = null)
    {
        _configRepo = configRepo;
        _jobRepo = jobRepo;
        _printerEnumerator = printerEnumerator;
        _printerSpooler = printerSpooler;
        _isServerRunning = isServerRunning;
        _serverPort = serverPort;
        _onModeChange = onModeChange;
    }

    public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        try
        {
            switch (request.Action)
            {
                case "GetStatus":
                {
                    var mode = await _configRepo.GetOperatingModeAsync(cancellationToken);
                    var recentJobs = await _jobRepo.GetRecentJobsAsync(1, cancellationToken);
                    var printers = _printerEnumerator.GetPrinters();

                    var status = new ServiceStatusData(
                        Mode: mode,
                        IsServerRunning: _isServerRunning(),
                        ServerPort: _serverPort,
                        Uptime: DateTimeOffset.UtcNow - _startedAt,
                        SharedPrinterCount: printers.Count(p => p.IsShared),
                        RecentJobCount: recentJobs.Count,
                        MachineName: Environment.MachineName,
                        Version: "1.0.0");

                    return IpcResponse.Ok(JsonSerializer.Serialize(status));
                }

                case "SetMode":
                {
                    if (request.PayloadJson != null && Enum.TryParse<OperatingMode>(request.PayloadJson.Trim('"'), out var newMode))
                    {
                        await _configRepo.SetOperatingModeAsync(newMode, cancellationToken);
                        if (_onModeChange != null)
                        {
                            await _onModeChange(newMode);
                        }
                        return IpcResponse.Ok(JsonSerializer.Serialize(new { NewMode = newMode.ToString() }));
                    }
                    return IpcResponse.Error("Invalid or missing OperatingMode payload.");
                }

                case "GetRecentJobs":
                {
                    var jobs = await _jobRepo.GetRecentJobsAsync(50, cancellationToken);
                    return IpcResponse.Ok(JsonSerializer.Serialize(jobs));
                }

                case "GetPrinters":
                {
                    var printers = _printerEnumerator.GetPrinters();
                    return IpcResponse.Ok(JsonSerializer.Serialize(printers));
                }

                case "PrintTestPage":
                {
                    if (string.IsNullOrWhiteSpace(request.PayloadJson))
                        return IpcResponse.Error("Missing payload for PrintTestPage.");

                    var payload = JsonSerializer.Deserialize<PrintTestPageIpcPayload>(request.PayloadJson);
                    if (payload == null || string.IsNullOrWhiteSpace(payload.PrinterName))
                        return IpcResponse.Error("Invalid printer name.");

                    var testBytes = PrintLink.Core.Utilities.TestPageGenerator.GenerateRawTestPage(
                        payload.PrinterName,
                        serverName: Environment.MachineName,
                        clientName: "PrintLink_IPC_Service");

                    var result = _printerSpooler.PrintBytes(payload.PrinterName, "PrintLink_Test_Page", testBytes, "RAW");
                    var jobId = Guid.NewGuid();
                    await _jobRepo.GetOrRegisterJobAsync(jobId, payload.PrinterName, "PrintLink_Service_IPC", "PrintLink_Test_Page", testBytes.Length, cancellationToken);

                    if (result.Success)
                    {
                        await _jobRepo.UpdateJobStateAsync(jobId, JobState.Completed, result.SpoolerJobId, cancellationToken: cancellationToken);
                        return IpcResponse.Ok(JsonSerializer.Serialize(new { Success = true, SpoolerJobId = result.SpoolerJobId, JobId = jobId }));
                    }
                    else
                    {
                        await _jobRepo.UpdateJobStateAsync(jobId, JobState.Failed, errorCode: $"PL-SPOOL-{result.Win32ErrorCode:D4}", errorMessage: result.ErrorMessage, cancellationToken: cancellationToken);
                        return IpcResponse.Error($"Spooler failed: {result.ErrorMessage}");
                    }
                }

                case "CancelJob":
                {
                    if (string.IsNullOrWhiteSpace(request.PayloadJson))
                        return IpcResponse.Error("Missing payload for CancelJob.");

                    var payload = JsonSerializer.Deserialize<CancelJobIpcPayload>(request.PayloadJson);
                    if (payload == null)
                        return IpcResponse.Error("Invalid CancelJob payload.");

                    bool cancelled = false;
                    if (payload.SpoolerJobId > 0 && !string.IsNullOrWhiteSpace(payload.PrinterName))
                    {
                        cancelled = _printerSpooler.CancelJob(payload.PrinterName, payload.SpoolerJobId);
                    }

                    if (payload.JobId.HasValue)
                    {
                        await _jobRepo.UpdateJobStateAsync(payload.JobId.Value, JobState.Cancelled, errorMessage: "Cancelled by user via IPC", cancellationToken: cancellationToken);
                    }

                    return IpcResponse.Ok(JsonSerializer.Serialize(new { Cancelled = cancelled }));
                }

                default:
                    return IpcResponse.Error($"Unknown IPC action '{request.Action}'.");
            }
        }
        catch (Exception ex)
        {
            return IpcResponse.Error($"IPC Handler Exception: {ex.Message}");
        }
    }
}
