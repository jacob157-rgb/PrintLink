using PrintLink.Core.Models;
using PrintLink.Spooler;

namespace PrintLink.Tests.Spooler;

public class WindowsPrinterInstallerTests
{
    [Fact]
    public void ResolveBestDriver_WithGenericPreferredDriver_ReturnsGenericOrFallback()
    {
        string driver = WindowsPrinterInstaller.ResolveBestDriver("Generic / Text Only");
        Assert.False(string.IsNullOrWhiteSpace(driver));
    }

    [Fact]
    public void ResolveBestDriver_WithNullPreferredDriver_ReturnsFallbackDriver()
    {
        string driver = WindowsPrinterInstaller.ResolveBestDriver(null);
        Assert.False(string.IsNullOrWhiteSpace(driver));
    }

    [Fact]
    public void RegisterCustomForm_WithInvalidDimensions_ReturnsFalseGracefully()
    {
        var invalidForm = new CustomPageForm
        {
            Name = "",
            WidthMm = -10,
            HeightMm = 0
        };

        bool result = WindowsPrinterInstaller.RegisterCustomForm(invalidForm);
        Assert.False(result);
    }
}
