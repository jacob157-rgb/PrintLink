using PrintLink.Spooler;

namespace PrintLink.Tests.Spooler;

public class WindowsPrinterSpoolerTests
{
    [Fact]
    public void PrintBytes_WithNonExistentPrinter_ReturnsFailedResultWithWin32Error()
    {
        var spooler = new WindowsPrinterSpooler();
        byte[] dummyData = [0x1B, 0x40, 0x54, 0x65, 0x73, 0x74]; // ESC @ Test

        var result = spooler.PrintBytes("NonExistent_Fake_Printer_987654321", "TestDoc", dummyData);

        Assert.False(result.Success);
        Assert.NotEqual(0, result.Win32ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void PrintBytes_WithEmptyPrinterName_ThrowsArgumentException()
    {
        var spooler = new WindowsPrinterSpooler();
        byte[] dummyData = [0x01, 0x02];

        Assert.Throws<ArgumentException>(() => spooler.PrintBytes("", "TestDoc", dummyData));
    }

    [Fact]
    public void PrintBytes_WithNullData_ThrowsArgumentNullException()
    {
        var spooler = new WindowsPrinterSpooler();

        Assert.Throws<ArgumentNullException>(() => spooler.PrintBytes("Microsoft Print to PDF", "TestDoc", null!));
    }
}
