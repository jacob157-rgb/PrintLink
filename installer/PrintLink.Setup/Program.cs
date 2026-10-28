using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace PrintLink.Setup;

internal static class Program
{
    private const string AppTitle = "L-Net PrintLink Setup v1.0.0";
    private const string ServiceName = "PrintLinkService";
    private const int DefaultPort = 8270;

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool isSilent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase));
        bool isElevated = IsAdministrator();

        if (!isSilent)
        {
            string message =
                "Welcome to L-Net PrintLink Enterprise Printer Sharing Setup!\n\n" +
                "This installer will configure:\n" +
                "  • L-Net PrintLink Desktop Dashboard & System Tray\n" +
                "  • Windows Background Print Spooler Service\n" +
                "  • Automatic Server Driver & Custom Page Size Sync\n" +
                "  • SQLite Reliability Database & Windows Firewall Rules\n\n" +
                $"Privilege Mode: {(isElevated ? "Administrator (Full System & Service Setup)" : "Standard User (Local Application)")}\n\n" +
                "Ready to proceed with automatic setup?";

            bool proceed = ModernSetupDialog.Show(
                title: AppTitle,
                message: message,
                confirmText: "Install Now",
                cancelText: "Cancel",
                isConfirmation: true,
                badgeIcon: "🖨️");

            if (!proceed)
            {
                return 1;
            }
        }

        try
        {
            // 1. Determine Target Directory
            string targetDir = isElevated
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LNet", "PrintLink")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LNet", "PrintLink");

            Directory.CreateDirectory(targetDir);

            // 2. Stop existing processes
            StopProcess("PrintLink.Desktop");
            StopProcess("PrintLink.Service");
            if (isElevated)
            {
                RunCommand("sc.exe", $"stop {ServiceName}");
            }
            Thread.Sleep(500);

            // 3. Locate and deploy payload files (Embedded Resource first, fallback to disk)
            bool deployedFromResource = false;
            bool payloadIsSelfContained = false;
            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            continue;
                        }

                        if (entry.Name.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals("hostfxr.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            payloadIsSelfContained = true;
                        }

                        string destPath = Path.Combine(targetDir, entry.FullName);
                        string? destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                    deployedFromResource = true;
                }
            }

            if (!deployedFromResource)
            {
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string payloadDir = Path.Combine(currentDir, "payload");

                if (Directory.Exists(payloadDir))
                {
                    CopyDirectory(payloadDir, targetDir);
                }
                else
                {
                    foreach (var file in Directory.GetFiles(currentDir, "*.*"))
                    {
                        string filename = Path.GetFileName(file);
                        if (!filename.Equals("PrintLink.Setup.exe", StringComparison.OrdinalIgnoreCase) &&
                            !filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(file, Path.Combine(targetDir, filename), overwrite: true);
                        }
                    }
                }
            }

            // 4. Ensure .NET Desktop Runtime is installed (if payload is framework-dependent)
            if (!payloadIsSelfContained && !IsDotNetDesktopRuntimeInstalled())
            {
                AutoDownloadAndInstallDotNet();
            }

            // 5. Configure Windows Service (if running elevated)
            string serviceExe = Path.Combine(targetDir, "PrintLink.Service.exe");
            if (isElevated && File.Exists(serviceExe))
            {
                RunCommand("sc.exe", $"delete {ServiceName}");
                Thread.Sleep(500);

                RunCommand("sc.exe", $"create {ServiceName} binPath= \"{serviceExe}\" start= auto DisplayName= \"L-Net PrintLink Service\"");
                RunCommand("sc.exe", $"description {ServiceName} \"Enterprise Windows Printer Sharing & Connection Reliability Engine\"");
                RunCommand("sc.exe", $"start {ServiceName}");

                RunCommand("netsh.exe", $"advfirewall firewall add rule name=\"L-Net PrintLink Service\" dir=in action=allow protocol=TCP localport={DefaultPort} profile=private,domain");
                RunCommand("netsh.exe", $"advfirewall firewall add rule name=\"L-Net PrintLink Discovery\" dir=in action=allow protocol=UDP localport=8271 profile=private,domain");
            }

            // 6. Create Desktop and Start Menu Shortcuts
            string desktopExe = Path.Combine(targetDir, "PrintLink.Desktop.exe");
            if (File.Exists(desktopExe))
            {
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "L-Net PrintLink.lnk"),
                    desktopExe,
                    "L-Net PrintLink Printer Sharing");

                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "L-Net PrintLink");
                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(
                    Path.Combine(startMenuDir, "L-Net PrintLink.lnk"),
                    desktopExe,
                    "L-Net PrintLink Printer Sharing");
            }

            // 7. Offer Launch at Login after installation
            bool enableAutoStart = false;
            if (!isSilent && File.Exists(desktopExe))
            {
                enableAutoStart = ModernSetupDialog.Show(
                    title: "Pengaturan Startup (Launch at Login)",
                    message: "Instalasi file L-Net PrintLink berhasil disiapkan!\n\n" +
                             "Apakah Anda ingin L-Net PrintLink otomatis berjalan setiap kali komputer dinyalakan (Launch at Login)?\n\n" +
                             "• Sangat direkomendasikan untuk Server maupun Client agar printer jaringan selalu siap sedia setiap saat.",
                    confirmText: "Ya, Aktifkan Auto-Start",
                    cancelText: "Tidak Sekarang",
                    isConfirmation: true,
                    badgeIcon: "🚀");

                if (enableAutoStart)
                {
                    SetStartupRegistry(desktopExe, true);
                }
            }

            // 8. Launch Desktop Application
            if (File.Exists(desktopExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = desktopExe,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }

            if (!isSilent)
            {
                ModernSetupDialog.Show(
                    title: AppTitle,
                    message: $"L-Net PrintLink telah berhasil diinstal!\n\n" +
                             $"Folder Instalasi:\n{targetDir}\n\n" +
                             $"Launch at Login: {(enableAutoStart ? "Aktif (Otomatis jalan saat login Windows)" : "Non-aktif (Dapat diubah di menu Pengaturan)")}\n\n" +
                             $"Aplikasi dashboard telah diluncurkan dan siap digunakan.",
                    confirmText: "Selesai",
                    cancelText: null,
                    isConfirmation: false,
                    badgeIcon: "✅");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (!isSilent)
            {
                ModernSetupDialog.Show(
                    title: AppTitle,
                    message: $"Installation failed with error:\n\n{ex.Message}",
                    confirmText: "Close",
                    cancelText: null,
                    isConfirmation: false,
                    badgeIcon: "❌");
            }
            return 2;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void SetStartupRegistry(string exePath, bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                {
                    key.SetValue("PrintLink.Desktop", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("PrintLink.Desktop", false);
                }
            }
        }
        catch
        {
        }
    }

    private static bool IsDotNetDesktopRuntimeInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet.exe",
                Arguments = "--list-runtimes",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                if (output.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static void AutoDownloadAndInstallDotNet()
    {
        try
        {
            string tempInstaller = Path.Combine(Path.GetTempPath(), "dotnet-runtime-installer.exe");
            string downloadUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(3);
                var data = client.GetByteArrayAsync(downloadUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(tempInstaller, data);
            }

            if (File.Exists(tempInstaller))
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = tempInstaller,
                    Arguments = "/install /quiet /norestart",
                    UseShellExecute = true
                });
                p?.WaitForExit(180000);
                try { File.Delete(tempInstaller); } catch { }
            }
        }
        catch
        {
        }
    }

    private static void StopProcess(string processName)
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                proc.Kill();
                proc.WaitForExit(2000);
            }
        }
        catch
        {
        }
    }

    private static void RunCommand(string fileName, string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            proc?.WaitForExit(5000);
        }
        catch
        {
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSub = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSub);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string description)
    {
        try
        {
            string psScript = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{targetPath}'; $s.Description = '{description}'; $s.WorkingDirectory = '{Path.GetDirectoryName(targetPath)}'; $s.Save()";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(3000);
        }
        catch
        {
        }
    }
}

