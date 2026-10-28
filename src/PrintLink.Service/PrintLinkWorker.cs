using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintLink.Core.Enums;
using PrintLink.Core.Interfaces;
using PrintLink.Network.Auth;
using PrintLink.Network.Discovery;
using PrintLink.Network.Jobs;
using PrintLink.Network.Server;
using PrintLink.Service.Ipc;
using PrintLink.Storage;
using PrintLink.Storage.Repositories;

namespace PrintLink.Service;

public sealed class PrintLinkWorker : BackgroundService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IConfigRepository _configRepo;
    private readonly IPrintJobRepository _jobRepo;
    private readonly IPrinterEnumerator _printerEnumerator;
    private readonly IPrinterSpooler _printerSpooler;
    private readonly IClientAuthenticator _authenticator;
    private readonly IJobTracker _jobTracker;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PrintLinkWorker> _logger;

    private PrintLinkServerHost? _serverHost;
    private LanBeaconBroadcaster? _beaconBroadcaster;
    private NamedPipeIpcServer? _ipcServer;
    private bool _isServerRunning;
    private const int DefaultPort = 8270;

    public PrintLinkWorker(
        ISqliteConnectionFactory connectionFactory,
        IConfigRepository configRepo,
        IPrintJobRepository jobRepo,
        IPrinterEnumerator printerEnumerator,
        IPrinterSpooler printerSpooler,
        IClientAuthenticator authenticator,
        IJobTracker jobTracker,
        ILoggerFactory loggerFactory,
        ILogger<PrintLinkWorker> logger)
    {
        _connectionFactory = connectionFactory;
        _configRepo = configRepo;
        _jobRepo = jobRepo;
        _printerEnumerator = printerEnumerator;
        _printerSpooler = printerSpooler;
        _authenticator = authenticator;
        _jobTracker = jobTracker;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PrintLink Background Service is starting...");

        // 1. Initialize SQLite Database Schema
        try
        {
            await DatabaseMigrator.MigrateAsync(_connectionFactory, stoppingToken);
            _logger.LogInformation("SQLite Database initialized at {DbPath}", _connectionFactory.DatabasePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run SQLite database migrations.");
            throw;
        }

        // 2. Start IPC Named Pipe Server for Desktop GUI communication
        var ipcHandler = new ServiceIpcHandler(
            _configRepo,
            _jobRepo,
            _printerEnumerator,
            _printerSpooler,
            () => _isServerRunning,
            DefaultPort,
            onModeChange: mode => ApplyModeChangeAsync(mode, stoppingToken));

        _ipcServer = new NamedPipeIpcServer(ipcHandler, _loggerFactory.CreateLogger<NamedPipeIpcServer>());
        _ipcServer.Start();

        // 3. Evaluate Operating Mode
        var mode = await _configRepo.GetOperatingModeAsync(stoppingToken);
        _logger.LogInformation("Active Operating Mode: {Mode}", mode);

        await ApplyModeChangeAsync(mode, stoppingToken);

        // Keep service alive while listening
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(5000, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("PrintLink Background Service stopping gracefully...");
        }
    }

    private async Task StartNetworkServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Kestrel Network Server on port {Port}...", DefaultPort);
            _serverHost = new PrintLinkServerHost(
                DefaultPort,
                _printerEnumerator,
                _printerSpooler,
                _authenticator,
                _jobTracker);

            await _serverHost.StartAsync(cancellationToken);
            _isServerRunning = true;
            _logger.LogInformation("Kestrel Network Server running successfully on port {Port}", DefaultPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Kestrel Network Server.");
            _isServerRunning = false;
        }
    }

    private async Task ApplyModeChangeAsync(OperatingMode newMode, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Applying dynamic operating mode: {Mode}", newMode);

        if (newMode is OperatingMode.Server or OperatingMode.Combined)
        {
            if (_serverHost == null)
            {
                await StartNetworkServerAsync(cancellationToken);
            }

            if (_beaconBroadcaster == null)
            {
                _beaconBroadcaster = new LanBeaconBroadcaster(
                    DefaultPort,
                    newMode.ToString(),
                    _printerEnumerator,
                    _loggerFactory.CreateLogger<LanBeaconBroadcaster>());
                _beaconBroadcaster.Start();
            }
        }
        else // Client mode
        {
            if (_beaconBroadcaster != null)
            {
                await _beaconBroadcaster.DisposeAsync();
                _beaconBroadcaster = null;
            }

            if (_serverHost != null)
            {
                await _serverHost.StopAsync(cancellationToken);
                await _serverHost.DisposeAsync();
                _serverHost = null;
                _isServerRunning = false;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping PrintLink Service components...");

        if (_beaconBroadcaster != null)
        {
            await _beaconBroadcaster.DisposeAsync();
            _beaconBroadcaster = null;
        }

        if (_serverHost != null)
        {
            await _serverHost.StopAsync(cancellationToken);
            await _serverHost.DisposeAsync();
            _serverHost = null;
            _isServerRunning = false;
        }

        if (_ipcServer != null)
        {
            await _ipcServer.DisposeAsync();
            _ipcServer = null;
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("PrintLink Service stopped.");
    }
}
