using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintLink.Core.Enums;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Models;
using PrintLink.Network.Auth;
using PrintLink.Network.Jobs;
using PrintLink.Spooler;

namespace PrintLink.Network.Server;

public sealed class PrintLinkServerHost : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly int _port;
    private readonly IPrinterEnumerator _printerEnumerator;
    private readonly IPrinterSpooler _printerSpooler;
    private readonly IClientAuthenticator _authenticator;
    private readonly IJobTracker _jobTracker;
    private readonly long _maxPayloadBytes;

    public PrintLinkServerHost(
        int port,
        IPrinterEnumerator printerEnumerator,
        IPrinterSpooler printerSpooler,
        IClientAuthenticator authenticator,
        IJobTracker jobTracker,
        long maxPayloadBytes = 50 * 1024 * 1024) // 50 MB default cap
    {
        _port = port;
        _printerEnumerator = printerEnumerator;
        _printerSpooler = printerSpooler;
        _authenticator = authenticator;
        _jobTracker = jobTracker;
        _maxPayloadBytes = maxPayloadBytes;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders(); // Use clean logging or integrate with host
        builder.WebHost.UseKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = _maxPayloadBytes;
            options.AllowSynchronousIO = true;
            options.ListenAnyIP(_port);
        });

        var app = builder.Build();

        // 1. Health check endpoint (anonymous)
        app.MapGet("/api/v1/health", () => Results.Ok(new
        {
            Status = "OK",
            Version = "1.0.0",
            MachineName = Environment.MachineName,
            Mode = "Server",
            Timestamp = DateTimeOffset.UtcNow
        }));

        // 2. Shared printers enumeration (authenticated + filtered)
        app.MapGet("/api/v1/printers", (HttpContext context) =>
        {
            var auth = AuthenticateRequest(context);
            if (!auth.IsAuthenticated)
            {
                return auth.IsRateLimited ? Results.StatusCode(429) : Results.Unauthorized();
            }

            var localPrinters = _printerEnumerator.GetPrinters();
            var authorizedPrinters = localPrinters
                .Where(p => p.IsShared && _authenticator.IsClientAuthorizedForPrinter(auth.ClientId!, p.DisplayName))
                .ToList();

            return Results.Ok(authorizedPrinters);
        });

        // 2b. Download printer driver package (for client 1-click automatic driver install)
        app.MapGet("/api/v1/printers/{printerName}/driver-package", (string printerName, HttpContext context) =>
        {
            var auth = AuthenticateRequest(context);
            if (!auth.IsAuthenticated)
            {
                return auth.IsRateLimited ? Results.StatusCode(429) : Results.Unauthorized();
            }

            var allPrinters = _printerEnumerator.GetPrinters();
            var matchedPrinter = allPrinters.FirstOrDefault(p =>
                string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.DisplayName, printerName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.ShareName, printerName, StringComparison.OrdinalIgnoreCase));

            if (matchedPrinter == null || !matchedPrinter.IsShared)
            {
                return Results.NotFound(new { Error = "Printer not found or not shared." });
            }

            if (string.IsNullOrWhiteSpace(matchedPrinter.DriverName))
            {
                return Results.NotFound(new { Error = "Printer does not specify a driver name." });
            }

            byte[]? zipBytes = WindowsPrinterInstaller.CreateDriverPackageZip(matchedPrinter.DriverName);
            if (zipBytes == null || zipBytes.Length == 0)
            {
                return Results.NotFound(new { Error = $"Driver package for '{matchedPrinter.DriverName}' could not be packaged from DriverStore." });
            }

            return Results.File(zipBytes, "application/zip", $"{matchedPrinter.DriverName}.zip");
        });

        // 3. Submit print job (authenticated + streaming payload)
        app.MapPost("/api/v1/jobs/submit", async (HttpContext context) =>
        {
            try
            {
                var auth = AuthenticateRequest(context);
                if (!auth.IsAuthenticated)
                {
                    return auth.IsRateLimited ? Results.StatusCode(429) : Results.Unauthorized();
                }

                // Extract headers
                if (!context.Request.Headers.TryGetValue("X-PrintLink-JobId", out var jobIdHeader) ||
                    !Guid.TryParse(jobIdHeader, out var jobId))
                {
                    return Results.BadRequest(new { Error = "Missing or invalid 'X-PrintLink-JobId' header." });
                }

                if (!context.Request.Headers.TryGetValue("X-PrintLink-Printer", out var targetPrinterHeader) ||
                    string.IsNullOrWhiteSpace(targetPrinterHeader))
                {
                    return Results.BadRequest(new { Error = "Missing 'X-PrintLink-Printer' header." });
                }

                string requestedPrinter = targetPrinterHeader.ToString();
                string docName = context.Request.Headers.TryGetValue("X-PrintLink-DocName", out var docNameHeader) && !string.IsNullOrWhiteSpace(docNameHeader)
                    ? docNameHeader.ToString()
                    : "PrintLink_Remote_Document";

                string dataType = context.Request.Headers.TryGetValue("X-PrintLink-DataType", out var dtHeader) && !string.IsNullOrWhiteSpace(dtHeader)
                    ? dtHeader.ToString()
                    : "RAW";

                // Lookup printer by Windows Name, DisplayName, or ShareName
                var allPrinters = _printerEnumerator.GetPrinters();
                var matchedPrinter = allPrinters.FirstOrDefault(p =>
                    string.Equals(p.Name, requestedPrinter, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.DisplayName, requestedPrinter, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.ShareName, requestedPrinter, StringComparison.OrdinalIgnoreCase));

                if (matchedPrinter == null)
                {
                    return Results.NotFound(new { Error = $"Printer '{requestedPrinter}' not found on server." });
                }

                if (!matchedPrinter.IsShared)
                {
                    return Results.StatusCode(403); // Forbidden - printer is not shared
                }

                // Verify printer authorization against friendly display / share name
                if (!_authenticator.IsClientAuthorizedForPrinter(auth.ClientId!, matchedPrinter.DisplayName))
                {
                    return Results.Forbid();
                }

                long contentLength = context.Request.ContentLength ?? 0;
                if (contentLength > _maxPayloadBytes)
                {
                    return Results.StatusCode(413); // Payload Too Large
                }

                // Register job with idempotency protection using actual Windows printer name
                var (isNew, existingJob) = _jobTracker.GetOrRegisterJob(jobId, matchedPrinter.Name, auth.ClientId!, docName, contentLength);
                if (!isNew)
                {
                    // Duplicate submission: return existing state without re-spooling!
                    return Results.Ok(new
                    {
                        JobId = jobId,
                        Status = existingJob.State.ToString(),
                        SpoolerJobId = existingJob.SpoolerJobId,
                        IsDuplicate = true,
                        Message = "Job was already submitted; returning current state."
                    });
                }

                // Update state to Spooling
                _jobTracker.UpdateJobState(jobId, JobState.Spooling);

                // Stream request body directly into Windows Print Spooler using exact Windows device name
                var spoolResult = await _printerSpooler.PrintStreamAsync(
                    matchedPrinter.Name,
                    docName,
                    context.Request.Body,
                    dataType,
                    context.RequestAborted);

                if (spoolResult.Success)
                {
                    _jobTracker.UpdateJobState(jobId, JobState.Completed, spoolResult.SpoolerJobId);
                    return Results.Ok(new
                    {
                        JobId = jobId,
                        Status = JobState.Completed.ToString(),
                        SpoolerJobId = spoolResult.SpoolerJobId,
                        BytesWritten = spoolResult.BytesWritten
                    });
                }
                else
                {
                    string errCode = $"PL-SPOOL-{spoolResult.Win32ErrorCode:D4}";
                    _jobTracker.UpdateJobState(jobId, JobState.Failed, spoolerJobId: null, errorCode: errCode, errorMessage: spoolResult.ErrorMessage);
                    return Results.Json(new
                    {
                        JobId = jobId,
                        Status = JobState.Failed.ToString(),
                        ErrorCode = errCode,
                        ErrorMessage = spoolResult.ErrorMessage
                    }, statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    Status = JobState.Failed.ToString(),
                    ErrorCode = "PL-SRV-ERR",
                    ErrorMessage = ex.Message
                }, statusCode: 500);
            }
        });

        // 4. Query job status (reconciliation endpoint for 'Unknown' state)
        app.MapGet("/api/v1/jobs/{id:guid}/status", (Guid id, HttpContext context) =>
        {
            var auth = AuthenticateRequest(context);
            if (!auth.IsAuthenticated)
            {
                return auth.IsRateLimited ? Results.StatusCode(429) : Results.Unauthorized();
            }

            var job = _jobTracker.GetJob(id);
            if (job == null)
            {
                return Results.NotFound(new { Error = "Job not found." });
            }

            return Results.Ok(job);
        });

        _app = app;
        await _app.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null)
        {
            await _app.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private AuthResult AuthenticateRequest(HttpContext context)
    {
        string? authHeader = context.Request.Headers.Authorization;
        string? clientIp = context.Connection.RemoteIpAddress?.ToString();
        return _authenticator.AuthenticateToken(authHeader, clientIp);
    }
}
