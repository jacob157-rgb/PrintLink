using PrintLink.Core.Enums;
using PrintLink.Storage;
using PrintLink.Storage.Repositories;

namespace PrintLink.Tests.Storage;

public class SqliteStorageTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteStorageTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"printlink_test_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_testDbPath);
    }

    [Fact]
    public async Task MigrateAsync_CreatesAllRequiredTables()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Config', 'SharedPrinters', 'AuthorizedClients', 'PrintJobs', 'AuditLogs');";

        long tableCount = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(5, tableCount);
    }

    [Fact]
    public async Task ConfigRepository_GetAndSet_PersistsValues()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var repo = new SqliteConfigRepository(_connectionFactory);

        await repo.SetValueAsync("CustomKey", "CustomValue123");
        string? retrieved = await repo.GetValueAsync("CustomKey");
        Assert.Equal("CustomValue123", retrieved);

        await repo.SetOperatingModeAsync(OperatingMode.Server);
        var mode = await repo.GetOperatingModeAsync();
        Assert.Equal(OperatingMode.Server, mode);
    }

    [Fact]
    public async Task AuthorizedClientRepository_RegisterAndRevoke_WorksDeterministically()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var repo = new SqliteAuthorizedClientRepository(_connectionFactory);

        string tokenHash = "hash_1234567890abcdef";
        await repo.RegisterClientAsync("client-pos-1", "POS Counter 1", tokenHash);

        var client = await repo.FindByTokenHashAsync(tokenHash);
        Assert.NotNull(client);
        Assert.Equal("client-pos-1", client.ClientId);
        Assert.Equal("POS Counter 1", client.FriendlyName);
        Assert.False(client.IsRevoked);

        await repo.RevokeClientAsync("client-pos-1");
        var revokedClient = await repo.FindByTokenHashAsync(tokenHash);
        Assert.NotNull(revokedClient);
        Assert.True(revokedClient.IsRevoked);

        await repo.DeleteClientAsync("client-pos-1");
        var deletedClient = await repo.FindByTokenHashAsync(tokenHash);
        Assert.Null(deletedClient);
        var allClients = await repo.GetAllClientsAsync();
        Assert.DoesNotContain(allClients, c => c.ClientId == "client-pos-1");
    }

    [Fact]
    public async Task PrintJobRepository_IdempotencyAndStateTransitions_SurvivesAcrossQueries()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var repo = new SqlitePrintJobRepository(_connectionFactory);

        var jobId = Guid.NewGuid();

        // 1. Initial job registration
        var (isNew, job) = await repo.GetOrRegisterJobAsync(jobId, "LaserPrinter", "client-1", "Invoice.pdf", 10240);
        Assert.True(isNew);
        Assert.Equal(JobState.Accepted, job.State);

        // 2. Duplicate registration with same JobId (Idempotency)
        var (isNewDuplicate, duplicateJob) = await repo.GetOrRegisterJobAsync(jobId, "LaserPrinter", "client-1", "Invoice.pdf", 10240);
        Assert.False(isNewDuplicate);
        Assert.Equal(jobId, duplicateJob.JobId);

        // 3. State update to Spooling, then Completed
        await repo.UpdateJobStateAsync(jobId, JobState.Spooling);
        var spoolingJob = await repo.GetJobAsync(jobId);
        Assert.NotNull(spoolingJob);
        Assert.Equal(JobState.Spooling, spoolingJob.State);

        await repo.UpdateJobStateAsync(jobId, JobState.Completed, spoolerJobId: 99);
        var completedJob = await repo.GetJobAsync(jobId);
        Assert.NotNull(completedJob);
        Assert.Equal(JobState.Completed, completedJob.State);
        Assert.Equal(99, completedJob.SpoolerJobId);
        Assert.NotNull(completedJob.CompletedAt);

        // 4. Query recent jobs
        var recent = await repo.GetRecentJobsAsync(10);
        Assert.Single(recent);
        Assert.Equal(jobId, recent[0].JobId);
    }

    [Fact]
    public async Task PrintJobRepository_ClearJobsByState_OnlyDeletesTargetState()
    {
        await DatabaseMigrator.MigrateAsync(_connectionFactory);
        var repo = new SqlitePrintJobRepository(_connectionFactory);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        await repo.GetOrRegisterJobAsync(id1, "Printer1", "c1", "doc1.txt", 100);
        await repo.UpdateJobStateAsync(id1, JobState.Completed);

        await repo.GetOrRegisterJobAsync(id2, "Printer1", "c1", "doc2.txt", 200);
        await repo.UpdateJobStateAsync(id2, JobState.Completed);

        await repo.GetOrRegisterJobAsync(id3, "Printer1", "c1", "doc3.txt", 300);
        await repo.UpdateJobStateAsync(id3, JobState.Failed);

        int deleted = await repo.ClearJobsByStateAsync(JobState.Completed);
        Assert.Equal(2, deleted);

        var remaining = await repo.GetRecentJobsAsync(10);
        Assert.Single(remaining);
        Assert.Equal(id3, remaining[0].JobId);
        Assert.Equal(JobState.Failed, remaining[0].State);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
        }
    }
}
