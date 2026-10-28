using Microsoft.Data.Sqlite;

namespace PrintLink.Storage;

public static class DatabaseMigrator
{
    private const string SchemaSql = @"
        CREATE TABLE IF NOT EXISTS Config (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL,
            UpdatedAt DATETIME NOT NULL
        );

        CREATE TABLE IF NOT EXISTS SharedPrinters (
            PrinterId TEXT PRIMARY KEY,
            WindowsPrinterName TEXT NOT NULL,
            ShareName TEXT NOT NULL UNIQUE,
            DriverName TEXT,
            PortName TEXT,
            IsEnabled INTEGER NOT NULL DEFAULT 1,
            AllowedClientsJson TEXT NOT NULL DEFAULT '[""*""]',
            CreatedAt DATETIME NOT NULL,
            UpdatedAt DATETIME NOT NULL
        );

        CREATE TABLE IF NOT EXISTS AuthorizedClients (
            ClientId TEXT PRIMARY KEY,
            FriendlyName TEXT NOT NULL,
            TokenHash TEXT NOT NULL UNIQUE,
            IsRevoked INTEGER NOT NULL DEFAULT 0,
            CreatedAt DATETIME NOT NULL,
            LastSeenAt DATETIME
        );

        CREATE TABLE IF NOT EXISTS PrintJobs (
            JobId TEXT PRIMARY KEY,
            PrinterName TEXT NOT NULL,
            ClientId TEXT NOT NULL,
            DocumentName TEXT NOT NULL,
            TotalBytes INTEGER NOT NULL,
            State INTEGER NOT NULL,
            SpoolerJobId INTEGER,
            ErrorCode TEXT,
            ErrorMessage TEXT,
            CreatedAt DATETIME NOT NULL,
            CompletedAt DATETIME
        );

        CREATE INDEX IF NOT EXISTS IX_PrintJobs_CreatedAt ON PrintJobs(CreatedAt);
        CREATE INDEX IF NOT EXISTS IX_PrintJobs_State ON PrintJobs(State);

        CREATE TABLE IF NOT EXISTS AuditLogs (
            LogId INTEGER PRIMARY KEY AUTOINCREMENT,
            Timestamp DATETIME NOT NULL,
            Severity TEXT NOT NULL,
            Source TEXT NOT NULL,
            Message TEXT NOT NULL,
            MetadataJson TEXT
        );
    ";

    public static async Task MigrateAsync(ISqliteConnectionFactory connectionFactory, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
