using System.Net;
using System.Text.Json;

namespace Cloudie.Core.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>A published GitHub release of the project.</summary>
public sealed record ReleaseInfo(SemanticVersion Version, string Tag, string Name, string Url, string? Notes, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The zip built for a CPU architecture ("x64" or "arm64"), when the release carries one.</summary>
    public ReleaseAsset? AssetFor(string architecture) =>
        Assets.FirstOrDefault(asset => asset.Name.Contains($"-{architecture}", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads the newest GitHub release of the project and compares it with the running version.</summary>
public sealed class UpdateChecker(string repository = UpdateChecker.DefaultRepository, HttpMessageHandler? handler = null) : IDisposable
{
    public const string DefaultRepository = "indown/cloudie";

    private readonly HttpClient _http = new(handler ?? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }, disposeHandler: true)
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public string Repository { get; } = repository;

    /// <summary>The latest non-draft, non-prerelease release; null when the project has none yet.</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.TryAddWithoutValidation("User-Agent", "Cloudie");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        if (!SemanticVersion.TryParse(tag, out var version)) return null;
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                assets.Add(new ReleaseAsset(
                    asset.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                    asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0));
            }
        }
        return new ReleaseInfo(
            version,
            tag,
            root.TryGetProperty("name", out var releaseName) && releaseName.GetString() is { Length: > 0 } n ? n : tag,
            root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null,
            assets);
    }

    /// <summary>The newest release when it is newer than <paramref name="current"/>; null when up to date or unreleased.</summary>
    public async Task<ReleaseInfo?> CheckAsync(SemanticVersion current, CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct).ConfigureAwait(false);
        return latest is not null && latest.Version > current ? latest : null;
    }

    public void Dispose() => _http.Dispose();
}
