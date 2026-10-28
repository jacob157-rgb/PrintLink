using Microsoft.Data.Sqlite;
using PrintLink.Core.Enums;
using PrintLink.Core.Models;

namespace PrintLink.Storage.Repositories;

public interface IPrintJobRepository
{
    Task<(bool IsNew, PrintJob Job)> GetOrRegisterJobAsync(
        Guid jobId,
        string printerName,
        string clientId,
        string docName,
        long totalBytes,
        CancellationToken cancellationToken = default);

    Task UpdateJobStateAsync(
        Guid jobId,
        JobState state,
        int? spoolerJobId = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    Task<PrintJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrintJob>> GetRecentJobsAsync(int limit = 50, CancellationToken cancellationToken = default);

    Task<int> PurgeOldJobsAsync(int retentionDays, CancellationToken cancellationToken = default);

    Task<int> ClearJobsByStateAsync(JobState state, CancellationToken cancellationToken = default);
}

public sealed class SqlitePrintJobRepository : IPrintJobRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqlitePrintJobRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<(bool IsNew, PrintJob Job)> GetOrRegisterJobAsync(
        Guid jobId,
        string printerName,
        string clientId,
        string docName,
        long totalBytes,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // Check if job already exists (Idempotency)
        var existing = await GetJobInternalAsync(connection, jobId, cancellationToken);
        if (existing != null)
        {
            return (false, existing);
        }

        var newJob = new PrintJob
        {
            JobId = jobId,
            PrinterName = printerName,
            ClientId = clientId,
            DocumentName = docName,
            TotalBytes = totalBytes,
            State = JobState.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO PrintJobs (JobId, PrinterName, ClientId, DocumentName, TotalBytes, State, CreatedAt)
            VALUES (@jobId, @printer, @client, @doc, @bytes, @state, @createdAt);
        ";
        insertCmd.Parameters.AddWithValue("@jobId", jobId.ToString());
        insertCmd.Parameters.AddWithValue("@printer", printerName);
        insertCmd.Parameters.AddWithValue("@client", clientId);
        insertCmd.Parameters.AddWithValue("@doc", docName);
        insertCmd.Parameters.AddWithValue("@bytes", totalBytes);
        insertCmd.Parameters.AddWithValue("@state", (int)JobState.Accepted);
        insertCmd.Parameters.AddWithValue("@createdAt", newJob.CreatedAt.ToString("O"));

        try
        {
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            return (true, newJob);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // Constraint failure (PRIMARY KEY)
        {
            var reloaded = await GetJobInternalAsync(connection, jobId, cancellationToken);
            return (false, reloaded ?? newJob);
        }
    }

    public async Task UpdateJobStateAsync(
        Guid jobId,
        JobState state,
        int? spoolerJobId = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        bool isFinal = state is JobState.Completed or JobState.Failed;
        command.CommandText = @"
            UPDATE PrintJobs
            SET State = @state,
                SpoolerJobId = COALESCE(@spoolerId, SpoolerJobId),
                ErrorCode = COALESCE(@errCode, ErrorCode),
                ErrorMessage = COALESCE(@errMsg, ErrorMessage),
                CompletedAt = CASE WHEN @isFinal = 1 THEN @now ELSE CompletedAt END
            WHERE JobId = @jobId;
        ";

        command.Parameters.AddWithValue("@jobId", jobId.ToString());
        command.Parameters.AddWithValue("@state", (int)state);
        command.Parameters.AddWithValue("@spoolerId", (object?)spoolerJobId ?? DBNull.Value);
        command.Parameters.AddWithValue("@errCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@errMsg", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@isFinal", isFinal ? 1 : 0);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PrintJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await GetJobInternalAsync(connection, jobId, cancellationToken);
    }

    public async Task<IReadOnlyList<PrintJob>> GetRecentJobsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var list = new List<PrintJob>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT JobId, PrinterName, ClientId, DocumentName, TotalBytes, State, SpoolerJobId, ErrorCode, ErrorMessage, CreatedAt, CompletedAt
            FROM PrintJobs
            ORDER BY CreatedAt DESC
            LIMIT @limit;
        ";
        command.Parameters.AddWithValue("@limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReader(reader));
        }

        return list;
    }

    public async Task<int> PurgeOldJobsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O");
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PrintJobs WHERE CreatedAt < @cutoff;";
        command.Parameters.AddWithValue("@cutoff", cutoff);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ClearJobsByStateAsync(JobState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PrintJobs WHERE State = @state;";
        command.Parameters.AddWithValue("@state", (int)state);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PrintJob?> GetJobInternalAsync(SqliteConnection connection, Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT JobId, PrinterName, ClientId, DocumentName, TotalBytes, State, SpoolerJobId, ErrorCode, ErrorMessage, CreatedAt, CompletedAt
            FROM PrintJobs
            WHERE JobId = @jobId
            LIMIT 1;
        ";
        command.Parameters.AddWithValue("@jobId", jobId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapReader(reader);
        }

        return null;
    }

    private static PrintJob MapReader(SqliteDataReader reader)
    {
        return new PrintJob
        {
            JobId = Guid.Parse(reader.GetString(0)),
            PrinterName = reader.GetString(1),
            ClientId = reader.GetString(2),
            DocumentName = reader.GetString(3),
            TotalBytes = reader.GetInt64(4),
            State = (JobState)reader.GetInt32(5),
            SpoolerJobId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            ErrorCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(9)),
            CompletedAt = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10))
        };
    }
}
