using System.Net;
using Cloudie.Core.Updates;

namespace Cloudie.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2.3-beta.1+abc123", 1, 2, 3, "beta.1")]
    [InlineData("0.1.0-ci.12+89ba1b8", 0, 1, 0, "ci.12")]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("1.2.3.4", 1, 2, 3, null)]
    public void Versions_parse(string text, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(new SemanticVersion(major, minor, patch, prerelease), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.x")]
    [InlineData("1.2.3.4.5")]
    public void Garbage_does_not_parse(string text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void Versions_order_like_semver()
    {
        var ordered = new[] { "0.1.0-ci.9", "0.1.0-ci.12", "0.1.0-rc.1", "0.1.0", "0.1.1", "0.2.0-alpha", "0.2.0", "1.0.0" }
            .Select(SemanticVersion.Parse).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i - 1] < ordered[i], $"{ordered[i - 1]} should sort before {ordered[i]}");
            Assert.True(ordered[i] > ordered[i - 1]);
        }
        Assert.Equal(0, SemanticVersion.Parse("v1.0.0+build1").CompareTo(SemanticVersion.Parse("1.0.0+build2")));
        Assert.Equal("0.1.0-ci.12", SemanticVersion.Parse("0.1.0-ci.12+89ba1b8").ToString());
    }

    private const string ReleaseJson = """
        {"tag_name":"v0.2.0","name":"Cloudie 0.2.0","html_url":"https://github.com/indown/cloudie/releases/tag/v0.2.0","body":"notes",
         "assets":[{"name":"Cloudie-win-x64.zip","browser_download_url":"https://example.test/x64.zip","size":10},
                   {"name":"Cloudie-win-arm64.zip","browser_download_url":"https://example.test/arm64.zip","size":11}]}
        """;

    [Fact]
    public async Task A_newer_release_is_reported_with_the_asset_for_each_architecture()
    {
        var handler = StubHandler.Json(ReleaseJson);
        using var checker = new UpdateChecker("indown/cloudie", handler);

        var release = await checker.CheckAsync(SemanticVersion.Parse("0.1.0-ci.12+89ba1b8"));

        Assert.NotNull(release);
        Assert.Equal("v0.2.0", release.Tag);
        Assert.Equal(new SemanticVersion(0, 2, 0), release.Version);
        Assert.Equal("https://github.com/indown/cloudie/releases/tag/v0.2.0", release.Url);
        Assert.Equal("https://example.test/arm64.zip", release.AssetFor("arm64")!.DownloadUrl);
        Assert.Equal("https://example.test/x64.zip", release.AssetFor("x64")!.DownloadUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/indown/cloudie/releases/latest", request.RequestUri!.ToString());
        Assert.True(request.Headers.Contains("User-Agent"));
    }

    [Fact]
    public async Task An_equal_or_newer_build_is_not_offered_an_update()
    {
        using var same = new UpdateChecker("indown/cloudie", StubHandler.Json(ReleaseJson));
        Assert.Null(await same.CheckAsync(SemanticVersion.Parse("0.2.0")));
        using var newer = new UpdateChecker("indown/cloudie", StubHandler.Json(ReleaseJson));
        Assert.Null(await newer.CheckAsync(SemanticVersion.Parse("0.3.0-ci.1")));
    }

    [Fact]
    public async Task No_release_yet_means_no_update()
    {
        using var checker = new UpdateChecker("indown/cloudie", StubHandler.Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound));
        Assert.Null(await checker.CheckAsync(SemanticVersion.Parse("0.1.0")));
    }
}
