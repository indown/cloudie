using System.Text.Json.Serialization;

namespace Cloudie.Core.Netease.Models;

/// <summary>Replies whose only interesting field is the business code.</summary>
public sealed class CodeOnlyResponse
{
    [JsonPropertyName("code")] public int Code { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}

public sealed class QrKeyResponse
{
    [JsonPropertyName("code")] public int Code { get; init; }
    [JsonPropertyName("unikey")] public string Unikey { get; init; } = string.Empty;
}

/// <summary>Codes: 800 expired · 801 waiting · 802 scanned · 803 success.</summary>
public sealed class QrCheckResponse
{
    [JsonPropertyName("code")] public int Code { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("nickname")] public string? Nickname { get; init; }
    [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; init; }

    [JsonIgnore] public QrLoginState State => Code switch
    {
        800 => QrLoginState.Expired,
        801 => QrLoginState.Waiting,
        802 => QrLoginState.Scanned,
        803 => QrLoginState.Confirmed,
        _ => QrLoginState.Unknown
    };
}

public enum QrLoginState { Unknown, Expired, Waiting, Scanned, Confirmed }

public sealed class AccountResponse
{
    [JsonPropertyName("code")] public int Code { get; init; }
    [JsonPropertyName("profile")] public UserProfile? Profile { get; init; }
}

public sealed class UserPlaylistsResponse
{
    [JsonPropertyName("playlist")] public List<PlaylistSummary> Playlist { get; init; } = [];
    [JsonPropertyName("more")] public bool? More { get; init; }
}

public sealed class LikeListResponse
{
    [JsonPropertyName("ids")] public List<long> Ids { get; init; } = [];
}

public sealed class PersonalizedPlaylistsResponse
{
    [JsonPropertyName("result")] public List<PlaylistSummary> Result { get; init; } = [];
}

public sealed class RecommendResourceResponse
{
    [JsonPropertyName("recommend")] public List<PlaylistSummary> Recommend { get; init; } = [];
}

public sealed class RecommendSongsResponse
{
    public sealed class Body
    {
        [JsonPropertyName("dailySongs")] public List<Track>? DailySongs { get; init; }
    }

    [JsonPropertyName("data")] public Body? Data { get; init; }
}

public sealed class PlaylistDetailResponse
{
    [JsonPropertyName("playlist")] public PlaylistDetail Playlist { get; init; } = new();
    [JsonPropertyName("privileges")] public List<TrackPrivilege>? Privileges { get; init; }
}

public sealed class SongDetailResponse
{
    [JsonPropertyName("songs")] public List<Track> Songs { get; init; } = [];
    [JsonPropertyName("privileges")] public List<TrackPrivilege>? Privileges { get; init; }
}

public sealed class SongUrlResponse
{
    [JsonPropertyName("data")] public List<SongUrlData> Data { get; init; } = [];
}

public sealed class SearchResponse
{
    [JsonPropertyName("result")] public SearchResult? Result { get; init; }
}

public sealed class AlbumResponse
{
    [JsonPropertyName("album")] public AlbumSummary? Album { get; init; }
    [JsonPropertyName("songs")] public List<Track> Songs { get; init; } = [];
}

public sealed class ArtistResponse
{
    [JsonPropertyName("artist")] public ArtistSummary? Artist { get; init; }
    [JsonPropertyName("hotSongs")] public List<Track> HotSongs { get; init; } = [];
}

public sealed class ArtistAlbumsResponse
{
    [JsonPropertyName("hotAlbums")] public List<AlbumSummary> HotAlbums { get; init; } = [];
    [JsonPropertyName("more")] public bool? More { get; init; }
}

public sealed class ToplistResponse
{
    [JsonPropertyName("list")] public List<ToplistItem> List { get; init; } = [];
}

public sealed class TopPlaylistsResponse
{
    [JsonPropertyName("playlists")] public List<PlaylistSummary> Playlists { get; init; } = [];
    [JsonPropertyName("total")] public int? Total { get; init; }
    [JsonPropertyName("more")] public bool? More { get; init; }
}

public sealed class SearchSuggestResponse
{
    public sealed class Body
    {
        [JsonPropertyName("allMatch")] public List<SuggestKeyword>? AllMatch { get; init; }
    }

    [JsonPropertyName("result")] public Body? Result { get; init; }
}

public sealed class SuggestKeyword
{
    [JsonPropertyName("keyword")] public string Keyword { get; init; } = string.Empty;
    [JsonPropertyName("type")] public int Type { get; init; }
}

public sealed class PersonalFmResponse
{
    [JsonPropertyName("data")] public List<Track>? Data { get; init; }
}

public sealed class CreatePlaylistResponse
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("playlist")] public PlaylistSummary? Playlist { get; init; }
}

public sealed class IntelligenceListResponse
{
    [JsonPropertyName("data")] public List<IntelligenceItem>? Data { get; init; }
}

public sealed class IntelligenceItem
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("songInfo")] public Track? SongInfo { get; init; }
    [JsonPropertyName("recommended")] public bool? Recommended { get; init; }
    [JsonPropertyName("alg")] public string? Alg { get; init; }
}
