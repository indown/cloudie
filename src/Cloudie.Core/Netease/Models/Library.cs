using System.Text.Json.Serialization;

namespace Cloudie.Core.Netease.Models;

public sealed class UserProfile
{
    [JsonPropertyName("userId")] public long UserId { get; init; }
    [JsonPropertyName("nickname")] public string Nickname { get; init; } = string.Empty;
    [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("backgroundUrl")] public string? BackgroundUrl { get; init; }
    [JsonPropertyName("signature")] public string? Signature { get; init; }
    [JsonPropertyName("vipType")] public int VipType { get; init; }
}

public sealed class PlaylistCreator
{
    [JsonPropertyName("userId")] public long UserId { get; init; }
    [JsonPropertyName("nickname")] public string Nickname { get; init; } = string.Empty;
    [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; init; }
}

/// <summary>A playlist card: the "my playlists" rows, recommendations and search results.</summary>
public sealed class PlaylistSummary
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("picUrl")] public string? PicUrl { get; init; }
    [JsonPropertyName("coverImgUrl")] public string? CoverImgUrl { get; init; }
    /// <summary>Some endpoints spell it <c>playcount</c>; matching is case-insensitive.</summary>
    [JsonPropertyName("playCount")] public long? PlayCount { get; init; }
    [JsonPropertyName("trackCount")] public int TrackCount { get; init; }
    [JsonPropertyName("updateTime")] public long? UpdateTime { get; init; }
    /// <summary>When the songs last changed; with <see cref="TrackCount"/>, the cheap test of whether a cached detail is current.</summary>
    [JsonPropertyName("trackUpdateTime")] public long TrackUpdateTime { get; init; }
    [JsonPropertyName("copywriter")] public string? Copywriter { get; init; }
    [JsonPropertyName("creator")] public PlaylistCreator? Creator { get; init; }
    [JsonPropertyName("specialType")] public int SpecialType { get; init; }
    [JsonPropertyName("privacy")] public int Privacy { get; init; }
    [JsonPropertyName("subscribed")] public bool Subscribed { get; init; }

    [JsonIgnore] public string? CoverUrl => CoverImgUrl ?? PicUrl;
    [JsonIgnore] public long Plays => PlayCount ?? 0;
    /// <summary>The account's own "我喜欢的音乐" list (specialType 5).</summary>
    [JsonIgnore] public bool IsFavorites => SpecialType == 5;
}

public sealed class TrackIdRef
{
    [JsonPropertyName("id")] public long Id { get; init; }
}

public sealed class PlaylistDetail
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("coverImgUrl")] public string? CoverImgUrl { get; init; }
    [JsonPropertyName("creator")] public PlaylistCreator? Creator { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("trackCount")] public int TrackCount { get; set; }
    [JsonPropertyName("playCount")] public long PlayCount { get; init; }
    [JsonPropertyName("subscribedCount")] public long SubscribedCount { get; init; }
    [JsonPropertyName("subscribed")] public bool Subscribed { get; set; }
    [JsonPropertyName("trackIds")] public List<TrackIdRef> TrackIds { get; set; } = [];
    [JsonPropertyName("tracks")] public List<Track> Tracks { get; set; } = [];
    [JsonPropertyName("specialType")] public int SpecialType { get; init; }
    [JsonPropertyName("updateTime")] public long UpdateTime { get; init; }
    [JsonPropertyName("trackUpdateTime")] public long TrackUpdateTime { get; init; }
}

public sealed class AlbumSummary
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("picUrl")] public string? PicUrl { get; init; }
    [JsonPropertyName("artist")] public ArtistRef? Artist { get; init; }
    [JsonPropertyName("artists")] public List<ArtistRef>? Artists { get; init; }
    [JsonPropertyName("publishTime")] public long PublishTime { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("company")] public string? Company { get; init; }

    [JsonIgnore] public string ArtistName => Artist?.Name ?? Artists?.FirstOrDefault()?.Name ?? string.Empty;
    [JsonIgnore] public DateTimeOffset? PublishDate => PublishTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(PublishTime) : null;
    [JsonIgnore] public string PublishYear => PublishDate?.Year.ToString() ?? string.Empty;
}

