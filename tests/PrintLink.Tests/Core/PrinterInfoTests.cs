using PrintLink.Core.Enums;
using PrintLink.Core.Models;

namespace PrintLink.Tests.Core;

public class PrinterInfoTests
{
    [Fact]
    public void DisplayName_PrefersShareNameWhenAvailable()
    {
        var printer = new PrinterInfo
        {
            Name = "HP_LaserJet_Pro_M404",
            ShareName = "Office_Main_Laser",
            IsShared = true
        };

        Assert.Equal("Office_Main_Laser", printer.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToNameWhenShareNameNullOrEmpty()
    {
        var printer = new PrinterInfo
        {
            Name = "Microsoft Print to PDF",
            ShareName = null,
            IsShared = false
        };

        Assert.Equal("Microsoft Print to PDF", printer.DisplayName);
    }

    [Fact]
    public void StatusDescription_ReportsReadyWhenNoFlagsSet()
    {
        var printer = new PrinterInfo
        {
            Name = "Test Printer",
            Status = PrinterStatusFlags.Ready
        };

        Assert.Equal("Ready", printer.StatusDescription);
    }

    [Fact]
    public void StatusDescription_ReportsCompoundFlagsCorrectly()
    {
        var printer = new PrinterInfo
        {
            Name = "Test Printer",
            Status = PrinterStatusFlags.Offline | PrinterStatusFlags.PaperOut
        };

        Assert.Contains("Offline", printer.StatusDescription);
        Assert.Contains("PaperOut", printer.StatusDescription);
    }

    [Fact]
    public void CustomForms_InitializesAndFormatsCorrectly()
    {
        var form = new CustomPageForm
        {
            Name = "Surat Jalan 21x14",
            WidthMm = 210.0,
            HeightMm = 140.0,
            IsCustom = true
        };

        var printer = new PrinterInfo
        {
            Name = "Epson LX-310",
            DriverName = "EPSON LX-310 ESC/P",
            CustomForms = [form],
            HasDriverPackage = true
        };

        Assert.Single(printer.CustomForms);
        Assert.Equal("Surat Jalan 21x14", printer.CustomForms[0].Name);
        Assert.Equal("210 × 140 mm", form.DisplaySize);
        Assert.True(printer.HasDriverPackage);
    }
}
