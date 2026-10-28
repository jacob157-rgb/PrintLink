using System.Windows.Media;
using PrintLink.Core.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace PrintLink.Desktop.ViewModels;

public sealed class PrinterItemViewModel : ViewModelBase
{
    private bool _isShared;
    private string? _shareName;

    public string Name { get; }
    public string? DriverName { get; }
    public string? PortName { get; }
    public bool IsDefault { get; }
    public string Status { get; }

    public bool IsShared
    {
        get => _isShared;
        set
        {
            if (SetProperty(ref _isShared, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ShareBadgeText));
            }
        }
    }

    public string? ShareName
    {
        get => _shareName;
        set
        {
            if (SetProperty(ref _shareName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => IsShared && !string.IsNullOrWhiteSpace(ShareName) ? ShareName : Name;

    public string ShareBadgeText => IsShared ? "Shared" : "Local Only";

    public MediaBrush ShareBadgeBrush => IsShared
        ? new SolidColorBrush(MediaColor.FromRgb(16, 185, 129))   // Green
        : new SolidColorBrush(MediaColor.FromRgb(107, 114, 128)); // Gray

    public MediaBrush StatusBadgeBrush => Status == "Ready"
        ? new SolidColorBrush(MediaColor.FromRgb(16, 185, 129))   // Green
        : new SolidColorBrush(MediaColor.FromRgb(245, 158, 11));  // Amber

    public bool SupportsColor { get; }
    public bool SupportsDuplex { get; }
    public IReadOnlyList<string> SupportedPaperSizes { get; }
    public IReadOnlyList<CustomPageForm> CustomForms { get; } = [];
    public bool HasDriverPackage { get; }
    public string CustomFormsSummary => CustomForms.Count > 0 
        ? $"{CustomForms.Count} Custom Forms ({string.Join(", ", CustomForms.Take(2).Select(f => f.Name))}{(CustomForms.Count > 2 ? "..." : "")})" 
        : "";

    public string CapabilitiesSummary => $"{(SupportsColor ? "Color" : "Monochrome")} • Duplex: {(SupportsDuplex ? "Supported" : "No")} • {(SupportedPaperSizes.Count > 0 ? $"{SupportedPaperSizes.Count} Formats" : "Standard")}{(CustomForms.Count > 0 ? $" • {CustomForms.Count} Custom" : "")}";

    private bool _isInstalledInWindows;
    private string? _windowsQueueName;
    private int? _bridgePort;
    private string _bridgeStatus = "Inactive";
    private bool _isInstalling;

    public bool IsInstalledInWindows
    {
        get => _isInstalledInWindows;
        set
        {
            if (SetProperty(ref _isInstalledInWindows, value))
            {
                OnPropertyChanged(nameof(InstalledBadgeBrush));
                OnPropertyChanged(nameof(InstalledBadgeText));
                OnPropertyChanged(nameof(CanInstallInWindows));
                OnPropertyChanged(nameof(CanUninstallFromWindows));
            }
        }
    }

    public string? WindowsQueueName
    {
        get => _windowsQueueName;
        set => SetProperty(ref _windowsQueueName, value);
    }

    public int? BridgePort
    {
        get => _bridgePort;
        set
        {
            if (SetProperty(ref _bridgePort, value))
            {
                OnPropertyChanged(nameof(BridgePortDisplay));
            }
        }
    }

    public string BridgePortDisplay => BridgePort.HasValue ? $"127.0.0.1:{BridgePort}" : "N/A";

    public string BridgeStatus
    {
        get => _bridgeStatus;
        set => SetProperty(ref _bridgeStatus, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                OnPropertyChanged(nameof(CanInstallInWindows));
                OnPropertyChanged(nameof(CanUninstallFromWindows));
            }
        }
    }

    public bool CanInstallInWindows => !IsInstalledInWindows && !IsInstalling;
    public bool CanUninstallFromWindows => IsInstalledInWindows && !IsInstalling;

    public MediaBrush InstalledBadgeBrush => IsInstalledInWindows
        ? new SolidColorBrush(MediaColor.FromRgb(16, 185, 129))   // Green
        : new SolidColorBrush(MediaColor.FromRgb(107, 114, 128)); // Gray

    public string InstalledBadgeText => IsInstalledInWindows ? "✓ Added to Windows" : "Not in Windows";

    public string? ServerHost { get; set; }
    public int? ServerPort { get; set; }
    public string? ServerName { get; set; }

    public bool IsRemote => !string.IsNullOrWhiteSpace(ServerHost);
    public string OriginBadgeText => IsRemote ? $"{ServerName ?? ServerHost}:{ServerPort ?? 8270}" : "Local Spooler";

    public PrinterItemViewModel(PrinterInfo info)
    {
        Name = info.Name;
        DriverName = info.DriverName ?? "Unknown Driver";
        PortName = info.PortName ?? "N/A";
        IsDefault = info.IsDefault;
        Status = info.StatusDescription;
        SupportsColor = info.SupportsColor;
        SupportsDuplex = info.SupportsDuplex;
        SupportedPaperSizes = info.SupportedPaperSizes;
        CustomForms = info.CustomForms;
        HasDriverPackage = info.HasDriverPackage;
        _isShared = info.IsShared;
        _shareName = info.ShareName;
    }

    public PrinterItemViewModel(
        string name,
        string displayName,
        string? driverName,
        string status,
        string? serverHost = null,
        int? serverPort = null,
        string? serverName = null,
        IReadOnlyList<CustomPageForm>? customForms = null)
    {
        Name = name;
        _shareName = displayName;
        DriverName = driverName ?? "Generic / Text Only";
        PortName = serverHost != null ? $"Remote:{serverHost}" : "N/A";
        Status = status;
        SupportsColor = false;
        SupportsDuplex = false;
        SupportedPaperSizes = [];
        CustomForms = customForms ?? [];
        _isShared = true;
        ServerHost = serverHost;
        ServerPort = serverPort;
        ServerName = serverName;
    }
}
