using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PrintLink.Network.Client;

namespace PrintLink.Diagnostics;

public record ConnectionStepResult(string StepName, bool Success, string Message, long ElapsedMs);

public record ConnectionTestReport(
    string TargetHost,
    int TargetPort,
    bool OverallSuccess,
    long TotalElapsedMs,
    IReadOnlyList<ConnectionStepResult> Steps,
    string? FailureReason);

public static class ConnectionTester
{
    public static async Task<ConnectionTestReport> TestConnectionAsync(
        string hostOrIp,
        int port = 8270,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostOrIp);

        var steps = new List<ConnectionStepResult>();
        var totalSw = Stopwatch.StartNew();
        bool overallSuccess = true;
        string? failureReason = null;

        // Step 1: DNS / IP Resolution
        var dnsSw = Stopwatch.StartNew();
        IPAddress[] addresses = [];
        try
        {
            addresses = await Dns.GetHostAddressesAsync(hostOrIp, cancellationToken);
            dnsSw.Stop();
            if (addresses.Length > 0)
            {
                steps.Add(new ConnectionStepResult("DNS / IP Resolution", true, $"Resolved to: {string.Join(", ", addresses.Select(a => a.ToString()))}", dnsSw.ElapsedMilliseconds));
            }
            else
            {
                overallSuccess = false;
                failureReason = "No IP addresses resolved for hostname.";
                steps.Add(new ConnectionStepResult("DNS / IP Resolution", false, failureReason, dnsSw.ElapsedMilliseconds));
                return new ConnectionTestReport(hostOrIp, port, false, totalSw.ElapsedMilliseconds, steps, failureReason);
            }
        }
        catch (Exception ex)
        {
            dnsSw.Stop();
            overallSuccess = false;
            failureReason = $"DNS resolution failed: {ex.Message}";
            steps.Add(new ConnectionStepResult("DNS / IP Resolution", false, failureReason, dnsSw.ElapsedMilliseconds));
            return new ConnectionTestReport(hostOrIp, port, false, totalSw.ElapsedMilliseconds, steps, failureReason);
        }

        // Step 2: TCP Port Reachability
        var tcpSw = Stopwatch.StartNew();
        try
        {
            using var tcpClient = new TcpClient();
            var targetIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
            await tcpClient.ConnectAsync(targetIp, port, cancellationToken);
            tcpSw.Stop();
            steps.Add(new ConnectionStepResult("TCP Socket Connection", true, $"Connected to {targetIp}:{port} in {tcpSw.ElapsedMilliseconds} ms", tcpSw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            tcpSw.Stop();
            overallSuccess = false;
            failureReason = $"TCP port {port} is closed or blocked by firewall: {ex.Message}";
            steps.Add(new ConnectionStepResult("TCP Socket Connection", false, failureReason, tcpSw.ElapsedMilliseconds));
            return new ConnectionTestReport(hostOrIp, port, false, totalSw.ElapsedMilliseconds, steps, failureReason);
        }

        // Step 3: HTTPS / TLS Handshake & Health Check
        var httpSw = Stopwatch.StartNew();
        try
        {
            string baseUrl = $"http://{hostOrIp}:{port}";
            using var client = new PrintLinkClient(baseUrl, token);
            var health = await client.CheckHealthAsync(cancellationToken);
            httpSw.Stop();

            if (health != null && health.Status == "OK")
            {
                steps.Add(new ConnectionStepResult(
                    "Service Health & Protocol",
                    true,
                    $"Service Online! Machine: {health.MachineName}, Version: {health.Version}, Mode: {health.Mode}",
                    httpSw.ElapsedMilliseconds));
            }
            else
            {
                overallSuccess = false;
                failureReason = "Health endpoint returned unexpected payload.";
                steps.Add(new ConnectionStepResult("Service Health & Protocol", false, failureReason, httpSw.ElapsedMilliseconds));
            }
        }
        catch (Exception ex)
        {
            httpSw.Stop();
            overallSuccess = false;
            failureReason = $"HTTP/TLS API handshake failed: {ex.Message}";
            steps.Add(new ConnectionStepResult("Service Health & Protocol", false, failureReason, httpSw.ElapsedMilliseconds));
            return new ConnectionTestReport(hostOrIp, port, false, totalSw.ElapsedMilliseconds, steps, failureReason);
        }

        // Step 4: Token Authentication (if token provided)
        if (!string.IsNullOrWhiteSpace(token))
        {
            var authSw = Stopwatch.StartNew();
            try
            {
                string baseUrl = $"http://{hostOrIp}:{port}";
                using var client = new PrintLinkClient(baseUrl, token);
                var printers = await client.GetSharedPrintersAsync(cancellationToken);
                authSw.Stop();
                steps.Add(new ConnectionStepResult(
                    "Token Authentication & Access",
                    true,
                    $"Authentication Successful! Authorized for {printers.Count} shared printer(s).",
                    authSw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                authSw.Stop();
                overallSuccess = false;
                failureReason = $"Authentication rejected: {ex.Message}";
                steps.Add(new ConnectionStepResult("Token Authentication & Access", false, failureReason, authSw.ElapsedMilliseconds));
            }
        }

        totalSw.Stop();
        return new ConnectionTestReport(hostOrIp, port, overallSuccess, totalSw.ElapsedMilliseconds, steps, failureReason);
    }
}
