using PrintLink.Core.Interfaces;
using PrintLink.Core.Models;
using PrintLink.Storage;
using PrintLink.Storage.Printers;
using PrintLink.Storage.Repositories;

namespace PrintLink.Tests.Storage;

public class ConfiguredPrinterEnumeratorTests : IDisposable
{
    private class DummyRawEnumerator : IPrinterEnumerator
    {
        public IReadOnlyList<PrinterInfo> GetPrinters() =>
        [
            new PrinterInfo { Name = "EPSON_L3250", IsShared = false },
            new PrinterInfo { Name = "Xprinter_XP", IsShared = false },
            new PrinterInfo { Name = "Native_SMB_Printer", IsShared = true, ShareName = "SmbShare" }
        ];

        public PrinterInfo? GetPrinter(string name) => GetPrinters().FirstOrDefault(p => p.Name == name);
    }

    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;

    public ConfiguredPrinterEnumeratorTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"printlink_cfg_enum_test_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
    }

    [Fact]
    public async Task ConfiguredPrinterEnumerator_MergesSharedStatusAndCustomShareName()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var configRepo = new SqliteConfigRepository(_connectionFactory);

        // Share EPSON_L3250 via PrintLink with a custom share name
        await configRepo.SetValueAsync("Printer:Shared:EPSON_L3250", "true");
        await configRepo.SetValueAsync("Printer:ShareName:EPSON_L3250", "FrontDesk_Receipt");

        // Explicitly unshare Native_SMB_Printer in PrintLink
        await configRepo.SetValueAsync("Printer:Shared:Native_SMB_Printer", "false");

        var rawEnumerator = new DummyRawEnumerator();
        var configuredEnumerator = new ConfiguredPrinterEnumerator(rawEnumerator, configRepo);

        var printers = configuredEnumerator.GetPrinters();

        var epson = printers.First(p => p.Name == "EPSON_L3250");
        Assert.True(epson.IsShared);
        Assert.Equal("FrontDesk_Receipt", epson.ShareName);
        Assert.Equal("FrontDesk_Receipt", epson.DisplayName);

        var xprinter = printers.First(p => p.Name == "Xprinter_XP");
        Assert.False(xprinter.IsShared);

        var smb = printers.First(p => p.Name == "Native_SMB_Printer");
        Assert.False(smb.IsShared); // overridden by PrintLink config
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
