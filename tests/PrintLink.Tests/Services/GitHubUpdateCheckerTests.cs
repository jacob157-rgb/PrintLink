using System.Net;
using System.Net.Http;
using System.Text;
using PrintLink.Core.Services;
using Xunit;

namespace PrintLink.Tests.Services;

public class GitHubUpdateCheckerTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_NewerVersionAvailable_ReturnsReleaseInfoWithIsNewerTrue()
    {
        string fakeJson = """
        {
            "tag_name": "v1.1.0",
            "name": "PrintLink v1.1.0 Release",
            "body": "Fixed scaling issues and added OTA updates.",
            "html_url": "https://github.com/jacob157-rgb/PrintLink/releases/tag/v1.1.0",
            "assets": [
                {
                    "name": "PrintLink-Setup-v1.1.0.exe",
                    "browser_download_url": "https://github.com/jacob157-rgb/PrintLink/releases/download/v1.1.0/PrintLink-Setup-v1.1.0.exe",
                    "size": 204800000
                }
            ]
        }
        """;

        var client = new HttpClient(new MockHttpMessageHandler(fakeJson));
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckForUpdateAsync("1.0.0");

        Assert.NotNull(result);
        Assert.Equal("v1.1.0", result.TagName);
        Assert.True(result.IsNewerVersion);
        Assert.Equal("PrintLink-Setup-v1.1.0.exe", result.AssetName);
        Assert.Equal("https://github.com/jacob157-rgb/PrintLink/releases/download/v1.1.0/PrintLink-Setup-v1.1.0.exe", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SameOrOlderVersion_ReturnsIsNewerFalse()
    {
        string fakeJson = """
        {
            "tag_name": "v1.0.0",
            "name": "PrintLink v1.0.0 Initial Release",
            "body": "First enterprise release.",
            "html_url": "https://github.com/jacob157-rgb/PrintLink/releases/tag/v1.0.0",
            "assets": []
        }
        """;

        var client = new HttpClient(new MockHttpMessageHandler(fakeJson));
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckForUpdateAsync("1.0.0");

        Assert.NotNull(result);
        Assert.Equal("v1.0.0", result.TagName);
        Assert.False(result.IsNewerVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ApiNotFound_ReturnsNull()
    {
        var client = new HttpClient(new MockHttpMessageHandler("Not Found", HttpStatusCode.NotFound));
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckForUpdateAsync("1.0.0");

        Assert.Null(result);
    }
}
