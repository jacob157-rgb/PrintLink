using System.Text;
using PrintLink.Core.Enums;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Models;
using PrintLink.Network.Auth;
using PrintLink.Network.Client;
using PrintLink.Network.Jobs;
using PrintLink.Network.Server;

namespace PrintLink.Tests.Network;

public class ServerClientIntegrationTests
{
    private class MockPrinterEnumerator : IPrinterEnumerator
    {
        public List<PrinterInfo> Printers { get; } =
        [
            new PrinterInfo { Name = "Office_Laser_1", ShareName = "OfficeLaser", IsShared = true },
            new PrinterInfo { Name = "Warehouse_Thermal_1", ShareName = "WarehouseThermal", IsShared = true }
        ];

        public IReadOnlyList<PrinterInfo> GetPrinters() => Printers;
        public PrinterInfo? GetPrinter(string name) => Printers.FirstOrDefault(p => p.Name == name || p.ShareName == name);
    }

    private class MockPrinterSpooler : IPrinterSpooler
    {
        public int CallCount { get; private set; }
        public long LastBytesWritten { get; private set; }
        public string? LastPrinter { get; private set; }

        public SpoolResult PrintBytes(string printerName, string documentTitle, byte[] data, string dataType = "RAW")
        {
            CallCount++;
            LastPrinter = printerName;
            LastBytesWritten = data.Length;
            return SpoolResult.Succeeded(42, data.Length);
        }

        public async Task<SpoolResult> PrintStreamAsync(string printerName, string documentTitle, Stream dataStream, string dataType = "RAW", CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrinter = printerName;
            using var ms = new MemoryStream();
            await dataStream.CopyToAsync(ms, cancellationToken);
            LastBytesWritten = ms.Length;
            return SpoolResult.Succeeded(42, LastBytesWritten);
        }

        public bool CancelJob(string printerName, int spoolerJobId) => true;
        public bool PauseJob(string printerName, int spoolerJobId) => true;
        public bool ResumeJob(string printerName, int spoolerJobId) => true;
    }

    [Fact]
    public async Task EndToEnd_ClientCanConnect_Authenticate_AndStreamPrintJob()
    {
        const int testPort = 18271;
        var mockEnumerator = new MockPrinterEnumerator();
        var mockSpooler = new MockPrinterSpooler();
        var authenticator = new TokenAuthenticator();
        var jobTracker = new InMemoryJobTracker();

        authenticator.RegisterClient("test-client", "Workstation-A", "valid-token-123");
        authenticator.SetPrinterAcl("OfficeLaser", ["test-client"]);
        authenticator.SetPrinterAcl("WarehouseThermal", ["other-client"]); // Restricted from test-client

        await using var server = new PrintLinkServerHost(testPort, mockEnumerator, mockSpooler, authenticator, jobTracker);
        await server.StartAsync();

        using var client = new PrintLinkClient($"http://127.0.0.1:{testPort}", "valid-token-123");

        // 1. Health check
        var health = await client.CheckHealthAsync();
        Assert.NotNull(health);
        Assert.Equal("OK", health.Status);

        // 2. Query printers (should only see OfficeLaser)
        var printers = await client.GetSharedPrintersAsync();
        Assert.NotNull(printers);
        Assert.Single(printers);
        Assert.Equal("OfficeLaser", printers[0].DisplayName);

        // 3. Submit print job
        var jobId = Guid.NewGuid();
        byte[] payload = Encoding.UTF8.GetBytes("Test Document Content for Spooler");
        using var payloadStream = new MemoryStream(payload);

        var submitResult = await client.SubmitJobAsync(jobId, "OfficeLaser", "Invoice #1001", payloadStream);
        Assert.Equal("Completed", submitResult.Status);
        Assert.Equal(42, submitResult.SpoolerJobId);
        Assert.Equal(payload.Length, submitResult.BytesWritten);
        Assert.False(submitResult.IsDuplicate);

        Assert.Equal(1, mockSpooler.CallCount);
        Assert.Equal("Office_Laser_1", mockSpooler.LastPrinter);
        Assert.Equal(payload.Length, mockSpooler.LastBytesWritten);

        // 4. Submit duplicate job with identical JobId (Idempotency test)
        using var duplicateStream = new MemoryStream(payload);
        var duplicateResult = await client.SubmitJobAsync(jobId, "OfficeLaser", "Invoice #1001", duplicateStream);
        Assert.True(duplicateResult.IsDuplicate);
        // Spooler should NOT have been called a second time!
        Assert.Equal(1, mockSpooler.CallCount);

        // 5. Query job status
        var status = await client.GetJobStatusAsync(jobId);
        Assert.NotNull(status);
        Assert.Equal(jobId, status.JobId);
        Assert.Equal(JobState.Completed, status.State);
    }

    [Fact]
    public async Task UnauthorizedClient_IsRejectedWith401()
    {
        const int testPort = 18272;
        var mockEnumerator = new MockPrinterEnumerator();
        var mockSpooler = new MockPrinterSpooler();
        var authenticator = new TokenAuthenticator();
        var jobTracker = new InMemoryJobTracker();

        await using var server = new PrintLinkServerHost(testPort, mockEnumerator, mockSpooler, authenticator, jobTracker);
        await server.StartAsync();

        using var client = new PrintLinkClient($"http://127.0.0.1:{testPort}", "invalid-token");

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.GetSharedPrintersAsync();
        });
    }

    [Fact]
    public async Task AnonymousClient_CanDiscoverSharedPrinters_WithoutToken()
    {
        const int testPort = 18273;
        var mockEnumerator = new MockPrinterEnumerator();
        mockEnumerator.Printers.Add(new PrinterInfo { Name = "Local_Unshared_PDF", IsShared = false });

        var mockSpooler = new MockPrinterSpooler();
        var authenticator = new TokenAuthenticator(allowAnonymousLanClients: true);
        var jobTracker = new InMemoryJobTracker();

        await using var server = new PrintLinkServerHost(testPort, mockEnumerator, mockSpooler, authenticator, jobTracker);
        await server.StartAsync();

        // Client connects with NO token (token is null/empty)
        using var client = new PrintLinkClient($"http://127.0.0.1:{testPort}", bearerToken: null);

        var printers = await client.GetSharedPrintersAsync();
        Assert.NotNull(printers);
        // Only the 2 shared printers should be returned, Local_Unshared_PDF must be excluded!
        Assert.Equal(2, printers.Count);
        Assert.Contains(printers, p => p.DisplayName == "OfficeLaser");
        Assert.Contains(printers, p => p.DisplayName == "WarehouseThermal");
        Assert.DoesNotContain(printers, p => p.Name == "Local_Unshared_PDF");
    }
}
