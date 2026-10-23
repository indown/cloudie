using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Netease;

/// <summary>Typed NetEase Cloud Music API surface, mapped to the real weapi / eapi endpoints.</summary>
public sealed class NeteaseApi(NeteaseClient client)
{
    public NeteaseClient Client { get; } = client;

    private static Dictionary<string, object?> P(params (string Key, object? Value)[] pairs)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return dict;
    }

    private async Task<T> WeapiAsync<T>(string path, Dictionary<string, object?>? payload = null, CancellationToken ct = default) =>
        NeteaseClient.Decode<T>(await Client.WeapiAsync(path, payload, cancellationToken: ct).ConfigureAwait(false));

    private async Task<T> EapiAsync<T>(string path, Dictionary<string, object?>? payload = null, CancellationToken ct = default) =>
        NeteaseClient.Decode<T>(await Client.EapiAsync(path, payload, cancellationToken: ct).ConfigureAwait(false));

    // MARK: Auth

    public async Task<string> GetQrKeyAsync(CancellationToken ct = default) =>
        (await WeapiAsync<QrKeyResponse>("/login/qrcode/unikey", P(("type", 1)), ct).ConfigureAwait(false)).Unikey;

    public static string QrLoginUrl(string unikey) => $"https://music.163.com/login?codekey={unikey}";

    /// <summary>On 803 the auth cookies arrive via Set-Cookie and are absorbed by the client.</summary>
    public async Task<QrCheckResponse> CheckQrAsync(string unikey, CancellationToken ct = default)
    {
        var json = await Client.WeapiAsync("/login/qrcode/client/login", P(("key", unikey), ("type", 1)), cancellationToken: ct)
            .ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Deserialize<QrCheckResponse>(json, NeteaseClient.ResponseOptions)
               ?? throw new NeteaseApiException(-1, "数据加载失败");
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Client.WeapiAsync("/logout", absorbCookies: false, cancellationToken: ct).ConfigureAwait(false);
            NeteaseClient.Decode<CodeOnlyResponse>(json);
        }
        finally
        {
            Client.ClearAuthCookies();
        }
    }

    public async Task RefreshLoginAsync(CancellationToken ct = default)
    {
        try { await Client.WeapiAsync("/login/token/refresh", cancellationToken: ct).ConfigureAwait(false); }
        catch (NeteaseApiException) { }
        catch (HttpRequestException) { }
    }

    public async Task<UserProfile?> GetAccountAsync(CancellationToken ct = default) =>
        (await WeapiAsync<AccountResponse>("/w/nuser/account/get", ct: ct).ConfigureAwait(false)).Profile;

    // MARK: Library

    public async Task<IReadOnlyList<PlaylistSummary>> GetUserPlaylistsAsync(long uid, int limit = 2000, int offset = 0, CancellationToken ct = default) =>
        (await WeapiAsync<UserPlaylistsResponse>("/user/playlist",
            P(("uid", uid), ("limit", limit), ("offset", offset), ("includeVideo", true)), ct).ConfigureAwait(false)).Playlist;

    public async Task<IReadOnlyList<long>> GetLikedTrackIdsAsync(long uid, CancellationToken ct = default) =>
        (await WeapiAsync<LikeListResponse>("/song/like/get", P(("uid", uid)), ct).ConfigureAwait(false)).Ids;

    public async Task LikeTrackAsync(long id, bool like, CancellationToken ct = default)
    {
        var response = await WeapiAsync<CodeOnlyResponse>($"/radio/like?alg=itembased&trackId={id}&time=3",
            P(("trackId", id), ("like", like)), ct).ConfigureAwait(false);
        if (response.Code != 200) throw new NeteaseApiException(response.Code, "操作失败，专辑下架或版权锁定");
    }

    // MARK: Discovery

    public async Task<IReadOnlyList<PlaylistSummary>> GetPersonalizedPlaylistsAsync(int limit = 30, CancellationToken ct = default) =>
        (await WeapiAsync<PersonalizedPlaylistsResponse>("/personalized/playlist",
            P(("limit", limit), ("total", true), ("n", 1000)), ct).ConfigureAwait(false)).Result;

    /// <summary>Logged-in daily recommended playlists.</summary>
    public async Task<IReadOnlyList<PlaylistSummary>> GetRecommendResourceAsync(CancellationToken ct = default) =>
        (await WeapiAsync<RecommendResourceResponse>("/v1/discovery/recommend/resource", ct: ct).ConfigureAwait(false)).Recommend;

    public async Task<IReadOnlyList<Track>> GetDailyRecommendSongsAsync(CancellationToken ct = default) =>
        (await WeapiAsync<RecommendSongsResponse>("/v3/discovery/recommend/songs", ct: ct).ConfigureAwait(false)).Data?.DailySongs ?? [];

    public async Task<IReadOnlyList<ToplistItem>> GetToplistsAsync(CancellationToken ct = default) =>
        (await EapiAsync<ToplistResponse>("/toplist", ct: ct).ConfigureAwait(false)).List;

    public Task<TopPlaylistsResponse> GetTopPlaylistsAsync(string category = "全部", string order = "hot", int limit = 50, int offset = 0, CancellationToken ct = default) =>
        WeapiAsync<TopPlaylistsResponse>("/playlist/list",
            P(("cat", category), ("order", order), ("limit", limit), ("offset", offset), ("total", true)), ct);

    // MARK: Playlists, albums, artists

    public Task<PlaylistDetailResponse> GetPlaylistDetailAsync(long id, CancellationToken ct = default) =>
        WeapiAsync<PlaylistDetailResponse>("/v6/playlist/detail", P(("id", id), ("n", 100_000), ("s", 8)), ct);

    public async Task<SongDetailResponse> GetSongDetailsAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var list = ids.ToList();
        if (list.Count == 0) return new SongDetailResponse();
        var c = "[" + string.Join(",", list.Select(static id => $"{{\"id\":{id}}}")) + "]";
        return await WeapiAsync<SongDetailResponse>("/v3/song/detail", P(("c", c)), ct).ConfigureAwait(false);
    }

    public Task<AlbumResponse> GetAlbumAsync(long id, CancellationToken ct = default) =>
        WeapiAsync<AlbumResponse>($"/v1/album/{id}", ct: ct);

    public Task<ArtistResponse> GetArtistAsync(long id, CancellationToken ct = default) =>
        WeapiAsync<ArtistResponse>($"/v1/artist/{id}", ct: ct);

    public Task<ArtistAlbumsResponse> GetArtistAlbumsAsync(long id, int limit = 50, int offset = 0, CancellationToken ct = default) =>
        WeapiAsync<ArtistAlbumsResponse>($"/artist/albums/{id}", P(("limit", limit), ("offset", offset), ("total", true)), ct);

    // MARK: Tracks

    public async Task<IReadOnlyList<SongUrlData>> GetSongUrlsAsync(IEnumerable<long> ids, AudioQuality quality, CancellationToken ct = default)
    {
        var level = quality.ToLevel();
        var payload = P(("ids", "[" + string.Join(",", ids) + "]"), ("level", level), ("encodeType", "flac"));
        if (level == "sky") payload["immerseType"] = "c51";
        return (await EapiAsync<SongUrlResponse>("/song/enhance/player/url/v1", payload, ct).ConfigureAwait(false)).Data;
    }

    public async Task<SongUrlData?> GetSongUrlAsync(long id, AudioQuality quality, CancellationToken ct = default) =>
        (await GetSongUrlsAsync([id], quality, ct).ConfigureAwait(false)).FirstOrDefault(d => d.Id == id);

    /// <summary><c>/song/lyric/v1</c> also returns verbatim (word-by-word) lyrics; falls back to the classic endpoint.</summary>
    public async Task<LyricResponse> GetLyricAsync(long id, CancellationToken ct = default)
    {
        try
        {
            var v1 = await WeapiAsync<LyricResponse>("/song/lyric/v1",
                P(("id", id), ("cp", false), ("lv", 0), ("kv", 0), ("tv", 0), ("rv", 0), ("yv", 0), ("ytv", 0), ("yrv", 0)), ct)
                .ConfigureAwait(false);
            if (v1.HasAnyLyric) return v1;
        }
        catch (NeteaseApiException) { }
        return await WeapiAsync<LyricResponse>("/song/lyric", P(("id", id), ("lv", -1), ("kv", -1), ("tv", -1), ("rv", -1)), ct)
            .ConfigureAwait(false);
    }

    // MARK: Search

    public async Task<SearchResult> SearchAsync(string keywords, SearchType type = SearchType.Song, int limit = 30, int offset = 0, CancellationToken ct = default) =>
        (await EapiAsync<SearchResponse>("/cloudsearch/pc",
            P(("s", keywords), ("type", (int)type), ("limit", limit), ("offset", offset), ("total", true)), ct).ConfigureAwait(false)).Result
        ?? new SearchResult();

    /// <summary>Keyword completions for the search box (<c>search/suggest/keyword</c>), in NetEase's order.</summary>
    public async Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string keywords, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return [];
        var response = await WeapiAsync<SearchSuggestResponse>("/search/suggest/keyword", P(("s", keywords.Trim())), ct).ConfigureAwait(false);
        return response.Result?.AllMatch?
            .Select(static match => match.Keyword)
            .Where(static keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct()
            .ToList() ?? [];
    }

    // MARK: Personal FM

    /// <summary>The next few songs of 私人 FM; NetEase hands out about three per call.</summary>
    public async Task<IReadOnlyList<Track>> GetPersonalFmAsync(CancellationToken ct = default) =>
        (await WeapiAsync<PersonalFmResponse>("/v1/radio/get", ct: ct).ConfigureAwait(false)).Data ?? [];

    /// <summary>"不喜欢" in 私人 FM: the song leaves the station and the taste model learns from it.</summary>
    public async Task TrashFmTrackAsync(long id, CancellationToken ct = default) =>
        await WeapiAsync<CodeOnlyResponse>($"/radio/trash/add?alg=RT&songId={id}&time=25", P(("songId", id)), ct).ConfigureAwait(false);

    // MARK: Playlist management

    public async Task SubscribePlaylistAsync(long id, bool subscribe, CancellationToken ct = default) =>
        await WeapiAsync<CodeOnlyResponse>(subscribe ? "/playlist/subscribe" : "/playlist/unsubscribe", P(("id", id)), ct).ConfigureAwait(false);

    /// <summary>
    /// Adds or removes songs in one of the account's own playlists (<c>playlist/manipulate/tracks</c>).
    /// NetEase answers 502 when every song is already in the playlist.
    /// </summary>
    public async Task ManipulatePlaylistTracksAsync(long playlistId, IEnumerable<long> trackIds, bool add, CancellationToken ct = default)
    {
        var ids = "[" + string.Join(",", trackIds) + "]";
        await WeapiAsync<CodeOnlyResponse>("/playlist/manipulate/tracks",
            P(("op", add ? "add" : "del"), ("pid", playlistId), ("trackIds", ids), ("imme", "true")), ct).ConfigureAwait(false);
    }

    public async Task<PlaylistSummary?> CreatePlaylistAsync(string name, bool isPrivate = false, CancellationToken ct = default) =>
        (await WeapiAsync<CreatePlaylistResponse>("/playlist/create",
            P(("name", name), ("privacy", isPrivate ? 10 : 0), ("type", "NORMAL")), ct).ConfigureAwait(false)).Playlist;

    // MARK: Listening history

    /// <summary>
    /// Reports a play (<c>feedback/weblog</c>) so it counts toward listening history, play counts and
    /// recommendations, like the official clients do. <paramref name="seconds"/> is how long the song was
    /// heard; <paramref name="sourceId"/> is the playlist or album it was played from, 0 when unknown.
    /// </summary>
    public async Task ScrobbleAsync(long trackId, int seconds, long sourceId = 0, CancellationToken ct = default)
    {
        var play = new Dictionary<string, object?>
        {
            ["download"] = 0,
            ["end"] = "playend",
            ["id"] = trackId,
            ["time"] = Math.Max(0, seconds),
            ["type"] = "song",
            ["wifi"] = 0,
            ["source"] = "list",
            ["mainsite"] = 1,
            ["content"] = string.Empty
        };
        if (sourceId > 0) play["sourceId"] = sourceId.ToString();
        var logs = System.Text.Json.JsonSerializer.Serialize(new[] { new Dictionary<string, object?> { ["action"] = "play", ["json"] = play } });
        await WeapiAsync<CodeOnlyResponse>("/feedback/weblog", P(("logs", logs)), ct).ConfigureAwait(false);
    }
}
