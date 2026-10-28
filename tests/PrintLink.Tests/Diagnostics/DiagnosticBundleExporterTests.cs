using System.IO.Compression;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Models;
using PrintLink.Diagnostics;
using PrintLink.Storage;
using PrintLink.Storage.Repositories;

namespace PrintLink.Tests.Diagnostics;

public class DiagnosticBundleExporterTests : IDisposable
{
    private class DummyEnumerator : IPrinterEnumerator
    {
        public IReadOnlyList<PrinterInfo> GetPrinters() =>
        [
            new PrinterInfo { Name = "Test_PDF_Printer", DriverName = "PDF_Driver", IsDefault = true, IsShared = true }
        ];

        public PrinterInfo? GetPrinter(string name) => GetPrinters().FirstOrDefault();
    }

    private readonly string _testDbPath;
    private readonly string _testZipPath;
    private readonly SqliteConnectionFactory _connectionFactory;

    public DiagnosticBundleExporterTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"printlink_diag_db_{Guid.NewGuid():N}.db");
        _testZipPath = Path.Combine(Path.GetTempPath(), $"printlink_diag_bundle_{Guid.NewGuid():N}.zip");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
    }

    [Fact]
    public async Task ExportBundleAsync_CreatesValidZipWithExpectedDiagnostics()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var jobRepo = new SqlitePrintJobRepository(_connectionFactory);
        var enumerator = new DummyEnumerator();

        await jobRepo.GetOrRegisterJobAsync(Guid.NewGuid(), "Test_PDF_Printer", "client-1", "Invoice#123", 4096);

        var exporter = new DiagnosticBundleExporter(enumerator, jobRepo);

        string generatedZip = await exporter.ExportBundleAsync(_testZipPath);

        Assert.True(File.Exists(generatedZip), "Diagnostic bundle zip file was not created.");

        using var archive = ZipFile.OpenRead(generatedZip);
        var entryNames = archive.Entries.Select(e => e.Name).ToList();

        Assert.Contains("environment_info.txt", entryNames);
        Assert.Contains("spooler_status.txt", entryNames);
        Assert.Contains("printers_inventory.json", entryNames);
        Assert.Contains("recent_jobs_metadata.json", entryNames);
    }

    [Fact]
    public async Task ConnectionTester_Localhost_ResolvesDnsSuccessfully()
    {
        var report = await ConnectionTester.TestConnectionAsync("127.0.0.1", port: 8270);

        Assert.NotNull(report);
        Assert.NotEmpty(report.Steps);
        Assert.True(report.Steps[0].Success, "DNS step should succeed for 127.0.0.1");
    }

    public void Dispose()
    {
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
        try { if (File.Exists(_testZipPath)) File.Delete(_testZipPath); } catch { }
    }
}
