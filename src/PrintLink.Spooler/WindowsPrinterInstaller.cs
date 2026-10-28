using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PrintLink.Core.Models;
using PrintLink.Spooler.Native;

namespace PrintLink.Spooler;

/// <summary>
/// Manages installation and uninstallation of Windows Printer queues and Standard TCP/IP ports
/// pointing to local loopback bridges, allowing any native Windows application to print.
/// Includes support for driver package extraction, driver installation, and custom paper form synchronization.
/// </summary>
public static class WindowsPrinterInstaller
{
    private static readonly Regex SafeNameRegex = new(@"[^a-zA-Z0-9_\-\. ]", RegexOptions.Compiled);

    /// <summary>
    /// Checks if a printer queue with the given name is installed in Windows Spooler.
    /// </summary>
    public static bool IsPrinterInstalled(string printerName)
    {
        string escapedName = printerName.Replace("'", "''");
        var (code, output, _) = RunPowerShell(
            $"if (Get-Printer -Name '{escapedName}' -ErrorAction SilentlyContinue) {{ Write-Output 'EXISTS' }} else {{ Write-Output 'NO' }}");
        return code == 0 && output.Contains("EXISTS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a printer port with the given name is installed.
    /// </summary>
    public static bool IsPortInstalled(string portName)
    {
        string escapedName = portName.Replace("'", "''");
        var (code, output, _) = RunPowerShell(
            $"if (Get-PrinterPort -Name '{escapedName}' -ErrorAction SilentlyContinue) {{ Write-Output 'EXISTS' }} else {{ Write-Output 'NO' }}");
        return code == 0 && output.Contains("EXISTS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the list of all printer drivers installed in Windows.
    /// </summary>
    public static List<string> GetInstalledDrivers()
    {
        var list = new List<string>();
        var (code, output, _) = RunPowerShell("Get-PrinterDriver | Select-Object -ExpandProperty Name");
        if (code == 0 && !string.IsNullOrWhiteSpace(output))
        {
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(trimmed);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Locates the INF driver repository path for a locally installed driver.
    /// </summary>
    public static string? FindDriverInfPath(string driverName)
    {
        try
        {
            string escapedDriver = driverName.Replace("'", "''");
            var (code, output, _) = RunPowerShell(
                $"Get-PrinterDriver -Name '{escapedDriver}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty InfPath");
            if (code == 0 && !string.IsNullOrWhiteSpace(output) && File.Exists(output.Trim()))
            {
                return output.Trim();
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// Compresses the driver package folder containing the INF and binaries into a ZIP byte array for network sharing.
    /// </summary>
    public static byte[]? CreateDriverPackageZip(string driverName)
    {
        try
        {
            string? infPath = FindDriverInfPath(driverName);
            if (string.IsNullOrEmpty(infPath)) return null;

            string? dir = Path.GetDirectoryName(infPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(dir, file);
                    archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
                }
            }
            return memoryStream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Installs a driver package ZIP onto the client machine using pnputil and Add-PrinterDriver.
    /// </summary>
    public static bool InstallDriverPackage(byte[] zipBytes, string preferredDriverName)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PrintLink", "Drivers", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            using (var ms = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(tempDir, true);
            }

            var infFiles = Directory.GetFiles(tempDir, "*.inf", SearchOption.AllDirectories);
            if (infFiles.Length > 0)
            {
                foreach (var inf in infFiles)
                {
                    RunProcess("pnputil.exe", $"/add-driver \"{inf}\" /install");
                }
            }

            string escapedDriver = preferredDriverName.Replace("'", "''");
            RunPowerShell($"Add-PrinterDriver -Name '{escapedDriver}' -ErrorAction SilentlyContinue");

            try { Directory.Delete(tempDir, true); } catch { }

            return GetInstalledDrivers().Any(d => d.Equals(preferredDriverName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers a custom page form in the Windows Print Spooler Forms database.
    /// </summary>
    public static bool RegisterCustomForm(CustomPageForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Name) || form.WidthMm <= 0 || form.HeightMm <= 0) return false;

        try
        {
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = NativeMethods.SERVER_ACCESS_ADMINISTER };
            bool opened = NativeMethods.OpenPrinterServer(null, out var hServer, ref defaults);
            if (!opened || hServer.IsInvalid)
            {
                defaults = new PRINTER_DEFAULTS { DesiredAccess = 0 };
                opened = NativeMethods.OpenPrinterServer(null, out hServer, ref defaults);
                if (!opened || hServer.IsInvalid) return false;
            }

            using (hServer)
            {
                int cxMicrons = (int)Math.Round(form.WidthMm * 1000.0);
                int cyMicrons = (int)Math.Round(form.HeightMm * 1000.0);

                var fi = new FORM_INFO_1
                {
                    Flags = NativeMethods.FORM_USER,
                    pName = form.Name,
                    Size = new SIZEL { cx = cxMicrons, cy = cyMicrons },
                    ImageableArea = new RECTL { left = 0, top = 0, right = cxMicrons, bottom = cyMicrons }
                };

                bool added = NativeMethods.AddForm(hServer, 1, ref fi);
                if (!added)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 1061 || err == 0) return true; // ERROR_FORM_ALREADY_EXISTS
                }
                return added;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Selects the best available driver on the current Windows machine.
    /// </summary>
    public static string ResolveBestDriver(string? preferredDriver)
    {
        var installedDrivers = GetInstalledDrivers();

        // 1. Exact match with preferred driver (e.g. "Kyocera ECOSYS M3860idn KX")
        if (!string.IsNullOrWhiteSpace(preferredDriver))
        {
            var exactMatch = installedDrivers.FirstOrDefault(d => d.Equals(preferredDriver, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null) return exactMatch;

            // Partial match
            var partialMatch = installedDrivers.FirstOrDefault(d => d.Contains(preferredDriver, StringComparison.OrdinalIgnoreCase));
            if (partialMatch != null) return partialMatch;
        }

        // 2. Generic / Text Only (Pre-staged on all Windows machines)
        var genericMatch = installedDrivers.FirstOrDefault(d => d.Equals("Generic / Text Only", StringComparison.OrdinalIgnoreCase));
        if (genericMatch != null) return genericMatch;

        // Try installing Generic / Text Only from driver store
        var (code, _, _) = RunPowerShell("Add-PrinterDriver -Name 'Generic / Text Only' -ErrorAction SilentlyContinue");
        if (code == 0) return "Generic / Text Only";

        // 3. Fallback to any installed generic driver
        var fallback = installedDrivers.FirstOrDefault(d =>
            d.Contains("Generic", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("Class Driver", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("PDF", StringComparison.OrdinalIgnoreCase));

        return fallback ?? "Generic / Text Only";
    }

    /// <summary>
    /// Installs a local TCP/IP printer port and creates a Windows Printer Queue with automatic server driver fetch and custom form syncing.
    /// </summary>
    public static async Task<(bool Success, string QueueName, string PortName, string DriverUsed, int FormsRegisteredCount, string? ErrorMessage)> InstallRemotePrinterAsync(
        string remotePrinterName,
        string? preferredDriver,
        int portNumber,
        IReadOnlyList<CustomPageForm>? customForms,
        Func<Task<byte[]?>>? driverPackageProvider)
    {
        // 1. Attempt driver package install if needed and provider supplied
        if (!string.IsNullOrWhiteSpace(preferredDriver) && driverPackageProvider != null)
        {
            var installedDrivers = GetInstalledDrivers();
            if (!installedDrivers.Any(d => d.Equals(preferredDriver, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var pkgBytes = await driverPackageProvider();
                    if (pkgBytes != null && pkgBytes.Length > 0)
                    {
                        InstallDriverPackage(pkgBytes, preferredDriver);
                    }
                }
                catch
                {
                }
            }
        }

        // 2. Register custom page forms into client's Windows Spooler Forms database
        int formsCount = 0;
        if (customForms != null)
        {
            foreach (var form in customForms)
            {
                if (RegisterCustomForm(form))
                {
                    formsCount++;
                }
            }
        }

        // 3. Complete Windows printer port and queue installation
        var (baseSuccess, qName, pName, drvUsed, err) = await InstallRemotePrinterAsync(remotePrinterName, preferredDriver, portNumber);
        return (baseSuccess, qName, pName, drvUsed, formsCount, err);
    }

    /// <summary>
    /// Legacy overload for backward compatibility with existing tests and callers.
    /// </summary>
    public static async Task<(bool Success, string QueueName, string PortName, string DriverUsed, string? ErrorMessage)> InstallRemotePrinterAsync(
        string remotePrinterName,
        string? preferredDriver,
        int portNumber)
    {
        return await Task.Run(() =>
        {
            try
            {
                string cleanName = SafeNameRegex.Replace(remotePrinterName, "").Trim();
                if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "RemotePrinter";

                string queueName = $"{cleanName} (PrintLink)";
                string portName = $"PL_Port_{portNumber}";

                // 1. Resolve Driver
                string driverToUse = ResolveBestDriver(preferredDriver);

                // 2. Add or verify Printer Port (Standard TCP/IP loopback port)
                if (!IsPortInstalled(portName))
                {
                    string addPortScript = $"Add-PrinterPort -Name '{portName}' -PrinterHostAddress '127.0.0.1' -PortNumber {portNumber} -ErrorAction Stop";
                    var (portCode, _, portErr) = RunPowerShell(addPortScript);
                    if (portCode != 0 && !IsPortInstalled(portName))
                    {
                        return (false, queueName, portName, driverToUse, $"Failed to create printer port '{portName}': {portErr}");
                    }
                }

                // 3. Add or update Printer Queue
                if (!IsPrinterInstalled(queueName))
                {
                    string addPrinterScript = $"Add-Printer -Name '{queueName}' -DriverName '{driverToUse}' -PortName '{portName}' -ErrorAction Stop";
                    var (printCode, _, printErr) = RunPowerShell(addPrinterScript);
                    if (printCode != 0)
                    {
                        return (false, queueName, portName, driverToUse, $"Failed to create printer queue '{queueName}': {printErr}");
                    }
                }

                return (true, queueName, portName, driverToUse, (string?)null);
            }
            catch (Exception ex)
            {
                return (false, "", "", "", (string?)ex.Message);
            }
        });
    }

    /// <summary>
    /// Uninstalls a Windows Printer Queue and removes its associated port.
    /// </summary>
    public static async Task<(bool Success, string? ErrorMessage)> UninstallRemotePrinterAsync(string queueName, string? portName)
    {
        return await Task.Run(() =>
        {
            try
            {
                string escapedQueue = queueName.Replace("'", "''");
                RunPowerShell($"Remove-Printer -Name '{escapedQueue}' -ErrorAction SilentlyContinue");

                if (!string.IsNullOrWhiteSpace(portName))
                {
                    string escapedPort = portName.Replace("'", "''");
                    RunPowerShell($"Remove-PrinterPort -Name '{escapedPort}' -ErrorAction SilentlyContinue");
                }

                return (true, (string?)null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    private static (int ExitCode, string Output, string Error) RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (-1, string.Empty, "Could not start process");

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);

            return (proc.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static (int ExitCode, string Output, string Error) RunPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return (-1, string.Empty, "Could not start PowerShell process");
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);

            return (proc.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
