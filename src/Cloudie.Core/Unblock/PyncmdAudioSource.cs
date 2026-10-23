using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudie.Core.Netease;

namespace Cloudie.Core.Unblock;

/// <summary>
/// The GD Studio music API ("pyncmd" in UnblockNeteaseMusic): serves NetEase's own files for
/// songs the account cannot play here, by the NetEase song id. Lossless when asked with br=999.
/// </summary>
public sealed class PyncmdAudioSource(HttpClient? http = null) : IAudioSource
{
    public const string SourceId = "pyncmd";

    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public string Id => SourceId;
    public string DisplayName => "pyncmd";

    public async Task<ResolvedAudio?> ResolveAsync(UnblockTrack track, AudioQuality quality, CancellationToken cancellationToken = default)
    {
        var br = quality.IsLossless() ? 999 : 320;
        var url = $"https://music-api.gdstudio.xyz/api.php?types=url&source=netease&id={track.Id}&br={br}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var reply = await JsonSerializer.DeserializeAsync<Reply>(stream, Json, cancellationToken).ConfigureAwait(false);
        if (reply is null || reply.Br is not > 0 || string.IsNullOrEmpty(reply.Url)) return null;
        var audioUrl = reply.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + reply.Url["http://".Length..]
            : reply.Url;
        return new ResolvedAudio(Id, DisplayName, new Uri(audioUrl), reply.Br == 999 ? "flac" : "mp3", reply.Br);
    }

    private static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    private sealed class Reply
    {
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("br")] public int? Br { get; init; }
        [JsonPropertyName("size")] public long? Size { get; init; }
    }
}
