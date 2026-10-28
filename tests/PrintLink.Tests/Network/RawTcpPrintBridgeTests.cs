using System.Net.Sockets;
using System.Text;
using PrintLink.Network.Bridge;

namespace PrintLink.Tests.Network;

public class RawTcpPrintBridgeTests
{
    [Fact]
    public void ExtractDocumentTitle_WithPjlJobName_ReturnsParsedTitle()
    {
        string pjlHeader = "\x1B%-12345X@PJL\r\n@PJL SET JOBNAME = \"Sales_Report_Q3.pdf\"\r\n@PJL ENTER LANGUAGE = PCLXL\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(pjlHeader);

        string title = RawTcpPrintBridge.ExtractDocumentTitle(bytes);

        Assert.Equal("Sales_Report_Q3.pdf", title);
    }

    [Fact]
    public void ExtractDocumentTitle_WithPostScriptTitle_ReturnsParsedTitle()
    {
        string psHeader = "%!PS-Adobe-3.0\r\n%%Title: Invoice_2026_001.docx\r\n%%Creator: Microsoft Word\r\n%%Pages: 2\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(psHeader);

        string title = RawTcpPrintBridge.ExtractDocumentTitle(bytes);

        Assert.Equal("Invoice_2026_001.docx", title);
    }

    [Fact]
    public void ExtractDocumentTitle_WithRawArbitraryData_ReturnsDefaultTitle()
    {
        byte[] bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        string title = RawTcpPrintBridge.ExtractDocumentTitle(bytes);

        Assert.StartsWith("PrintJob_", title);
    }

    [Fact]
    public async Task RawTcpPrintBridge_EndToEnd_ReceivesAndForwardsPrintJob()
    {
        int testPort = PrintBridgeManager.AllocateAvailablePort(startPort: 9200, endPort: 9299);
        await using var bridge = new RawTcpPrintBridge(testPort, "Kyocera_ECOSYS", "Kyocera (PrintLink)");

        Guid capturedJobId = Guid.Empty;
        string capturedPrinter = "";
        string capturedDoc = "";
        byte[]? capturedData = null;
        var tcs = new TaskCompletionSource<bool>();

        bridge.OnSubmitJob = (jobId, printer, doc, data) =>
        {
            capturedJobId = jobId;
            capturedPrinter = printer;
            capturedDoc = doc;
            capturedData = data;
            tcs.SetResult(true);
            return Task.FromResult((true, (int?)101, (string?)null));
        };

        bridge.Start();
        Assert.True(bridge.IsRunning);

        // Send simulated print stream from Windows Spooler client
        using (var client = new TcpClient())
        {
            await client.ConnectAsync("127.0.0.1", testPort);
            using var stream = client.GetStream();
            byte[] payload = Encoding.UTF8.GetBytes("@PJL SET JOBNAME = \"Test_Spooler_Job.txt\"\r\nHello from Windows App via Spooler\r\n\x0C");
            await stream.WriteAsync(payload, 0, payload.Length);
            await stream.FlushAsync();
            client.Client.Shutdown(SocketShutdown.Send); // Spooler signals EOF
        }

        // Wait for bridge to process job
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, completed);

        Assert.NotEqual(Guid.Empty, capturedJobId);
        Assert.Equal("Kyocera_ECOSYS", capturedPrinter);
        Assert.Equal("Test_Spooler_Job.txt", capturedDoc);
        Assert.NotNull(capturedData);
        Assert.Contains("Hello from Windows App via Spooler", Encoding.UTF8.GetString(capturedData));

        await bridge.StopAsync();
        Assert.False(bridge.IsRunning);
    }

    [Fact]
    public async Task PrintBridgeManager_Lifecycle_StartsAndStopsMultipleBridges()
    {
        await using var manager = new PrintBridgeManager();

        int port1 = PrintBridgeManager.AllocateAvailablePort(9300, 9350);
        int port2 = PrintBridgeManager.AllocateAvailablePort(9351, 9399);

        var bridge1 = await manager.StartBridgeAsync("Printer1", "Queue1", port1, (_, _, _, _) => Task.FromResult((true, (int?)1, (string?)null)));
        var bridge2 = await manager.StartBridgeAsync("Printer2", "Queue2", port2, (_, _, _, _) => Task.FromResult((true, (int?)2, (string?)null)));

        Assert.True(manager.IsBridgeRunning("Queue1"));
        Assert.True(manager.IsBridgeRunning("Queue2"));
        Assert.Equal(port1, manager.GetBridgePort("Queue1"));
        Assert.Equal(port2, manager.GetBridgePort("Queue2"));
        Assert.Equal(2, manager.ActiveBridges.Count);

        await manager.StopBridgeAsync("Queue1");
        Assert.False(manager.IsBridgeRunning("Queue1"));
        Assert.True(manager.IsBridgeRunning("Queue2"));

        await manager.StopAllAsync();
        Assert.Empty(manager.ActiveBridges);
    }
}
