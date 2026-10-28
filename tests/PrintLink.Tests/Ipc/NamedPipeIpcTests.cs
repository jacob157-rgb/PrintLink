using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PrintLink.Core.Enums;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Ipc;
using PrintLink.Core.Models;
using PrintLink.Service.Ipc;
using PrintLink.Storage;
using PrintLink.Storage.Repositories;

namespace PrintLink.Tests.Ipc;

public class NamedPipeIpcTests : IDisposable
{
    private class DummyPrinterEnumerator : IPrinterEnumerator
    {
        public IReadOnlyList<PrinterInfo> GetPrinters() =>
        [
            new PrinterInfo { Name = "Shared_Test_Printer", IsShared = true }
        ];

        public PrinterInfo? GetPrinter(string name) => GetPrinters().FirstOrDefault();
    }

    private class DummyPrinterSpooler : IPrinterSpooler
    {
        public SpoolResult PrintBytes(string printerName, string documentTitle, byte[] data, string dataType = "RAW") => SpoolResult.Succeeded(101, data.Length);
        public Task<SpoolResult> PrintStreamAsync(string printerName, string documentTitle, Stream dataStream, string dataType = "RAW", CancellationToken cancellationToken = default) => Task.FromResult(SpoolResult.Succeeded(101, 0));
        public bool CancelJob(string printerName, int spoolerJobId) => true;
        public bool PauseJob(string printerName, int spoolerJobId) => true;
        public bool ResumeJob(string printerName, int spoolerJobId) => true;
    }

    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;

    public NamedPipeIpcTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"printlink_ipc_test_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
    }

    [Fact]
    public async Task IpcServer_HandlesGetStatusAndSetMode_Successfully()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var configRepo = new SqliteConfigRepository(_connectionFactory);
        var jobRepo = new SqlitePrintJobRepository(_connectionFactory);
        var enumerator = new DummyPrinterEnumerator();
        var spooler = new DummyPrinterSpooler();

        string testPipeName = $"PrintLink_Test_Pipe_{Guid.NewGuid():N}";
        var handler = new ServiceIpcHandler(configRepo, jobRepo, enumerator, spooler, () => true, 8270);
        await using var server = new NamedPipeIpcServer(handler, NullLogger<NamedPipeIpcServer>.Instance, testPipeName);
        server.Start();

        var client = new NamedPipeIpcClient(testPipeName);

        // 1. Send "GetStatus" request
        var statusResponse = await client.SendRequestAsync(new IpcRequest("GetStatus"));
        Assert.True(statusResponse.Success);
        Assert.NotNull(statusResponse.DataJson);

        var status = JsonSerializer.Deserialize<ServiceStatusData>(statusResponse.DataJson);
        Assert.NotNull(status);
        Assert.True(status.IsServerRunning);
        Assert.Equal(8270, status.ServerPort);
        Assert.Equal(1, status.SharedPrinterCount);

        // 2. Send "SetMode" request
        var setModeResponse = await client.SendRequestAsync(new IpcRequest("SetMode", "\"Server\""));
        Assert.True(setModeResponse.Success);

        var updatedMode = await configRepo.GetOperatingModeAsync();
        Assert.Equal(OperatingMode.Server, updatedMode);

        // 3. Send "PrintTestPage" request
        var testPagePayload = JsonSerializer.Serialize(new PrintTestPageIpcPayload("EPSON-TM-T88VI"));
        var testPageResponse = await client.SendRequestAsync(new IpcRequest("PrintTestPage", testPagePayload));
        Assert.True(testPageResponse.Success);
        Assert.NotNull(testPageResponse.DataJson);

        // 4. Send "CancelJob" request
        var cancelPayload = JsonSerializer.Serialize(new CancelJobIpcPayload("EPSON-TM-T88VI", 101));
        var cancelResponse = await client.SendRequestAsync(new IpcRequest("CancelJob", cancelPayload));
        Assert.True(cancelResponse.Success);
        Assert.NotNull(cancelResponse.DataJson);

        // 5. Send unknown action
        var unknownResponse = await client.SendRequestAsync(new IpcRequest("InvalidActionXYZ"));
        Assert.False(unknownResponse.Success);
        Assert.Contains("Unknown IPC action", unknownResponse.ErrorMessage);
    }

    [Fact]
    public async Task IpcServer_SetMode_TriggersOnModeChangeCallback()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var configRepo = new SqliteConfigRepository(_connectionFactory);
        var jobRepo = new SqlitePrintJobRepository(_connectionFactory);
        var enumerator = new DummyPrinterEnumerator();
        var spooler = new DummyPrinterSpooler();

        OperatingMode? receivedMode = null;
        string testPipeName = $"PrintLink_Test_Pipe_{Guid.NewGuid():N}";
        var handler = new ServiceIpcHandler(configRepo, jobRepo, enumerator, spooler, () => true, 8270, mode =>
        {
            receivedMode = mode;
            return Task.CompletedTask;
        });

        await using var server = new NamedPipeIpcServer(handler, NullLogger<NamedPipeIpcServer>.Instance, testPipeName);
        server.Start();

        var client = new NamedPipeIpcClient(testPipeName);
        var setModeResponse = await client.SendRequestAsync(new IpcRequest("SetMode", "\"Client\""));

        Assert.True(setModeResponse.Success);
        Assert.Equal(OperatingMode.Client, receivedMode);
        Assert.Equal(OperatingMode.Client, await configRepo.GetOperatingModeAsync());
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
        }
    }
}
