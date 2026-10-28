using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace PrintLink.Desktop.SystemTray;

public sealed class SystemTrayController : IDisposable
{
    private readonly Window _mainWindow;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isExplicitExit;

    public SystemTrayController(Window mainWindow)
    {
        _mainWindow = mainWindow;

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "L-Net PrintLink",
            Visible = true,
            Icon = CreateTrayIcon()
        };

        var contextMenu = new Forms.ContextMenuStrip();
        var openItem = new Forms.ToolStripMenuItem("Open Dashboard", null, (_, _) => RestoreWindow());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);

        var statusItem = new Forms.ToolStripMenuItem("L-Net PrintLink v1.0.0") { Enabled = false };
        var separator = new Forms.ToolStripSeparator();
        var exitItem = new Forms.ToolStripMenuItem("Exit GUI (Service stays running)", null, (_, _) =>
        {
            _isExplicitExit = true;
            _mainWindow.Close();
        });

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(separator);
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => RestoreWindow();

        // Hook Window events
        _mainWindow.StateChanged += MainWindow_StateChanged;
        _mainWindow.Closing += MainWindow_Closing;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.Hide();
            _notifyIcon.ShowBalloonTip(
                2000,
                "L-Net PrintLink",
                "Application minimized to system tray. Background service remains active.",
                Forms.ToolTipIcon.Info);
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            e.Cancel = true;
            _mainWindow.Hide();
            _notifyIcon.ShowBalloonTip(
                2000,
                "L-Net PrintLink",
                "Minimized to system tray. Closing the window does not stop printing services.",
                Forms.ToolTipIcon.Info);
        }
    }

    public void RestoreWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private static Icon CreateTrayIcon()
    {
        // Programmatically draw a clean 32x32 printer icon
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Background circle
            using var brushBg = new SolidBrush(System.Drawing.Color.FromArgb(37, 99, 235)); // Modern Blue #2563EB
            g.FillEllipse(brushBg, 1, 1, 30, 30);

            // Printer paper top
            using var brushPaper = new SolidBrush(System.Drawing.Color.White);
            g.FillRectangle(brushPaper, 9, 5, 14, 8);

            // Printer body
            using var brushBody = new SolidBrush(System.Drawing.Color.FromArgb(243, 244, 246));
            g.FillRectangle(brushBody, 6, 12, 20, 10);

            // Output paper bottom
            using var brushPaperOut = new SolidBrush(System.Drawing.Color.FromArgb(209, 213, 219));
            g.FillRectangle(brushPaperOut, 9, 17, 14, 9);

            // Status indicator dot
            using var brushDot = new SolidBrush(System.Drawing.Color.FromArgb(16, 185, 129)); // Green
            g.FillEllipse(brushDot, 21, 21, 8, 8);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