public sealed class ArtistSummary
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("picUrl")] public string? PicUrl { get; init; }
    [JsonPropertyName("img1v1Url")] public string? Img1v1Url { get; init; }
    [JsonPropertyName("cover")] public string? Cover { get; init; }
    [JsonPropertyName("albumSize")] public int AlbumSize { get; init; }
    [JsonPropertyName("musicSize")] public int MusicSize { get; init; }
    [JsonPropertyName("briefDesc")] public string? BriefDesc { get; init; }
    [JsonPropertyName("alias")] public List<string>? Alias { get; init; }

    [JsonIgnore] public string? ImageUrl => Cover ?? PicUrl ?? Img1v1Url;
}

public sealed class SongUrlData
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("br")] public int Br { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("level")] public string? Level { get; init; }
    [JsonPropertyName("fee")] public int Fee { get; init; }
    [JsonPropertyName("freeTrialInfo")] public FreeTrialInfo? FreeTrialInfo { get; init; }
    [JsonPropertyName("time")] public int Time { get; init; }
    [JsonPropertyName("code")] public int? Code { get; init; }
    [JsonPropertyName("md5")] public string? Md5 { get; init; }

    [JsonIgnore] public bool IsTrial => FreeTrialInfo is not null;
}

public sealed class FreeTrialInfo
{
    [JsonPropertyName("start")] public int? Start { get; init; }
    [JsonPropertyName("end")] public int? End { get; init; }
}

public sealed class LyricBody
{
    [JsonPropertyName("lyric")] public string? Lyric { get; init; }
}

public sealed class LyricContributor
{
    [JsonPropertyName("nickname")] public string? Nickname { get; init; }
}

public sealed class LyricResponse
{
    [JsonPropertyName("lrc")] public LyricBody? Lrc { get; init; }
    [JsonPropertyName("tlyric")] public LyricBody? Tlyric { get; init; }
    [JsonPropertyName("romalrc")] public LyricBody? Romalrc { get; init; }
    /// <summary>Verbatim (word-by-word) lyrics; only on <c>/song/lyric/v1</c>.</summary>
    [JsonPropertyName("yrc")] public LyricBody? Yrc { get; init; }
    [JsonPropertyName("ytlrc")] public LyricBody? Ytlrc { get; init; }
    [JsonPropertyName("yromalrc")] public LyricBody? Yromalrc { get; init; }
    [JsonPropertyName("lyricUser")] public LyricContributor? LyricUser { get; init; }
    [JsonPropertyName("transUser")] public LyricContributor? TransUser { get; init; }
    [JsonPropertyName("nolyric")] public bool? NoLyric { get; init; }
    [JsonPropertyName("uncollected")] public bool? Uncollected { get; init; }

    [JsonIgnore] public bool HasAnyLyric => !string.IsNullOrEmpty(Lrc?.Lyric) || !string.IsNullOrEmpty(Yrc?.Lyric);
}

public enum SearchType
{
    Song = 1,
    Album = 10,
    Artist = 100,
    Playlist = 1000
}

public sealed class SearchResult
{
    [JsonPropertyName("songs")] public List<Track>? Songs { get; init; }
    [JsonPropertyName("albums")] public List<AlbumSummary>? Albums { get; init; }
    [JsonPropertyName("artists")] public List<ArtistSummary>? Artists { get; init; }
    [JsonPropertyName("playlists")] public List<PlaylistSummary>? Playlists { get; init; }
    [JsonPropertyName("songCount")] public int? SongCount { get; init; }
    [JsonPropertyName("albumCount")] public int? AlbumCount { get; init; }
    [JsonPropertyName("artistCount")] public int? ArtistCount { get; init; }
    [JsonPropertyName("playlistCount")] public int? PlaylistCount { get; init; }
}

public sealed class ToplistItem
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("coverImgUrl")] public string? CoverImgUrl { get; init; }
    [JsonPropertyName("updateFrequency")] public string? UpdateFrequency { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
}