/// <summary>
/// Modern Windows 11 style rounded dark dialog for installer setup and alerts.
/// </summary>
internal sealed class ModernSetupDialog : Form
{
    private readonly bool _isConfirmation;

    public static bool Show(string title, string message, string confirmText, string? cancelText = null, bool isConfirmation = true, string badgeIcon = "🖨️")
    {
        using var dialog = new ModernSetupDialog(title, message, confirmText, cancelText, isConfirmation, badgeIcon);
        return dialog.ShowDialog() == DialogResult.OK;
    }

    private ModernSetupDialog(string title, string message, string confirmText, string? cancelText, bool isConfirmation, string badgeIcon)
    {
        _isConfirmation = isConfirmation;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42); // #0F172A
        ForeColor = Color.FromArgb(241, 245, 249);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        Size = new Size(540, 360);
        ShowInTaskbar = true;

        // Custom Paint for border
        Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(51, 65, 85), 1.5f); // #334155
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        // Header Panel with Drag Move
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 54,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(18, 12, 18, 0)
        };
        headerPanel.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        };

        var iconLabel = new Label
        {
            Text = badgeIcon,
            Font = new Font("Segoe UI Emoji", 14f),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(18, 14)
        };

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(54, 15)
        };

        var closeBtn = new Button
        {
            Text = "✕",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(32, 32),
            Location = new Point(Width - 44, 12),
            Cursor = Cursors.Hand
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59);
        closeBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        headerPanel.Controls.Add(iconLabel);
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(closeBtn);
        Controls.Add(headerPanel);

        // Body message
        var messageBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular),
            Location = new Point(24, 68),
            Size = new Size(Width - 48, Height - 145),
            Text = message,
            ScrollBars = ScrollBars.Vertical
        };
        Controls.Add(messageBox);

        // Bottom Action Bar
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Color.FromArgb(22, 32, 54),
            Padding = new Padding(18, 12, 20, 12)
        };

        var confirmBtn = new Button
        {
            Text = confirmText,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(37, 99, 235), // Primary blue
            FlatStyle = FlatStyle.Flat,
            Size = new Size(130, 36),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        confirmBtn.FlatAppearance.BorderSize = 0;
        confirmBtn.Location = new Point(Width - 150, 12);
        bottomPanel.Controls.Add(confirmBtn);

        if (!string.IsNullOrEmpty(cancelText))
        {
            var cancelBtn = new Button
            {
                Text = cancelText,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(203, 213, 225),
                BackColor = Color.FromArgb(51, 65, 85),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(95, 36),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Location = new Point(Width - 255, 12);
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottomPanel.Controls.Add(cancelBtn);
        }

        Controls.Add(bottomPanel);
        AcceptButton = confirmBtn;
    }

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
