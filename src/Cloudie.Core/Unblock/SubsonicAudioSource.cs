using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudie.Core.Netease;

namespace Cloudie.Core.Unblock;

/// <summary>Where a personal Navidrome (or any Subsonic-compatible) server lives.</summary>
public sealed record SubsonicConfiguration(Uri BaseUrl, string User, string Password)
{
    public static SubsonicConfiguration? TryCreate(string? url, string? user, string? password)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password)) return null;
        var trimmed = url.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUrl)) return null;
        if (baseUrl.Scheme is not ("http" or "https")) return null;
        return new SubsonicConfiguration(baseUrl, user, password);
    }

    /// <summary>A Subsonic REST call with token authentication: only md5(password + salt) travels.</summary>
    public Uri Endpoint(string method, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        var token = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(Password + salt)));
        var query = new List<KeyValuePair<string, string>>
        {
            new("u", User), new("t", token), new("s", salt), new("v", "1.16.1"), new("c", "Cloudie"), new("f", "json")
        };
        if (parameters is not null) query.AddRange(parameters.OrderBy(static p => p.Key, StringComparer.Ordinal));
        var builder = new StringBuilder(BaseUrl.ToString().TrimEnd('/'));
        builder.Append("/rest/").Append(method).Append('?');
        builder.Append(string.Join("&", query.Select(static p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")));
        return new Uri(builder.ToString());
    }
}

/// <summary>Resolves gray tracks from a personal Navidrome / Subsonic library.</summary>
public sealed class SubsonicAudioSource(Func<SubsonicConfiguration?> configuration, HttpClient? http = null) : IAudioSource
{
    public const string SourceId = "navidrome";

    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public string Id => SourceId;
    public string DisplayName => "Navidrome";

    public async Task<ResolvedAudio?> ResolveAsync(UnblockTrack track, AudioQuality quality, CancellationToken cancellationToken = default)
    {
        var config = configuration();
        if (config is null) return null;

        var artist = track.Artists.FirstOrDefault() ?? string.Empty;
        var queries = new List<string> { $"{track.Name} {artist}".Trim() };
        if (queries[0] != track.Name) queries.Add(track.Name);

        foreach (var query in queries)
        {
            var url = config.Endpoint("search3.view", new Dictionary<string, string>
            {
                ["query"] = query, ["songCount"] = "20", ["artistCount"] = "0", ["albumCount"] = "0"
            });
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await JsonSerializer.DeserializeAsync<SubsonicEnvelope>(stream, SubsonicJson, cancellationToken).ConfigureAwait(false);
            var body = envelope?.Response ?? throw new InvalidOperationException("Navidrome returned no subsonic-response.");
            if (body.Status != "ok")
            {
                throw new InvalidOperationException($"Navidrome rejected the search: {body.Error?.Message ?? body.Status}");
            }
            var songs = body.SearchResult3?.Song ?? [];
            var candidates = songs.Select(static s => new MatchCandidate(s.Id, s.Title, s.Artist, s.Duration, s.Suffix, s.BitRate));
            if (TrackMatcher.Pick(candidates, track) is { } match)
            {
                var song = songs.First(s => s.Id == match.Id);
                // "format=raw" keeps the server from transcoding, so byte ranges work.
                var streamUrl = config.Endpoint("stream.view", new Dictionary<string, string> { ["id"] = song.Id, ["format"] = "raw" });
                return new ResolvedAudio(Id, DisplayName, streamUrl, song.Suffix, song.BitRate);
            }
        }
        return null;
    }

    private static readonly JsonSerializerOptions SubsonicJson = new() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };

    public sealed class SubsonicSong
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
        [JsonPropertyName("artist")] public string? Artist { get; init; }
        [JsonPropertyName("album")] public string? Album { get; init; }
        [JsonPropertyName("duration")] public int? Duration { get; init; }
        [JsonPropertyName("suffix")] public string? Suffix { get; init; }
        [JsonPropertyName("bitRate")] public int? BitRate { get; init; }
        [JsonPropertyName("size")] public long? Size { get; init; }
    }

    public sealed class SubsonicEnvelope
    {
        [JsonPropertyName("subsonic-response")] public SubsonicResponse? Response { get; init; }
    }

    public sealed class SubsonicResponse
    {
        [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
        [JsonPropertyName("error")] public SubsonicError? Error { get; init; }
        [JsonPropertyName("searchResult3")] public SubsonicSearchResult3? SearchResult3 { get; init; }
    }

    public sealed class SubsonicError
    {
        [JsonPropertyName("code")] public int? Code { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
    }

    public sealed class SubsonicSearchResult3
    {
        [JsonPropertyName("song")] public List<SubsonicSong>? Song { get; init; }
    }
}
