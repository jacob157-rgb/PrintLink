using System.Text;
using PrintLink.Core.Utilities;

namespace PrintLink.Tests.Core;

public class TestPageGeneratorTests
{
    [Fact]
    public void GenerateRawTestPage_ProducesValidContentWithMandatorySections()
    {
        // Act
        byte[] pageBytes = TestPageGenerator.GenerateRawTestPage(
            printerName: "EPSON-TM-T88VI",
            serverName: "HOST-SERVER-01",
            clientName: "POS-TERMINAL-03",
            driverName: "Advanced Receipt Driver",
            portName: "COM1");

        string content = Encoding.UTF8.GetString(pageBytes);

        // Assert
        Assert.NotEmpty(pageBytes);
        Assert.Contains("L-NET PRINTLINK TEST PAGE", content);
        Assert.Contains("EPSON-TM-T88VI", content);
        Assert.Contains("HOST-SERVER-01", content);
        Assert.Contains("POS-TERMINAL-03", content);
        Assert.Contains("Advanced Receipt Driver", content);
        Assert.Contains("RAW (Zero-Retention Stream)", content);
        Assert.Contains("Job Verification ID:", content);
        Assert.EndsWith("\x0C", content); // Must end with Form Feed for clean page eject
    }
}
