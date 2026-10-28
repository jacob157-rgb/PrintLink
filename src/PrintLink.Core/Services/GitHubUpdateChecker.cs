using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PrintLink.Core.Services;

public record GitHubReleaseInfo(
    string TagName,
    string Name,
    string Body,
    string HtmlUrl,
    string? DownloadUrl,
    string? AssetName,
    long AssetSize,
    bool IsNewerVersion);

public sealed class GitHubUpdateChecker
{
    private const string GitHubApiUrl = "https://api.github.com/repos/jacob157-rgb/PrintLink/releases/latest";
    private readonly HttpClient _httpClient;

    public GitHubUpdateChecker(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "LNet-PrintLink-AutoUpdater/1.0");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        }
    }

    /// <summary>
    /// Checks GitHub repository for newer releases.
    /// </summary>
    public async Task<GitHubReleaseInfo?> CheckForUpdateAsync(string currentVersion = "1.0.0", CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(GitHubApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            string name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? tagName : tagName;
            string body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";

            string? downloadUrl = null;
            string? assetName = null;
            long assetSize = 0;

            if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    string aName = asset.TryGetProperty("name", out var aNameEl) ? aNameEl.GetString() ?? "" : "";
                    if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetName = aName;
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var dlEl) ? dlEl.GetString() : null;
                        assetSize = asset.TryGetProperty("size", out var sEl) ? sEl.GetInt64() : 0;
                        break;
                    }
                }
            }

            bool isNewer = IsVersionGreater(tagName, currentVersion);

            return new GitHubReleaseInfo(
                TagName: tagName,
                Name: name,
                Body: body,
                HtmlUrl: htmlUrl,
                DownloadUrl: downloadUrl,
                AssetName: assetName,
                AssetSize: assetSize,
                IsNewerVersion: isNewer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the update installer with progress reporting.
    /// </summary>
    public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"PrintLink-Setup-Update-{Guid.NewGuid():N}.exe");

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                double percentage = (double)totalRead / totalBytes * 100.0;
                progress.Report(percentage);
            }
        }

        return tempFile;
    }

    /// <summary>
    /// Executes the downloaded installer and terminates the current application process so the installer can replace files.
    /// </summary>
    public static void LaunchInstallerAndExit(string installerPath)
    {
        if (!File.Exists(installerPath)) return;

        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        };

        Process.Start(psi);
        Environment.Exit(0);
    }

    private static bool IsVersionGreater(string remoteTag, string currentVersion)
    {
        string cleanRemote = remoteTag.TrimStart('v', 'V').Split('-')[0];
        string cleanCurrent = currentVersion.TrimStart('v', 'V').Split('-')[0];

        if (Version.TryParse(cleanRemote, out var vRemote) && Version.TryParse(cleanCurrent, out var vCurrent))
        {
            return vRemote > vCurrent;
        }

        return string.Compare(cleanRemote, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
