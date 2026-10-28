using Microsoft.Data.Sqlite;
using PrintLink.Core.Enums;

namespace PrintLink.Storage.Repositories;

public interface IConfigRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> GetValuesByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    Dictionary<string, string> GetValuesByPrefix(string prefix);
    Task<OperatingMode> GetOperatingModeAsync(CancellationToken cancellationToken = default);
    Task SetOperatingModeAsync(OperatingMode mode, CancellationToken cancellationToken = default);
}

public sealed class SqliteConfigRepository : IConfigRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private const string OperatingModeKey = "System:OperatingMode";

    public SqliteConfigRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Config WHERE Key = @key LIMIT 1;";
        command.Parameters.AddWithValue("@key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString();
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Config (Key, Value, UpdatedAt)
            VALUES (@key, @value, @updatedAt)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedAt = excluded.UpdatedAt;
        ";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Dictionary<string, string>> GetValuesByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Config WHERE Key LIKE @pattern;";
        command.Parameters.AddWithValue("@pattern", prefix + "%");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    public Dictionary<string, string> GetValuesByPrefix(string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Key, Value FROM Config WHERE Key LIKE @pattern;";
            command.Parameters.AddWithValue("@pattern", prefix + "%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch
        {
        }

        return result;
    }

    public async Task<OperatingMode> GetOperatingModeAsync(CancellationToken cancellationToken = default)
    {
        string? val = await GetValueAsync(OperatingModeKey, cancellationToken);
        if (Enum.TryParse<OperatingMode>(val, out var mode))
        {
            return mode;
        }

        return OperatingMode.Client; // Default safe fallback
    }

    public Task SetOperatingModeAsync(OperatingMode mode, CancellationToken cancellationToken = default)
    {
        return SetValueAsync(OperatingModeKey, mode.ToString(), cancellationToken);
    }
}
