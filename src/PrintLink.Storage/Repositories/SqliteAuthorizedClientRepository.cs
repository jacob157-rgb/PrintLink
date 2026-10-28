using Microsoft.Data.Sqlite;

namespace PrintLink.Storage.Repositories;

public record AuthorizedClientRecord(string ClientId, string FriendlyName, string TokenHash, bool IsRevoked, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt);

public interface IAuthorizedClientRepository
{
    Task RegisterClientAsync(string clientId, string friendlyName, string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeClientAsync(string clientId, CancellationToken cancellationToken = default);
    Task DeleteClientAsync(string clientId, CancellationToken cancellationToken = default);
    Task<AuthorizedClientRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task UpdateLastSeenAsync(string clientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthorizedClientRecord>> GetAllClientsAsync(CancellationToken cancellationToken = default);
}

public sealed class SqliteAuthorizedClientRepository : IAuthorizedClientRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteAuthorizedClientRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RegisterClientAsync(string clientId, string friendlyName, string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO AuthorizedClients (ClientId, FriendlyName, TokenHash, IsRevoked, CreatedAt)
            VALUES (@clientId, @friendlyName, @tokenHash, 0, @createdAt)
            ON CONFLICT(ClientId) DO UPDATE SET
                FriendlyName = excluded.FriendlyName,
                TokenHash = excluded.TokenHash,
                IsRevoked = 0;
        ";
        command.Parameters.AddWithValue("@clientId", clientId);
        command.Parameters.AddWithValue("@friendlyName", friendlyName);
        command.Parameters.AddWithValue("@tokenHash", tokenHash);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RevokeClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AuthorizedClients SET IsRevoked = 1 WHERE ClientId = @clientId;";
        command.Parameters.AddWithValue("@clientId", clientId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AuthorizedClients WHERE ClientId = @clientId;";
        command.Parameters.AddWithValue("@clientId", clientId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuthorizedClientRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ClientId, FriendlyName, TokenHash, IsRevoked, CreatedAt, LastSeenAt FROM AuthorizedClients WHERE TokenHash = @hash LIMIT 1;";
        command.Parameters.AddWithValue("@hash", tokenHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapReader(reader);
        }

        return null;
    }

    public async Task UpdateLastSeenAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AuthorizedClients SET LastSeenAt = @now WHERE ClientId = @clientId;";
        command.Parameters.AddWithValue("@clientId", clientId);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuthorizedClientRecord>> GetAllClientsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<AuthorizedClientRecord>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ClientId, FriendlyName, TokenHash, IsRevoked, CreatedAt, LastSeenAt FROM AuthorizedClients ORDER BY CreatedAt DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReader(reader));
        }

        return list;
    }

    private static AuthorizedClientRecord MapReader(SqliteDataReader reader)
    {
        string clientId = reader.GetString(0);
        string friendlyName = reader.GetString(1);
        string tokenHash = reader.GetString(2);
        bool isRevoked = reader.GetInt32(3) == 1;
        var createdAt = DateTimeOffset.Parse(reader.GetString(4));
        DateTimeOffset? lastSeenAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5));

        return new AuthorizedClientRecord(clientId, friendlyName, tokenHash, isRevoked, createdAt, lastSeenAt);
    }
}
