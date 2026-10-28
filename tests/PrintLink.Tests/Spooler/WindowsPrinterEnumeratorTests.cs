using PrintLink.Spooler;

namespace PrintLink.Tests.Spooler;

public class WindowsPrinterEnumeratorTests
{
    [Fact]
    public void GetPrinters_ReturnsNonEmptyListOfWindowsPrinters()
    {
        var enumerator = new WindowsPrinterEnumerator();

        var printers = enumerator.GetPrinters();

        Assert.NotNull(printers);
        Assert.NotEmpty(printers);

        foreach (var p in printers)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name), "Printer name should not be blank");
        }
    }

    [Fact]
    public void GetPrinter_WithInvalidName_ReturnsNull()
    {
        var enumerator = new WindowsPrinterEnumerator();

        var printer = enumerator.GetPrinter("NonExistent_Printer_123456789_XYZ");

        Assert.Null(printer);
    }
}
