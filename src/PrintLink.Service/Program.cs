using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintLink.Core.Interfaces;
using PrintLink.Network.Auth;
using PrintLink.Network.Jobs;
using PrintLink.Service;
using PrintLink.Spooler;
using PrintLink.Storage;
using PrintLink.Storage.Printers;
using PrintLink.Storage.Repositories;

var builder = Host.CreateApplicationBuilder(args);

// Enable running as a Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PrintLinkService";
});

// Configure Database Path: %ProgramData%\LNet\PrintLink\printlink.db
string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
string dbDir = Path.Combine(programData, "LNet", "PrintLink");
string dbPath = Path.Combine(dbDir, "printlink.db");

builder.Services.AddSingleton<ISqliteConnectionFactory>(_ => new SqliteConnectionFactory(dbPath));
builder.Services.AddSingleton<IConfigRepository, SqliteConfigRepository>();
builder.Services.AddSingleton<IPrintJobRepository, SqlitePrintJobRepository>();
builder.Services.AddSingleton<IAuthorizedClientRepository, SqliteAuthorizedClientRepository>();

// Windows Spooler & Network singletons
builder.Services.AddSingleton<WindowsPrinterEnumerator>();
builder.Services.AddSingleton<IPrinterEnumerator>(sp => new ConfiguredPrinterEnumerator(
    sp.GetRequiredService<WindowsPrinterEnumerator>(),
    sp.GetRequiredService<IConfigRepository>()));
builder.Services.AddSingleton<IPrinterSpooler, WindowsPrinterSpooler>();
builder.Services.AddSingleton<IClientAuthenticator, TokenAuthenticator>();
builder.Services.AddSingleton<IJobTracker, InMemoryJobTracker>();

// Register Main Background Worker
builder.Services.AddHostedService<PrintLinkWorker>();

var host = builder.Build();
host.Run();
