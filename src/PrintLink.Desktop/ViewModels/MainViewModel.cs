using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PrintLink.Core.Enums;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Ipc;
using PrintLink.Core.Models;
using PrintLink.Core.Utilities;
using PrintLink.Diagnostics;
using System.Text.RegularExpressions;
using PrintLink.Network.Auth;
using PrintLink.Network.Bridge;
using PrintLink.Network.Client;
using PrintLink.Network.Discovery;
using PrintLink.Network.Jobs;
using PrintLink.Network.Server;
using PrintLink.Spooler;
using PrintLink.Storage;
using PrintLink.Storage.Printers;
using PrintLink.Storage.Repositories;
using PrintLink.Desktop.Views;
using PrintLink.Core.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace PrintLink.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly NamedPipeIpcClient _ipcClient = new();
    private readonly IPrinterEnumerator _localEnumerator = new WindowsPrinterEnumerator();
    private readonly IPrinterEnumerator _configuredEnumerator;
    private readonly IPrinterSpooler _localSpooler = new WindowsPrinterSpooler();
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IConfigRepository _configRepo;
    private readonly IAuthorizedClientRepository _clientRepo;
    private readonly IPrintJobRepository _jobRepo;
    private readonly DispatcherTimer _refreshTimer;
    private readonly PrintBridgeManager _bridgeManager = new();
    private readonly GitHubUpdateChecker _updateChecker = new();

    private PrintLinkServerHost? _standaloneServerHost;
    private LanBeaconBroadcaster? _standaloneBeaconBroadcaster;

    private OperatingMode _currentMode = OperatingMode.Server;
    private bool _isServiceConnected;
    private string _serviceStatusText = "Connecting to background service...";
    private string _serverPortText = "8270";
    private string _statusMessage = "Ready";
    private int _selectedTabIndex;
    private PrinterItemViewModel? _selectedPrinter;
    private PrinterItemViewModel? _selectedRemotePrinter;
    private PrintJobItemViewModel? _selectedJob;

    // First-Run Wizard
    private bool _isFirstRun;
    private int _wizardStep = 1;

    // Remote Server inputs
    private string _serverAddressInput = "127.0.0.1";
    private string _serverPortInput = "8270";
    private string _serverTokenInput = "";
    private string _connectionTestLog = "Enter server IP/hostname and click 'Connect & Fetch Printers' to verify reachability.";
    private bool _isServerConnected;
    private string _serverConnectionStatus = "Disconnected";

    // Job Search & Filters
    private string _jobSearchQuery = "";
    private string _jobStatusFilter = "All";

    // Client Management inputs
    private string _newClientNameInput = "";
    private string _generatedTokenOutput = "";

    // Auto-start
    private bool _isAutoStartEnabled;

    public ObservableCollection<PrinterItemViewModel> Printers { get; } = [];
    public ObservableCollection<PrinterItemViewModel> RemotePrinters { get; } = [];
    public ObservableCollection<PrintJobItemViewModel> RecentJobs { get; } = [];
    public ObservableCollection<PrintJobItemViewModel> FilteredJobs { get; } = [];
    public ObservableCollection<ClientItemViewModel> Clients { get; } = [];
    public ObservableCollection<ServerConnectionItemViewModel> RemoteServers { get; } = [];
    public ObservableCollection<DiscoveredServer> DiscoveredServers { get; } = [];

    private bool _isDiscoveringServers;
    public bool IsDiscoveringServers
    {
        get => _isDiscoveringServers;
        set => SetProperty(ref _isDiscoveringServers, value);
    }

    public OperatingMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(IsServerMode));
                OnPropertyChanged(nameof(IsClientMode));
                OnPropertyChanged(nameof(IsCombinedMode));
                OnPropertyChanged(nameof(ModeBadgeText));
                NotifyNetworkPropertiesChanged();
            }
        }
    }

    public bool IsServerMode => CurrentMode == OperatingMode.Server;
    public bool IsClientMode => CurrentMode == OperatingMode.Client;
    public bool IsCombinedMode => CurrentMode == OperatingMode.Combined;

    public string ModeBadgeText => CurrentMode switch
    {
        OperatingMode.Server => "Server Mode (Sharing Local Printers)",
        OperatingMode.Client => "Client Mode (Using Remote Printers)",
        _ => "Combined Mode (Server + Client)"
    };

    public bool IsServiceConnected
    {
        get => _isServiceConnected;
        set
        {
            if (SetProperty(ref _isServiceConnected, value))
            {
                OnPropertyChanged(nameof(ServiceStatusBrush));
            }
        }
    }

    public MediaBrush ServiceStatusBrush => IsServiceConnected
        ? new MediaSolidColorBrush(MediaColor.FromRgb(34, 197, 94))   // Green #22C55E
        : new MediaSolidColorBrush(MediaColor.FromRgb(245, 158, 11)); // Amber #F59E0B

    public string ServiceStatusText
    {
        get => _serviceStatusText;
        set => SetProperty(ref _serviceStatusText, value);
    }

    public string ServerPortText
    {
        get => _serverPortText;
        set => SetProperty(ref _serverPortText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public PrinterItemViewModel? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (SetProperty(ref _selectedPrinter, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public PrinterItemViewModel? SelectedRemotePrinter
    {
        get => _selectedRemotePrinter;
        set
        {
            if (SetProperty(ref _selectedRemotePrinter, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public PrintJobItemViewModel? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetProperty(ref _selectedJob, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsFirstRun
    {
        get => _isFirstRun;
        set => SetProperty(ref _isFirstRun, value);
    }

    public int WizardStep
    {
        get => _wizardStep;
        set => SetProperty(ref _wizardStep, value);
    }

    public string JobSearchQuery
    {
        get => _jobSearchQuery;
        set
        {
            if (SetProperty(ref _jobSearchQuery, value))
            {
                ApplyJobFilters();
            }
        }
    }

    public string JobStatusFilter
    {
        get => _jobStatusFilter;
        set
        {
            if (SetProperty(ref _jobStatusFilter, value))
            {
                ApplyJobFilters();
            }
        }
    }

    public string ServerAddressInput
    {
        get => _serverAddressInput;
        set
        {
            if (SetProperty(ref _serverAddressInput, value))
            {
                NotifyNetworkPropertiesChanged();
            }
        }
    }

    public string ServerPortInput
    {
        get => _serverPortInput;
        set
        {
            if (SetProperty(ref _serverPortInput, value))
            {
                NotifyNetworkPropertiesChanged();
            }
        }
    }

    public string ServerTokenInput
    {
        get => _serverTokenInput;
        set => SetProperty(ref _serverTokenInput, value);
    }

    public string ConnectionTestLog
    {
        get => _connectionTestLog;
        set => SetProperty(ref _connectionTestLog, value);
    }

    public bool IsServerConnected
    {
        get => _isServerConnected;
        set
        {
            if (SetProperty(ref _isServerConnected, value))
            {
                OnPropertyChanged(nameof(ServerConnectionBadge));
                OnPropertyChanged(nameof(ServerConnectionBrush));
            }
        }
    }

    public string ServerConnectionStatus
    {
        get => _serverConnectionStatus;
        set
        {
            if (SetProperty(ref _serverConnectionStatus, value))
            {
                OnPropertyChanged(nameof(ServerConnectionBadge));
                OnPropertyChanged(nameof(ServerConnectionBrush));
            }
        }
    }

    public string ServerConnectionBadge => IsServerConnected
        ? $"● Connected ({ServerAddressInput}:{ServerPortInput})"
        : $"○ {ServerConnectionStatus}";

    public MediaBrush ServerConnectionBrush => IsServerConnected
        ? new MediaSolidColorBrush(MediaColor.FromRgb(16, 185, 129))
        : new MediaSolidColorBrush(MediaColor.FromRgb(100, 116, 139));

    public string NewClientNameInput
    {
        get => _newClientNameInput;
        set => SetProperty(ref _newClientNameInput, value);
    }

    public string GeneratedTokenOutput
    {
        get => _generatedTokenOutput;
        set => SetProperty(ref _generatedTokenOutput, value);
    }

    public bool IsAutoStartEnabled
    {
        get => _isAutoStartEnabled;
        set
        {
            if (SetProperty(ref _isAutoStartEnabled, value))
            {
                SetAutoStartRegistry(value);
            }
        }
    }

    public int TotalPrintersCount => Printers.Count;
    public int SharedPrintersCount => Printers.Count(p => p.IsShared);
    public int RecentJobsCount => RecentJobs.Count;
    public int AuthorizedClientsCount => Clients.Count(c => !c.IsRevoked);

    private string _localIpAddress = "127.0.0.1";
    public string LocalIpAddress
    {
        get => _localIpAddress;
        private set => SetProperty(ref _localIpAddress, value);
    }

    public string NetworkIdentityText
    {
        get
        {
            if (CurrentMode == OperatingMode.Server)
            {
                return $"🖥️ Server IP: {LocalIpAddress}:{ServerPortText}";
            }
            else if (CurrentMode == OperatingMode.Client)
            {
                return $"💻 Client IP: {LocalIpAddress}";
            }
            else
            {
                return $"🖥️ Host/Client IP: {LocalIpAddress}:{ServerPortText}";
            }
        }
    }

    public string NetworkTargetText
    {
        get
        {
            if (CurrentMode == OperatingMode.Client)
            {
                if (IsServerConnected && !string.IsNullOrWhiteSpace(ServerAddressInput))
                {
                    return $"🔗 Server: {ServerAddressInput}:{ServerPortInput} (Terhubung)";
                }
                else if (!string.IsNullOrWhiteSpace(ServerAddressInput))
                {
                    return $"🔗 Server Target: {ServerAddressInput}:{ServerPortInput}";
                }
                return "🔗 Server: Belum Terhubung";
            }
            else if (CurrentMode == OperatingMode.Server)
            {
                return $"📡 Port: {ServerPortText} | Klien: {AuthorizedClientsCount}";
            }
            return $"📡 Port: {ServerPortText}";
        }
    }

    private bool _isCheckingUpdate;
    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        set => SetProperty(ref _isCheckingUpdate, value);
    }

    private bool _isDownloadingUpdate;
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set => SetProperty(ref _isDownloadingUpdate, value);
    }

    private double _updateProgressPercent;
    public double UpdateProgressPercent
    {
        get => _updateProgressPercent;
        set => SetProperty(ref _updateProgressPercent, value);
    }

    public IReadOnlyList<string> StatusFilterOptions { get; } =
        ["All", "Completed", "Spooling", "Accepted", "Sending", "Queued", "Failed", "Cancelled", "Unknown"];

    public ICommand RefreshCommand { get; }
    public ICommand SwitchModeCommand { get; }
    public ICommand SendTestPrintCommand { get; }
    public ICommand SendRemoteTestPrintCommand { get; }
    public ICommand ToggleShareCommand { get; }
    public ICommand NavigateTabCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand AddRemoteServerCommand { get; }
    public ICommand FetchRemotePrintersCommand { get; }
    public ICommand InstallWindowsPrinterCommand { get; }
    public ICommand UninstallWindowsPrinterCommand { get; }
    public ICommand CreateClientTokenCommand { get; }
    public ICommand RevokeClientCommand { get; }
    public ICommand CopyTokenCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand RetryJobCommand { get; }
    public ICommand CancelJobCommand { get; }
    public ICommand RefreshJobsCommand { get; }
    public ICommand ClearCompletedJobsCommand { get; }
    public ICommand CopyJobIdCommand { get; }
    public ICommand DiscoverServersCommand { get; }
    public ICommand SelectDiscoveredServerCommand { get; }
    public ICommand CompleteWizardCommand { get; }
    public ICommand NextWizardStepCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }

    public MainViewModel()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string dbPath = Path.Combine(programData, "LNet", "PrintLink", "printlink.db");
        _connectionFactory = new SqliteConnectionFactory(dbPath);
        _configRepo = new SqliteConfigRepository(_connectionFactory);
        _clientRepo = new SqliteAuthorizedClientRepository(_connectionFactory);
        _jobRepo = new SqlitePrintJobRepository(_connectionFactory);
        _configuredEnumerator = new ConfiguredPrinterEnumerator(_localEnumerator, _configRepo);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SwitchModeCommand = new AsyncRelayCommand(param => SwitchModeAsync(param?.ToString()));
        SendTestPrintCommand = new AsyncRelayCommand(SendTestPrintAsync, () => SelectedPrinter != null);
        SendRemoteTestPrintCommand = new AsyncRelayCommand(SendRemoteTestPrintAsync, () => SelectedRemotePrinter != null);
        ToggleShareCommand = new AsyncRelayCommand(ToggleShareAsync);
        NavigateTabCommand = new RelayCommand(param =>
        {
            if (param != null && int.TryParse(param.ToString(), out int tabIndex))
            {
                SelectedTabIndex = tabIndex;
            }
        });

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        AddRemoteServerCommand = new RelayCommand(AddRemoteServer);
        FetchRemotePrintersCommand = new AsyncRelayCommand(FetchRemotePrintersAsync);
        InstallWindowsPrinterCommand = new AsyncRelayCommand(param => InstallWindowsPrinterAsync(param as PrinterItemViewModel));
        UninstallWindowsPrinterCommand = new AsyncRelayCommand(param => UninstallWindowsPrinterAsync(param as PrinterItemViewModel));
        CreateClientTokenCommand = new AsyncRelayCommand(CreateClientTokenAsync);
        RevokeClientCommand = new AsyncRelayCommand(param => RevokeClientAsync(param as ClientItemViewModel));
        CopyTokenCommand = new RelayCommand(CopyTokenToClipboard);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync);
        RetryJobCommand = new AsyncRelayCommand(RetryJobAsync, () => SelectedJob != null && SelectedJob.State != JobState.Completed);
        CancelJobCommand = new AsyncRelayCommand(CancelJobAsync, () => SelectedJob != null);
        RefreshJobsCommand = new AsyncRelayCommand(LoadHistoricalJobsAsync);
        ClearCompletedJobsCommand = new AsyncRelayCommand(ClearCompletedJobsAsync);
        CopyJobIdCommand = new RelayCommand(CopyJobIdToClipboard);
        DiscoverServersCommand = new AsyncRelayCommand(DiscoverServersAsync);
        SelectDiscoveredServerCommand = new RelayCommand(SelectDiscoveredServer);
        CheckForUpdatesCommand = new AsyncRelayCommand(_ => CheckForUpdatesAsync(false));

        CompleteWizardCommand = new AsyncRelayCommand(param => CompleteWizardAsync(param));
        NextWizardStepCommand = new RelayCommand(param =>
        {
            if (param != null && int.TryParse(param.ToString(), out int step))
            {
                WizardStep = step;
            }
            else
            {
                WizardStep++;
            }
        });

        _isAutoStartEnabled = CheckAutoStartRegistry();
        UpdateLocalIpAddress();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _refreshTimer.Tick += async (_, _) => await CheckServiceStatusAsync();
        _refreshTimer.Start();

        // Initial load & First Run check
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await CheckFirstRunAsync();
        await LoadClientServerSettingsAsync();
        await RefreshAsync();
        if (!IsServiceConnected && (CurrentMode is OperatingMode.Server or OperatingMode.Combined))
        {
            await ApplyStandaloneModeAsync(CurrentMode);
        }
        if (CurrentMode is OperatingMode.Client or OperatingMode.Combined)
        {
            if (!string.IsNullOrWhiteSpace(ServerAddressInput) && ServerAddressInput != "127.0.0.1")
            {
                _ = TestConnectionAsync();
            }
        }
        _ = DiscoverServersAsync();
    }

    private async Task LoadClientServerSettingsAsync()
    {
        try
        {
            string? savedHost = await _configRepo.GetValueAsync("Client:ServerAddress");
            string? savedPort = await _configRepo.GetValueAsync("Client:ServerPort");
            string? savedToken = await _configRepo.GetValueAsync("Client:ServerToken");

            if (!string.IsNullOrWhiteSpace(savedHost))
            {
                ServerAddressInput = savedHost;
            }
            if (!string.IsNullOrWhiteSpace(savedPort))
            {
                ServerPortInput = savedPort;
            }
            if (!string.IsNullOrWhiteSpace(savedToken))
            {
                ServerTokenInput = savedToken;
            }
        }
        catch
        {
        }
    }

    private async Task CheckFirstRunAsync()
    {
        try
        {
            await DatabaseMigrator.MigrateAsync(_connectionFactory);
            string? setupDone = await _configRepo.GetValueAsync("Setup:Completed");
            IsFirstRun = string.IsNullOrWhiteSpace(setupDone);
        }
        catch
        {
            IsFirstRun = false;
        }
    }

    public async Task RefreshAsync()
    {
        StatusMessage = "Refreshing printers, services, and scanning LAN...";
        UpdateLocalIpAddress();
        await CheckServiceStatusAsync();
        await LoadLocalPrintersAsync();
        await LoadClientsAsync();
        await LoadHistoricalJobsAsync();
        if (CurrentMode == OperatingMode.Client && !string.IsNullOrWhiteSpace(ServerAddressInput))
        {
            _ = FetchRemotePrintersAsync();
        }
        await DiscoverServersAsync();
        StatusMessage = $"Updated at {DateTime.Now:HH:mm:ss}";
    }

    private async Task CheckServiceStatusAsync()
    {
        try
        {
            var response = await _ipcClient.SendRequestAsync(new IpcRequest("GetStatus"), timeoutMs: 800);
            if (response.Success && response.DataJson != null)
            {
                var status = JsonSerializer.Deserialize<ServiceStatusData>(response.DataJson);
                if (status != null)
                {
                    IsServiceConnected = true;
                    ServiceStatusText = "Windows Service: Active (Connected)";
                    CurrentMode = status.Mode;
                    ServerPortText = status.ServerPort.ToString();
                    return;
                }
            }
        }
        catch
        {
            // Background service is offline
        }

        IsServiceConnected = false;
        ServiceStatusText = "Windows Service: Offline (Local Direct Mode)";
        try
        {
            CurrentMode = await _configRepo.GetOperatingModeAsync();
        }
        catch
        {
        }
    }

    private Task LoadLocalPrintersAsync()
    {
        var localPrinters = _configuredEnumerator.GetPrinters();

        Printers.Clear();
        foreach (var p in localPrinters)
        {
            Printers.Add(new PrinterItemViewModel(p));
        }

        if (SelectedPrinter == null && Printers.Count > 0)
        {
            SelectedPrinter = Printers[0];
        }

        OnPropertyChanged(nameof(TotalPrintersCount));
        OnPropertyChanged(nameof(SharedPrintersCount));
        return Task.CompletedTask;
    }

    private async Task LoadClientsAsync()
    {
        try
        {
            var clients = await _clientRepo.GetAllClientsAsync();

            Clients.Clear();
            foreach (var c in clients)
            {
                Clients.Add(new ClientItemViewModel(c));
            }
            OnPropertyChanged(nameof(AuthorizedClientsCount));
        }
        catch
        {
        }
    }

    private async Task LoadHistoricalJobsAsync()
    {
        try
        {
            var jobs = await _jobRepo.GetRecentJobsAsync(100);

            RecentJobs.Clear();
            foreach (var j in jobs)
            {
                RecentJobs.Add(new PrintJobItemViewModel(j));
            }
            ApplyJobFilters();
            OnPropertyChanged(nameof(RecentJobsCount));
        }
        catch
        {
        }
    }

    private void ApplyJobFilters()
    {
        FilteredJobs.Clear();

        var query = RecentJobs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(JobSearchQuery))
        {
            query = query.Where(j =>
                j.DocumentName.Contains(JobSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                j.PrinterName.Contains(JobSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                j.ClientId.Contains(JobSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                j.FullJobId.Contains(JobSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(JobStatusFilter) && !string.Equals(JobStatusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(j => j.StateName.Equals(JobStatusFilter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var job in query)
        {
            FilteredJobs.Add(job);
        }

        if (SelectedJob == null && FilteredJobs.Count > 0)
        {
            SelectedJob = FilteredJobs[0];
        }
        else if (SelectedJob != null && !FilteredJobs.Contains(SelectedJob))
        {
            SelectedJob = FilteredJobs.FirstOrDefault();
        }
    }

    private async Task SwitchModeAsync(string? modeString)
    {
        if (Enum.TryParse<OperatingMode>(modeString, out var newMode))
        {
            CurrentMode = newMode;
            StatusMessage = $"Switching mode to {newMode}...";

            try
            {
                await _configRepo.SetOperatingModeAsync(newMode);

                if (IsServiceConnected)
                {
                    await _ipcClient.SendRequestAsync(new IpcRequest("SetMode", $"\"{newMode}\""));
                }
                else
                {
                    await ApplyStandaloneModeAsync(newMode);
                }

                StatusMessage = $"Operating mode is active: {newMode}.";

                string modeInfo = newMode switch
                {
                    OperatingMode.Server => "SERVER (HOST MODE)\n\nThis computer will host local printers and broadcast availability to client PCs across the network.",
                    OperatingMode.Client => "CLIENT (WORKSTATION MODE)\n\nThis computer connects to remote PrintLink servers. Opening Remote Servers view.",
                    _ => "COMBINED MODE (SERVER + CLIENT)\n\nThis computer can both share local printers and connect to remote branch servers."
                };

                ModernDialog.ShowInformation(
                    $"Operating mode successfully set to:\n\n{modeInfo}",
                    "PrintLink Mode Switch");

                SelectedTabIndex = 0; // Printers Hub
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error setting mode {newMode}: {ex.Message}";
                ModernDialog.ShowError($"Failed to configure mode {newMode}:\n{ex.Message}", "Mode Error");
            }
        }
    }

    private async Task ApplyStandaloneModeAsync(OperatingMode mode)
    {
        try
        {
            if (mode is OperatingMode.Server or OperatingMode.Combined)
            {
                if (_standaloneServerHost == null)
                {
                    int port = int.TryParse(ServerPortText, out int p) ? p : 8270;
                    var authenticator = new TokenAuthenticator(allowAnonymousLanClients: true);
                    _standaloneServerHost = new PrintLinkServerHost(
                        port,
                        _configuredEnumerator,
                        _localSpooler,
                        authenticator,
                        new InMemoryJobTracker());

                    await _standaloneServerHost.StartAsync();
                }

                if (_standaloneBeaconBroadcaster == null)
                {
                    int port = int.TryParse(ServerPortText, out int p) ? p : 8270;
                    _standaloneBeaconBroadcaster = new LanBeaconBroadcaster(port, mode.ToString(), _configuredEnumerator);
                    _standaloneBeaconBroadcaster.Start();
                }
            }
            else // Client mode
            {
                if (_standaloneBeaconBroadcaster != null)
                {
                    await _standaloneBeaconBroadcaster.DisposeAsync();
                    _standaloneBeaconBroadcaster = null;
                }

                if (_standaloneServerHost != null)
                {
                    await _standaloneServerHost.StopAsync();
                    await _standaloneServerHost.DisposeAsync();
                    _standaloneServerHost = null;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Standalone server notice: {ex.Message}";
        }
    }

    public async Task DisposeStandaloneServicesAsync()
    {
        try
        {
            if (_standaloneBeaconBroadcaster != null)
            {
                await _standaloneBeaconBroadcaster.DisposeAsync();
                _standaloneBeaconBroadcaster = null;
            }

            if (_standaloneServerHost != null)
            {
                await _standaloneServerHost.StopAsync();
                await _standaloneServerHost.DisposeAsync();
                _standaloneServerHost = null;
            }

            await _bridgeManager.StopAllAsync();
        }
        catch
        {
        }
    }

    private async Task ToggleShareAsync(object? parameter = null)
    {
        if (SelectedPrinter != null)
        {
            SelectedPrinter.IsShared = !SelectedPrinter.IsShared;
            if (SelectedPrinter.IsShared && string.IsNullOrWhiteSpace(SelectedPrinter.ShareName))
            {
                SelectedPrinter.ShareName = SelectedPrinter.Name.Replace(" ", "_");
            }
            await _configRepo.SetValueAsync($"Printer:Shared:{SelectedPrinter.Name}", SelectedPrinter.IsShared ? "true" : "false");
            if (!string.IsNullOrWhiteSpace(SelectedPrinter.ShareName))
            {
                await _configRepo.SetValueAsync($"Printer:ShareName:{SelectedPrinter.Name}", SelectedPrinter.ShareName);
            }
            OnPropertyChanged(nameof(SharedPrintersCount));
            StatusMessage = $"Toggled sharing for {SelectedPrinter.Name}: {(SelectedPrinter.IsShared ? "Shared over PrintLink" : "Local Only")}";
        }
    }

    private async Task SendTestPrintAsync()
    {
        if (SelectedPrinter == null) return;

        StatusMessage = $"Sending local test print to '{SelectedPrinter.Name}'...";

        byte[] testBytes = TestPageGenerator.GenerateRawTestPage(
            printerName: SelectedPrinter.Name,
            serverName: Environment.MachineName,
            clientName: "PrintLink_Desktop_UI",
            driverName: SelectedPrinter.DriverName,
            portName: SelectedPrinter.PortName);

        var result = _localSpooler.PrintBytes(SelectedPrinter.Name, "PrintLink_Test_Page", testBytes, "RAW");

        var jobId = Guid.NewGuid();
        var (isNew, job) = await _jobRepo.GetOrRegisterJobAsync(jobId, SelectedPrinter.Name, "Local_Desktop_UI", "PrintLink_Test_Page", testBytes.Length);

        if (result.Success)
        {
            await _jobRepo.UpdateJobStateAsync(jobId, JobState.Completed, result.SpoolerJobId);
            StatusMessage = $"Test print spooled successfully! Spooler Job ID: {result.SpoolerJobId}";
        }
        else
        {
            string errCode = $"PL-SPOOL-{result.Win32ErrorCode:D4}";
            await _jobRepo.UpdateJobStateAsync(jobId, JobState.Failed, errorCode: errCode, errorMessage: result.ErrorMessage);
            StatusMessage = $"Test print failed: {result.ErrorMessage}";
        }

        await LoadHistoricalJobsAsync();
    }

    private async Task SendRemoteTestPrintAsync()
    {
        if (SelectedRemotePrinter == null) return;

        string targetHost = !string.IsNullOrWhiteSpace(SelectedRemotePrinter.ServerHost) ? SelectedRemotePrinter.ServerHost : ServerAddressInput;
        int targetPort = SelectedRemotePrinter.ServerPort ?? (int.TryParse(ServerPortInput, out int sp) ? sp : 8270);

        StatusMessage = $"Streaming remote test print to '{SelectedRemotePrinter.DisplayName}' on {targetHost}:{targetPort}...";

        byte[] testBytes = TestPageGenerator.GenerateRawTestPage(
            printerName: SelectedRemotePrinter.DisplayName,
            serverName: targetHost,
            clientName: Environment.MachineName,
            driverName: SelectedRemotePrinter.DriverName,
            portName: $"Remote:{targetPort}");

        var jobId = Guid.NewGuid();
        using var stream = new MemoryStream(testBytes);

        try
        {
            string baseUrl = $"http://{targetHost}:{targetPort}";
            using var client = new PrintLinkClient(baseUrl, ServerTokenInput);

            // Pass exact device name if available, otherwise display name
            string targetDevice = !string.IsNullOrWhiteSpace(SelectedRemotePrinter.Name)
                ? SelectedRemotePrinter.Name
                : SelectedRemotePrinter.DisplayName;

            var response = await client.SubmitJobAsync(jobId, targetDevice, "Remote_Test_Page", stream);
            StatusMessage = $"Remote print accepted by server! Status: {response.Status}, Spooler ID: {response.SpoolerJobId}";

            await _jobRepo.GetOrRegisterJobAsync(jobId, SelectedRemotePrinter.DisplayName, "Remote_Client", "Remote_Test_Page", testBytes.Length);
            await _jobRepo.UpdateJobStateAsync(jobId, JobState.Completed, response.SpoolerJobId);
            await LoadHistoricalJobsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remote print failed: {ex.Message}";
        }
    }

    private async Task DiscoverServersAsync()
    {
        if (IsDiscoveringServers) return;

        IsDiscoveringServers = true;
        StatusMessage = "Auto-discovering shared printers across local network...";
        DiscoveredServers.Clear();

        try
        {
            var servers = await LanDiscoveryClient.DiscoverServersAsync(TimeSpan.FromSeconds(2));

            foreach (var s in servers)
            {
                DiscoveredServers.Add(s);
            }

            var printers = new List<DiscoveredNetworkPrinter>();
            var seenPrinters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in servers)
            {
                if (server.SharedPrinters != null && server.SharedPrinters.Count > 0)
                {
                    foreach (var p in server.SharedPrinters)
                    {
                        string uniqueKey = $"{server.HostAddress}:{server.Port}/{p.DisplayName}";
                        if (seenPrinters.Add(uniqueKey))
                        {
                            printers.Add(new DiscoveredNetworkPrinter(
                                HostAddress: server.HostAddress,
                                Port: server.Port,
                                ServerName: server.ServerName,
                                PrinterName: p.Name,
                                DisplayName: p.DisplayName,
                                DriverName: p.DriverName,
                                Status: p.Status ?? "Ready",
                                IsShared: true,
                                CapabilitiesSummary: p.CapabilitiesSummary));
                        }
                    }
                }
            }

            if (printers.Count > 0)
            {
                foreach (var p in printers)
                {
                    var existing = RemotePrinters.FirstOrDefault(rp => 
                        rp.DisplayName.Equals(p.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                        (rp.ServerHost == p.HostAddress || string.IsNullOrWhiteSpace(rp.ServerHost)));

                    if (existing != null)
                    {
                        existing.ServerHost = p.HostAddress;
                        existing.ServerPort = p.Port;
                        existing.ServerName = p.ServerName;
                    }
                    else
                    {
                        var vm = new PrinterItemViewModel(
                            name: p.PrinterName,
                            displayName: p.DisplayName,
                            driverName: p.DriverName,
                            status: p.Status,
                            serverHost: p.HostAddress,
                            serverPort: p.Port,
                            serverName: p.ServerName,
                            customForms: p.CustomForms);

                        string cleanName = Regex.Replace(vm.DisplayName, @"[^a-zA-Z0-9_\-\. ]", "").Trim();
                        string expectedQueue = $"{cleanName} (PrintLink)";

                        if (WindowsPrinterInstaller.IsPrinterInstalled(expectedQueue))
                        {
                            vm.IsInstalledInWindows = true;
                            vm.WindowsQueueName = expectedQueue;
                            string? savedPort = await _configRepo.GetValueAsync($"BridgePort:{vm.DisplayName}");
                            vm.BridgePort = int.TryParse(savedPort, out int bp) ? bp : 9100;
                            vm.BridgeStatus = $"Active (Port {vm.BridgePort})";

                            if (!_bridgeManager.IsBridgeRunning(expectedQueue))
                            {
                                await StartBridgeForPrinterAsync(vm);
                            }
                        }
                        else
                        {
                            vm.IsInstalledInWindows = false;
                            vm.BridgeStatus = "Inactive";
                        }

                        RemotePrinters.Add(vm);
                    }
                }

                if (SelectedRemotePrinter == null && RemotePrinters.Count > 0)
                {
                    SelectedRemotePrinter = RemotePrinters[0];
                }

                StatusMessage = $"Auto-discovery found {printers.Count} shared printer(s) on LAN.";
            }
            else if (servers.Count > 0)
            {
                StatusMessage = $"Found {servers.Count} PrintLink server(s), but no printers are shared yet.";
            }
            else
            {
                StatusMessage = "No remote printers found on LAN. Ensure host PC has sharing enabled.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auto-discovery notice: {ex.Message}";
        }
        finally
        {
            IsDiscoveringServers = false;
        }
    }

    private void SelectDiscoveredServer(object? parameter)
    {
        if (parameter is DiscoveredServer server)
        {
            ServerAddressInput = server.HostAddress;
            ServerPortInput = server.Port.ToString();
            StatusMessage = $"Selected server '{server.ServerName}' ({server.HostAddress}:{server.Port}). Connecting to server...";
            _ = TestConnectionAsync();
        }
    }

    private async Task CancelJobAsync()
    {
        if (SelectedJob == null) return;

        if (SelectedJob.State is JobState.Completed or JobState.Cancelled)
        {
            ModernDialog.ShowInformation("This job is already in a terminal state (Completed or Cancelled).", "Job Control");
            return;
        }

        StatusMessage = $"Cancelling job '{SelectedJob.DocumentName}'...";
        try
        {
            if (IsServiceConnected)
            {
                var payload = new CancelJobIpcPayload(SelectedJob.PrinterName, SelectedJob.SpoolerJobId ?? 0, SelectedJob.JobId);
                await _ipcClient.SendRequestAsync(new IpcRequest("CancelJob", JsonSerializer.Serialize(payload)));
            }
            else
            {
                if (SelectedJob.SpoolerJobId.HasValue && SelectedJob.SpoolerJobId > 0)
                {
                    _localSpooler.CancelJob(SelectedJob.PrinterName, SelectedJob.SpoolerJobId.Value);
                }
                await _jobRepo.UpdateJobStateAsync(SelectedJob.JobId, JobState.Cancelled, errorMessage: "Cancelled by user");
            }

            StatusMessage = $"Job '{SelectedJob.DocumentName}' cancelled.";
            await LoadHistoricalJobsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to cancel job: {ex.Message}";
        }
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerAddressInput)) return;

        int port = int.TryParse(ServerPortInput, out int p) ? p : 8270;
        ConnectionTestLog = $"Connecting to {ServerAddressInput}:{port}...\n";
        ServerConnectionStatus = "Connecting...";

        var report = await ConnectionTester.TestConnectionAsync(ServerAddressInput, port, ServerTokenInput);

        var sb = new StringBuilder();
        sb.AppendLine($"Connection Diagnostic Report ({DateTime.Now:HH:mm:ss}):");
        sb.AppendLine($"Target: {report.TargetHost}:{report.TargetPort}");
        sb.AppendLine($"Result: {(report.OverallSuccess ? "SUCCESS (ONLINE)" : "FAILED")}");
        sb.AppendLine($"Total Latency: {report.TotalElapsedMs} ms\n");

        foreach (var step in report.Steps)
        {
            string statusIcon = step.Success ? "[PASS]" : "[FAIL]";
            sb.AppendLine($"{statusIcon} {step.StepName} ({step.ElapsedMs} ms): {step.Message}");
        }

        if (!report.OverallSuccess && !string.IsNullOrWhiteSpace(report.FailureReason))
        {
            sb.AppendLine($"\nDiagnostic Recommendation: {report.FailureReason}");
        }

        ConnectionTestLog = sb.ToString();

        if (report.OverallSuccess)
        {
            IsServerConnected = true;
            ServerConnectionStatus = $"Connected ({ServerAddressInput}:{port})";
            StatusMessage = $"Successfully connected to PrintLink server at {ServerAddressInput}:{port}.";

            // Persist server connection settings in SQLite
            await _configRepo.SetValueAsync("Client:ServerAddress", ServerAddressInput);
            await _configRepo.SetValueAsync("Client:ServerPort", port.ToString());
            if (!string.IsNullOrWhiteSpace(ServerTokenInput))
            {
                await _configRepo.SetValueAsync("Client:ServerToken", ServerTokenInput);
            }

            await FetchRemotePrintersAsync();
        }
        else
        {
            IsServerConnected = false;
            ServerConnectionStatus = "Connection Failed";
            StatusMessage = $"Connection to server {ServerAddressInput}:{port} failed: {report.FailureReason}";
        }
    }

    private void AddRemoteServer(object? parameter)
    {
        if (string.IsNullOrWhiteSpace(ServerAddressInput)) return;

        int port = int.TryParse(ServerPortInput, out int p) ? p : 8270;
        var existing = RemoteServers.FirstOrDefault(s => s.Host == ServerAddressInput && s.Port == port);
        if (existing == null)
        {
            RemoteServers.Add(new ServerConnectionItemViewModel(ServerAddressInput, port, ServerTokenInput));
            StatusMessage = $"Added server {ServerAddressInput}:{port} to list.";
        }
    }

    private async Task FetchRemotePrintersAsync()
    {
        try
        {
            int port = int.TryParse(ServerPortInput, out int p) ? p : 8270;
            string baseUrl = $"http://{ServerAddressInput}:{port}";
            using var client = new PrintLinkClient(baseUrl, ServerTokenInput);

            var printers = await client.GetSharedPrintersAsync();
            RemotePrinters.Clear();
            foreach (var r in printers)
            {
                var vm = new PrinterItemViewModel(r)
                {
                    ServerHost = ServerAddressInput,
                    ServerPort = port,
                    ServerName = "Remote Server"
                };

                // Check if already installed in Windows Spooler
                string cleanName = Regex.Replace(vm.DisplayName, @"[^a-zA-Z0-9_\-\. ]", "").Trim();
                string expectedQueue = $"{cleanName} (PrintLink)";

                if (WindowsPrinterInstaller.IsPrinterInstalled(expectedQueue))
                {
                    vm.IsInstalledInWindows = true;
                    vm.WindowsQueueName = expectedQueue;

                    string? savedPort = await _configRepo.GetValueAsync($"BridgePort:{vm.DisplayName}");
                    if (int.TryParse(savedPort, out int bridgePort))
                    {
                        vm.BridgePort = bridgePort;
                    }
                    else
                    {
                        vm.BridgePort = 9100;
                    }

                    vm.BridgeStatus = $"Active (Port {vm.BridgePort})";

                    // Ensure bridge listener is running
                    if (!_bridgeManager.IsBridgeRunning(expectedQueue))
                    {
                        await StartBridgeForPrinterAsync(vm);
                    }
                }
                else
                {
                    vm.IsInstalledInWindows = false;
                    vm.BridgeStatus = "Inactive";
                }

                RemotePrinters.Add(vm);
            }

            if (SelectedRemotePrinter == null && RemotePrinters.Count > 0)
            {
                SelectedRemotePrinter = RemotePrinters[0];
            }

            if (RemotePrinters.Count == 0)
            {
                StatusMessage = $"Server at {ServerAddressInput}:{port} is online, but no printers are currently shared. On the server machine, toggle sharing ON for target printers.";
            }
            else
            {
                StatusMessage = $"Discovered {RemotePrinters.Count} shared remote printer(s) from {ServerAddressInput}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to fetch remote printers: {ex.Message}";
        }
    }

    private async Task StartBridgeForPrinterAsync(PrinterItemViewModel printer)
    {
        if (printer.BridgePort == null)
        {
            printer.BridgePort = PrintBridgeManager.AllocateAvailablePort();
        }
        int port = printer.BridgePort.Value;
        string queueName = printer.WindowsQueueName ?? $"{printer.DisplayName} (PrintLink)";
        string remotePrinter = !string.IsNullOrWhiteSpace(printer.Name) ? printer.Name : printer.DisplayName;

        await _bridgeManager.StartBridgeAsync(remotePrinter, queueName, port, async (jobId, targetPrinter, docTitle, data) =>
        {
            // 1. Register job in SQLite
            var (isNew, job) = await _jobRepo.GetOrRegisterJobAsync(
                jobId,
                targetPrinter,
                $"WindowsSpooler_{Environment.MachineName}",
                docTitle,
                data.Length);

            await _jobRepo.UpdateJobStateAsync(jobId, JobState.Spooling);
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadHistoricalJobsAsync);
            }

            // 2. Submit to remote PrintLink server
            try
            {
                string targetHost = !string.IsNullOrWhiteSpace(printer.ServerHost) ? printer.ServerHost : ServerAddressInput;
                int serverPort = printer.ServerPort ?? (int.TryParse(ServerPortInput, out int sp) ? sp : 8270);
                string baseUrl = $"http://{targetHost}:{serverPort}";
                using var client = new PrintLinkClient(baseUrl, ServerTokenInput);
                using var ms = new MemoryStream(data);

                var response = await client.SubmitJobAsync(jobId, targetPrinter, docTitle, ms);
                await _jobRepo.UpdateJobStateAsync(jobId, JobState.Completed, response.SpoolerJobId);
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadHistoricalJobsAsync);
                }

                return (true, response.SpoolerJobId, null);
            }
            catch (Exception ex)
            {
                string errCode = "PL-BRIDGE-ERR";
                await _jobRepo.UpdateJobStateAsync(jobId, JobState.Failed, errorCode: errCode, errorMessage: ex.Message);
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(LoadHistoricalJobsAsync);
                }

                return (false, null, ex.Message);
            }
        });
    }

    private async Task InstallWindowsPrinterAsync(PrinterItemViewModel? printer)
    {
        if (printer == null) printer = SelectedRemotePrinter;
        if (printer == null) return;

        if (printer.IsInstalledInWindows)
        {
            ModernDialog.ShowInformation(
                $"Printer '{printer.DisplayName}' is already installed in Windows Settings.",
                "Printer Already Installed");
            return;
        }

        printer.IsInstalling = true;
        StatusMessage = $"Adding '{printer.DisplayName}' to Windows with Server Driver & Custom Pages...";

        try
        {
            Func<Task<byte[]?>>? driverProvider = null;
            if (printer.IsRemote && !string.IsNullOrWhiteSpace(printer.ServerHost))
            {
                driverProvider = async () =>
                {
                    try
                    {
                        string baseUrl = $"http://{printer.ServerHost}:{printer.ServerPort ?? 8270}";
                        using var client = new PrintLinkClient(baseUrl, ServerTokenInput);
                        return await client.DownloadDriverPackageAsync(printer.Name);
                    }
                    catch
                    {
                        return null;
                    }
                };
            }

            int port = PrintBridgeManager.AllocateAvailablePort();
            var (success, queueName, portName, driverUsed, formsCount, error) = await WindowsPrinterInstaller.InstallRemotePrinterAsync(
                printer.DisplayName,
                printer.DriverName,
                port,
                printer.CustomForms,
                driverProvider);

            if (!success)
            {
                StatusMessage = $"Failed to install printer: {error}";
                ModernDialog.ShowError(
                    $"Failed to add printer to Windows:\n\n{error}",
                    "Printer Installation Error");
                return;
            }

            printer.IsInstalledInWindows = true;
            printer.WindowsQueueName = queueName;
            printer.BridgePort = port;
            printer.BridgeStatus = $"Active (Port {port})";

            // Persist in config
            await _configRepo.SetValueAsync($"BridgePort:{printer.DisplayName}", port.ToString());
            await _configRepo.SetValueAsync($"WindowsQueue:{printer.DisplayName}", queueName);

            // Start bridge
            await StartBridgeForPrinterAsync(printer);

            StatusMessage = $"Printer '{queueName}' successfully installed in Windows!";

            string formsDetail = formsCount > 0 
                ? $"• Custom Forms Synced: {formsCount} paper format(s) registered in Windows Spooler\n" 
                : (printer.CustomForms.Count > 0 ? $"• Custom Forms Available: {printer.CustomForms.Count} format(s)\n" : "");

            ModernDialog.ShowSuccess(
                $"Printer '{queueName}' successfully added to Windows Settings!\n\n" +
                $"• Queue Name: {queueName}\n" +
                $"• Driver Applied: {driverUsed}\n" +
                formsDetail +
                $"• Standard TCP/IP Port: 127.0.0.1:{port}\n" +
                $"• Remote Target Server: {(printer.ServerHost ?? ServerAddressInput)}:{(printer.ServerPort?.ToString() ?? ServerPortInput)}\n\n" +
                $"You can now print to '{queueName}' with all custom page sizes directly from Microsoft Word, Excel, Google Chrome, Adobe Acrobat, POS, ERP, or any Windows application.",
                "Printer Added to Windows");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error installing printer: {ex.Message}";
            ModernDialog.ShowError($"Error: {ex.Message}", "Installation Error");
        }
        finally
        {
            printer.IsInstalling = false;
        }
    }

    private async Task UninstallWindowsPrinterAsync(PrinterItemViewModel? printer)
    {
        if (printer == null) printer = SelectedRemotePrinter;
        if (printer == null) return;

        bool confirm = ModernDialog.ShowConfirmation(
            $"Are you sure you want to remove '{printer.WindowsQueueName ?? printer.DisplayName}' from Windows Settings?",
            "Remove Windows Printer",
            confirmText: "Yes, Remove",
            cancelText: "Cancel",
            isDestructive: true);

        if (!confirm) return;

        printer.IsInstalling = true;
        StatusMessage = $"Removing '{printer.WindowsQueueName ?? printer.DisplayName}' from Windows...";

        try
        {
            string queueName = printer.WindowsQueueName ?? $"{printer.DisplayName} (PrintLink)";
            string portName = printer.BridgePort.HasValue ? $"PL_Port_{printer.BridgePort.Value}" : "";

            await _bridgeManager.StopBridgeAsync(queueName);
            var (success, error) = await WindowsPrinterInstaller.UninstallRemotePrinterAsync(queueName, portName);

            printer.IsInstalledInWindows = false;
            printer.WindowsQueueName = null;
            printer.BridgePort = null;
            printer.BridgeStatus = "Inactive";

            await _configRepo.SetValueAsync($"BridgePort:{printer.DisplayName}", "");
            await _configRepo.SetValueAsync($"WindowsQueue:{printer.DisplayName}", "");

            StatusMessage = $"Printer '{queueName}' removed from Windows Settings.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error removing printer: {ex.Message}";
        }
        finally
        {
            printer.IsInstalling = false;
        }
    }

    private async Task ClearCompletedJobsAsync()
    {
        bool confirm = ModernDialog.ShowConfirmation(
            "Clear all completed print jobs from the log history?",
            "Clear Completed Jobs",
            confirmText: "Yes, Clear",
            cancelText: "Cancel",
            isDestructive: true);

        if (!confirm) return;

        try
        {
            int deleted = await _jobRepo.ClearJobsByStateAsync(JobState.Completed);
            await LoadHistoricalJobsAsync();
            StatusMessage = $"Cleared {deleted} completed print job(s) from history.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to clear completed jobs: {ex.Message}";
        }
    }

    private void CopyJobIdToClipboard(object? parameter)
    {
        string? idToCopy = parameter?.ToString() ?? SelectedJob?.JobId.ToString();
        if (!string.IsNullOrWhiteSpace(idToCopy))
        {
            try
            {
                System.Windows.Clipboard.SetText(idToCopy);
                StatusMessage = $"Copied Job ID to clipboard: {idToCopy}";
            }
            catch
            {
            }
        }
    }

    private async Task CreateClientTokenAsync()
    {
        string clientName = string.IsNullOrWhiteSpace(NewClientNameInput) ? $"Client_{DateTime.Now:yyyyMMdd_HHmmss}" : NewClientNameInput;
        string clientId = $"client_{Guid.NewGuid():N}";

        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string rawToken = Convert.ToBase64String(tokenBytes);
        string tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        await _clientRepo.RegisterClientAsync(clientId, clientName, tokenHash);

        GeneratedTokenOutput = rawToken;
        NewClientNameInput = "";
        await LoadClientsAsync();

        StatusMessage = $"Generated authorization token for client '{clientName}'.";
    }

    private async Task RevokeClientAsync(ClientItemViewModel? client)
    {
        if (client == null) return;

        bool confirm = ModernDialog.ShowConfirmation(
            $"Apakah Anda yakin ingin menghapus dan mencabut hak akses client '{client.FriendlyName}'?\n\nClient ID: {client.ClientId}\nPerangkat ini tidak akan dapat mencetak lagi melalui server ini.",
            "Hapus & Cabut Akses Client",
            confirmText: "Ya, Hapus & Cabut",
            cancelText: "Batal",
            isDestructive: true);

        if (!confirm) return;

        try
        {
            await _clientRepo.DeleteClientAsync(client.ClientId);
            Clients.Remove(client);
            OnPropertyChanged(nameof(AuthorizedClientsCount));
            StatusMessage = $"Berhasil menghapus dan mencabut akses client '{client.FriendlyName}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal menghapus client: {ex.Message}";
        }
    }

    private void CopyTokenToClipboard(object? parameter)
    {
        if (!string.IsNullOrWhiteSpace(GeneratedTokenOutput))
        {
            try
            {
                System.Windows.Clipboard.SetText(GeneratedTokenOutput);
                StatusMessage = "Token copied to clipboard!";
            }
            catch
            {
            }
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        StatusMessage = "Exporting sanitized diagnostic bundle...";
        try
        {
            var exporter = new DiagnosticBundleExporter(_localEnumerator, _jobRepo);
            string zipPath = await exporter.ExportBundleAsync();
            StatusMessage = $"Diagnostic bundle exported: {Path.GetFileName(zipPath)}";
            ModernDialog.ShowSuccess($"Diagnostic support bundle exported successfully to:\n\n{zipPath}\n\nAll secrets and document contents have been scrubbed.", "Diagnostic Export");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private async Task RetryJobAsync()
    {
        if (SelectedJob == null) return;

        // Anti-Duplicate Safe Retry: Never retry jobs that already succeeded
        if (SelectedJob.State == JobState.Completed)
        {
            ModernDialog.ShowWarning("This job has already completed successfully. To prevent duplicate printing, completed jobs cannot be retried.", "Safe Retry Guard");
            return;
        }

        StatusMessage = $"Retrying job '{SelectedJob.DocumentName}' on '{SelectedJob.PrinterName}'...";
        var result = _localSpooler.PrintBytes(SelectedJob.PrinterName, SelectedJob.DocumentName, Encoding.UTF8.GetBytes("Retried Document Payload\r\n\x0C"), "RAW");

        if (result.Success)
        {
            await _jobRepo.UpdateJobStateAsync(SelectedJob.JobId, JobState.Completed, result.SpoolerJobId);
            StatusMessage = $"Job retried and completed! Spooler ID: {result.SpoolerJobId}";
        }
        else
        {
            string errCode = $"PL-SPOOL-{result.Win32ErrorCode:D4}";
            await _jobRepo.UpdateJobStateAsync(SelectedJob.JobId, JobState.Failed, errorCode: errCode, errorMessage: result.ErrorMessage);
            StatusMessage = $"Retry failed: {result.ErrorMessage}";
        }

        await LoadHistoricalJobsAsync();
    }

    private async Task CompleteWizardAsync(object? parameter = null)
    {
        if (parameter != null && Enum.TryParse<OperatingMode>(parameter.ToString(), out var selectedMode))
        {
            CurrentMode = selectedMode;
        }

        await _configRepo.SetValueAsync("Setup:Completed", "true");
        await _configRepo.SetOperatingModeAsync(CurrentMode);

        if (IsServiceConnected)
        {
            await _ipcClient.SendRequestAsync(new IpcRequest("SetMode", $"\"{CurrentMode}\""));
        }
        else
        {
            await ApplyStandaloneModeAsync(CurrentMode);
        }

        IsFirstRun = false;
        StatusMessage = $"Setup Wizard completed! Active Mode: {CurrentMode}";

        SelectedTabIndex = 0; // Printers Hub

        ModernDialog.ShowSuccess(
            $"PrintLink configuration complete!\n\nOperating Mode: {CurrentMode}",
            "Setup Complete");
    }

    private static bool CheckAutoStartRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("PrintLink.Desktop") != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutoStartRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? "";
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

    public void UpdateLocalIpAddress()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    iface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = iface.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(unicast.Address))
                    {
                        LocalIpAddress = unicast.Address.ToString();
                        NotifyNetworkPropertiesChanged();
                        return;
                    }
                }
            }
        }
        catch
        {
        }
        LocalIpAddress = "127.0.0.1";
        NotifyNetworkPropertiesChanged();
    }

    private void NotifyNetworkPropertiesChanged()
    {
        OnPropertyChanged(nameof(LocalIpAddress));
        OnPropertyChanged(nameof(NetworkIdentityText));
        OnPropertyChanged(nameof(NetworkTargetText));
    }

    public async Task CheckForUpdatesAsync(bool silentIfUpToDate = false)
    {
        if (IsCheckingUpdate || IsDownloadingUpdate) return;

        IsCheckingUpdate = true;
        StatusMessage = "Checking GitHub for updates...";

        try
        {
            var release = await _updateChecker.CheckForUpdateAsync("1.0.0");
            if (release != null && release.IsNewerVersion)
            {
                StatusMessage = $"Update available: {release.TagName}";

                string notes = string.IsNullOrWhiteSpace(release.Body) ? "Perbaikan performa, UI responsif, dan stabilitas jaringan." : release.Body;
                bool confirm = ModernDialog.ShowConfirmation(
                    $"Versi baru {release.TagName} telah tersedia di GitHub!\n\n" +
                    $"Catatan Pembaruan:\n{notes}\n\n" +
                    $"Apakah Anda ingin mengunduh dan memasang pembaruan otomatis sekarang?",
                    "Pembaruan PrintLink Tersedia",
                    confirmText: "Unduh & Pasang Sekarang",
                    cancelText: "Nanti");

                if (confirm && !string.IsNullOrEmpty(release.DownloadUrl))
                {
                    await DownloadAndApplyUpdateAsync(release.DownloadUrl, release.TagName);
                }
            }
            else
            {
                StatusMessage = "L-Net PrintLink is up-to-date (v1.0.0).";
                if (!silentIfUpToDate)
                {
                    ModernDialog.ShowInformation(
                        "Anda telah menggunakan versi terbaru L-Net PrintLink (v1.0.0).\nTidak ada pembaruan baru di GitHub saat ini.",
                        "Versi Terkini");
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update check failed: {ex.Message}";
            if (!silentIfUpToDate)
            {
                ModernDialog.ShowError($"Gagal memeriksa pembaruan di GitHub:\n\n{ex.Message}", "Cek Pembaruan Gagal");
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task DownloadAndApplyUpdateAsync(string downloadUrl, string tagName)
    {
        IsDownloadingUpdate = true;
        UpdateProgressPercent = 0;
        StatusMessage = $"Downloading update {tagName}...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateProgressPercent = p;
                StatusMessage = $"Downloading update {tagName}: {p:0}%";
            });

            string installerPath = await _updateChecker.DownloadUpdateAsync(downloadUrl, progress);

            ModernDialog.ShowSuccess(
                $"Pembaruan {tagName} berhasil diunduh!\n\nAplikasi akan ditutup otomatis dan installer akan memperbarui sistem Anda sekarang.",
                "Pembaruan Siap Dipasang");

            GitHubUpdateChecker.LaunchInstallerAndExit(installerPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengunduh pembaruan: {ex.Message}";
            ModernDialog.ShowError($"Gagal mengunduh file instalasi pembaruan:\n\n{ex.Message}", "Unduh Pembaruan Gagal");
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }
}
