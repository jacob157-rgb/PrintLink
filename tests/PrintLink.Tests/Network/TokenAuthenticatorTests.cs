using PrintLink.Network.Auth;

namespace PrintLink.Tests.Network;

public class TokenAuthenticatorTests
{
    [Fact]
    public void AuthenticateToken_WithValidToken_ReturnsSuccess()
    {
        var auth = new TokenAuthenticator();
        auth.RegisterClient("client-01", "Accounting-PC", "secret-token-xyz");

        var result = auth.AuthenticateToken("Bearer secret-token-xyz", "192.168.1.50");

        Assert.True(result.IsAuthenticated);
        Assert.Equal("client-01", result.ClientId);
        Assert.Equal("Accounting-PC", result.FriendlyName);
    }

    [Fact]
    public void AuthenticateToken_WithInvalidToken_ReturnsDenied()
    {
        var auth = new TokenAuthenticator();
        auth.RegisterClient("client-01", "Accounting-PC", "secret-token-xyz");

        var result = auth.AuthenticateToken("Bearer wrong-token", "192.168.1.50");

        Assert.False(result.IsAuthenticated);
        Assert.Contains("Invalid token", result.FailureReason);
    }

    [Fact]
    public void AuthenticateToken_WithRevokedClient_ReturnsDenied()
    {
        var auth = new TokenAuthenticator();
        auth.RegisterClient("client-01", "Accounting-PC", "secret-token-xyz");
        auth.RevokeClient("client-01");

        var result = auth.AuthenticateToken("Bearer secret-token-xyz", "192.168.1.50");

        Assert.False(result.IsAuthenticated);
        Assert.Contains("revoked", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthenticateToken_RepeatedFailures_TriggersRateLimit()
    {
        var auth = new TokenAuthenticator();
        string ip = "192.168.1.99";

        // Fail 5 times
        for (int i = 0; i < 5; i++)
        {
            var res = auth.AuthenticateToken("bad-token", ip);
            Assert.False(res.IsAuthenticated);
        }

        // 6th attempt should be rate limited
        var rateLimitedResult = auth.AuthenticateToken("bad-token", ip);
        Assert.True(rateLimitedResult.IsRateLimited);
    }

    [Fact]
    public void IsClientAuthorizedForPrinter_RespectsAclRules()
    {
        var auth = new TokenAuthenticator();
        auth.SetPrinterAcl("SecureHRPrinter", ["client-hr-01", "client-hr-02"]);

        Assert.True(auth.IsClientAuthorizedForPrinter("client-hr-01", "SecureHRPrinter"));
        Assert.False(auth.IsClientAuthorizedForPrinter("client-finance-01", "SecureHRPrinter"));
    }
}
