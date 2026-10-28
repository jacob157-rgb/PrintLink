using System.Diagnostics;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrintLink.Core.Interfaces;
using PrintLink.Storage.Repositories;

namespace PrintLink.Diagnostics;

public sealed class DiagnosticBundleExporter
{
    private readonly IPrinterEnumerator _printerEnumerator;
    private readonly IPrintJobRepository _jobRepo;

    public DiagnosticBundleExporter(IPrinterEnumerator printerEnumerator, IPrintJobRepository jobRepo)
    {
        _printerEnumerator = printerEnumerator;
        _jobRepo = jobRepo;
    }

    public async Task<string> ExportBundleAsync(string? targetZipPath = null, CancellationToken cancellationToken = default)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"PrintLink_Diagnostic_Bundle_{timestamp}.zip");

        string finalZipPath = targetZipPath ?? defaultPath;
        string tempDir = Path.Combine(Path.GetTempPath(), $"printlink_diag_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. System Environment Report
            var envReport = GenerateSystemEnvironmentReport();
            await File.WriteAllTextAsync(Path.Combine(tempDir, "environment_info.txt"), envReport, cancellationToken);

            // 2. Windows Spooler Service Status
            var spoolerReport = GenerateSpoolerStatusReport();
            await File.WriteAllTextAsync(Path.Combine(tempDir, "spooler_status.txt"), spoolerReport, cancellationToken);

            // 3. Printers Inventory Report
            var printers = _printerEnumerator.GetPrinters();
            string printersJson = JsonSerializer.Serialize(printers, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "printers_inventory.json"), printersJson, cancellationToken);

            // 4. Recent Jobs Metadata (Scrubbed - strictly NO document content)
            var recentJobs = await _jobRepo.GetRecentJobsAsync(100, cancellationToken);
            string jobsJson = JsonSerializer.Serialize(recentJobs, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "recent_jobs_metadata.json"), jobsJson, cancellationToken);

            // 5. Gather and Scrub Log Files (if exist)
            string logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LNet", "PrintLink", "logs");
            if (Directory.Exists(logsDir))
            {
                string scrubbedLogsDir = Path.Combine(tempDir, "logs");
                Directory.CreateDirectory(scrubbedLogsDir);

                foreach (var logFile in Directory.GetFiles(logsDir, "*.log"))
                {
                    string rawContent = await File.ReadAllTextAsync(logFile, cancellationToken);
                    string sanitized = SanitizeLogContent(rawContent);
                    string destFile = Path.Combine(scrubbedLogsDir, Path.GetFileName(logFile));
                    await File.WriteAllTextAsync(destFile, sanitized, cancellationToken);
                }
            }

            // 6. Compress into ZIP bundle
            if (File.Exists(finalZipPath))
            {
                File.Delete(finalZipPath);
            }

            ZipFile.CreateFromDirectory(tempDir, finalZipPath, CompressionLevel.Optimal, false);
            return finalZipPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
            }
        }
    }

    private static string GenerateSystemEnvironmentReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== L-NET PRINTLINK SYSTEM DIAGNOSTIC REPORT ===");
        sb.AppendLine($"Generated UTC:     {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Machine Name:      {Environment.MachineName}");
        sb.AppendLine($"OS Version:        {Environment.OSVersion} (64-bit: {Environment.Is64BitOperatingSystem})");
        sb.AppendLine($".NET Runtime:      {Environment.Version}");
        sb.AppendLine($"Processor Count:   {Environment.ProcessorCount}");
        sb.AppendLine($"System Directory:  {Environment.SystemDirectory}");
        sb.AppendLine();
        sb.AppendLine("=== NETWORK ADAPTERS ===");

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    var props = ni.GetIPProperties();
                    var ips = string.Join(", ", props.UnicastAddresses.Select(a => a.Address.ToString()));
                    sb.AppendLine($"[{ni.NetworkInterfaceType}] {ni.Name} ({ni.Description}): {ips}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error querying network adapters: {ex.Message}");
        }

        return sb.ToString();
    }

    private static string GenerateSpoolerStatusReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== WINDOWS PRINT SPOOLER (spoolsv.exe) DIAGNOSTIC ===");

        var processes = Process.GetProcessesByName("spoolsv");
        if (processes.Length > 0)
        {
            var p = processes[0];
            sb.AppendLine($"Spooler Process ID: {p.Id}");
            sb.AppendLine("Spooler Status:     Process Found (Active)");

            try
            {
                sb.AppendLine($"Responding:         {p.Responding}");
            }
            catch
            {
            }

            try
            {
                sb.AppendLine($"Working Set (RAM):  {p.WorkingSet64 / (1024 * 1024)} MB");
            }
            catch
            {
            }

            try
            {
                sb.AppendLine($"Start Time:         {p.StartTime:yyyy-MM-dd HH:mm:ss}");
            }
            catch
            {
            }
        }
        else
        {
            sb.AppendLine("WARNING: spoolsv.exe process is NOT running!");
        }

        return sb.ToString();
    }

    private static string SanitizeLogContent(string content)
    {
        // Scrub Bearer tokens, passwords, and private keys
        string sanitized = Regex.Replace(content, @"Bearer\s+[A-Za-z0-9_\-\.]+", "Bearer [REDACTED]");
        sanitized = Regex.Replace(sanitized, @"password[=:]\s*[\S]+", "password=[REDACTED]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"token[=:]\s*[\S]+", "token=[REDACTED]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"-----BEGIN [A-Z ]+ PRIVATE KEY-----[\s\S]+?-----END [A-Z ]+ PRIVATE KEY-----", "[PRIVATE_KEY_REDACTED]");
        return sanitized;
    }
}
