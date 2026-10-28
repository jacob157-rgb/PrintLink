using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PrintLink.Network.Auth;

/// <summary>
/// Result of an authentication and authorization check for an incoming client request.
/// </summary>
public record AuthResult
{
    public bool IsAuthenticated { get; init; }
    public string? ClientId { get; init; }
    public string? FriendlyName { get; init; }
    public bool IsRateLimited { get; init; }
    public string? FailureReason { get; init; }

    public static AuthResult Success(string clientId, string friendlyName) =>
        new() { IsAuthenticated = true, ClientId = clientId, FriendlyName = friendlyName };

    public static AuthResult Denied(string reason) =>
        new() { IsAuthenticated = false, FailureReason = reason };

    public static AuthResult RateLimited() =>
        new() { IsAuthenticated = false, IsRateLimited = true, FailureReason = "Too many failed attempts. Rate limit applied." };
}

/// <summary>
/// Contract for validating client bearer tokens and checking printer authorization.
/// </summary>
public interface IClientAuthenticator
{
    AuthResult AuthenticateToken(string? token, string? clientIp);
    bool IsClientAuthorizedForPrinter(string clientId, string printerShareName);
}

/// <summary>
/// Production in-memory token authenticator with SHA-256 hash comparison and brute-force rate-limiting.
/// </summary>
public sealed class TokenAuthenticator : IClientAuthenticator
{
    private readonly ConcurrentDictionary<string, ClientIdentity> _clientTokens = new();
    private readonly ConcurrentDictionary<string, List<string>> _printerAcls = new(); // printerShareName -> list of allowed clientIds ("*" means all)
    private readonly ConcurrentDictionary<string, RateLimitTracker> _ipRateLimits = new();

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When true (default for office LANs), clients without a Bearer token can connect and access open shared printers.
    /// </summary>
    public bool AllowAnonymousLanClients { get; set; }

    public TokenAuthenticator(bool allowAnonymousLanClients = true)
    {
        AllowAnonymousLanClients = allowAnonymousLanClients;
    }

    private record ClientIdentity(string ClientId, string FriendlyName, string TokenHash, bool IsRevoked);
    private record RateLimitTracker(int Failures, DateTimeOffset LastFailure);

    public void RegisterClient(string clientId, string friendlyName, string rawToken)
    {
        string tokenHash = ComputeSha256(rawToken);
        _clientTokens[tokenHash] = new ClientIdentity(clientId, friendlyName, tokenHash, false);
    }

    public void RevokeClient(string clientId)
    {
        foreach (var (hash, identity) in _clientTokens)
        {
            if (identity.ClientId == clientId)
            {
                _clientTokens[hash] = identity with { IsRevoked = true };
            }
        }
    }

    public void SetPrinterAcl(string printerShareName, IEnumerable<string> allowedClientIds)
    {
        _printerAcls[printerShareName] = allowedClientIds.ToList();
    }

    public AuthResult AuthenticateToken(string? token, string? clientIp)
    {
        string ipKey = clientIp ?? "unknown";

        // Check rate limiting
        if (_ipRateLimits.TryGetValue(ipKey, out var tracker))
        {
            if (tracker.Failures >= MaxFailedAttempts)
            {
                if (DateTimeOffset.UtcNow - tracker.LastFailure < LockoutDuration)
                {
                    return AuthResult.RateLimited();
                }
                // Lockout expired, reset
                _ipRateLimits.TryRemove(ipKey, out _);
            }
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            if (AllowAnonymousLanClients)
            {
                return AuthResult.Success("anonymous_lan_client", $"LAN Client ({ipKey})");
            }

            RecordFailure(ipKey);
            return AuthResult.Denied("Bearer token missing.");
        }

        // Clean bearer prefix if present
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token["Bearer ".Length..].Trim();
        }

        string tokenHash = ComputeSha256(token);
        if (_clientTokens.TryGetValue(tokenHash, out var client))
        {
            if (client.IsRevoked)
            {
                RecordFailure(ipKey);
                return AuthResult.Denied("Client credential has been revoked.");
            }

            // Reset rate limit on success
            _ipRateLimits.TryRemove(ipKey, out _);
            return AuthResult.Success(client.ClientId, client.FriendlyName);
        }

        RecordFailure(ipKey);
        return AuthResult.Denied("Invalid token.");
    }

    public bool IsClientAuthorizedForPrinter(string clientId, string printerShareName)
    {
        if (!_printerAcls.TryGetValue(printerShareName, out var allowedClients))
        {
            // Default policy: if no specific ACL is set, all authenticated clients can access
            return true;
        }

        return allowedClients.Contains("*") || allowedClients.Contains(clientId);
    }

    private void RecordFailure(string ipKey)
    {
        _ipRateLimits.AddOrUpdate(
            ipKey,
            _ => new RateLimitTracker(1, DateTimeOffset.UtcNow),
            (_, existing) => new RateLimitTracker(existing.Failures + 1, DateTimeOffset.UtcNow));
    }

    private static string ComputeSha256(string raw)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(bytes);
    }
}
