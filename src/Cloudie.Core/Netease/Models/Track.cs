using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cloudie.Core.Netease.Models;

public sealed class ArtistRef
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
}

public sealed class AlbumRef
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("picUrl")] public string? PicUrl { get; init; }
}

/// <summary>
/// A song as NetEase returns it. Decodes both the "v3" shape (<c>ar</c>/<c>al</c>/<c>dt</c>)
/// and the legacy shape (<c>artists</c>/<c>album</c>/<c>duration</c>).
/// </summary>
public sealed class Track
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("ar")] public List<ArtistRef>? Ar { get; init; }
    [JsonPropertyName("artists")] public List<ArtistRef>? ArtistsLegacy { get; init; }
    [JsonPropertyName("al")] public AlbumRef? Al { get; init; }
    [JsonPropertyName("album")] public AlbumRef? AlbumLegacy { get; init; }
    [JsonPropertyName("dt")] public int? Dt { get; init; }
    [JsonPropertyName("duration")] public int? DurationLegacy { get; init; }
    [JsonPropertyName("alia")] public List<string>? Alia { get; init; }
    [JsonPropertyName("alias")] public List<string>? AliasLegacy { get; init; }
    [JsonPropertyName("tns")] public List<string>? Tns { get; init; }
    [JsonPropertyName("fee")] public int Fee { get; init; }
    [JsonPropertyName("mv")] public long MvId { get; init; }
    [JsonPropertyName("no")] public int TrackNo { get; init; }
    [JsonPropertyName("cd")] public string? Disc { get; init; }
    [JsonPropertyName("noCopyrightRcmd")] public JsonElement? NoCopyrightRcmd { get; init; }
    [JsonPropertyName("pc")] public JsonElement? Pc { get; init; }
    [JsonPropertyName("privilege")] public TrackPrivilege? EmbeddedPrivilege { get; init; }

    [JsonIgnore] public IReadOnlyList<ArtistRef> Artists => Ar ?? ArtistsLegacy ?? [];
    [JsonIgnore] public AlbumRef Album => Al ?? AlbumLegacy ?? new AlbumRef();
    [JsonIgnore] public int DurationMs => Dt ?? DurationLegacy ?? 0;
    [JsonIgnore] public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);
    [JsonIgnore] public IReadOnlyList<string> Alias => Alia ?? AliasLegacy ?? [];
    [JsonIgnore] public IReadOnlyList<string> TransNames => Tns ?? [];
    [JsonIgnore] public string ArtistNames => string.Join(" / ", Artists.Select(static a => a.Name));
    [JsonIgnore] public string? Subtitle => TransNames.FirstOrDefault() ?? Alias.FirstOrDefault();
    [JsonIgnore] public bool NoCopyright => NoCopyrightRcmd is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };
    [JsonIgnore] public bool IsCloud => Pc is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };

    /// <summary>Mirrors YesPlayMusic's decision chain, with the VIP check covering 黑胶 SVIP.</summary>
    public TrackPlayability GetPlayability(TrackPrivilege? privilege, bool isLoggedIn, int vipType)
    {
        privilege ??= EmbeddedPrivilege;
        if (privilege?.Pl is > 0) return TrackPlayability.Playable;
        if (isLoggedIn && privilege?.Cs == true) return TrackPlayability.Playable;
        var effectiveFee = privilege?.Fee ?? Fee;
        if (effectiveFee == 1) return vipType > 0 ? TrackPlayability.Playable : TrackPlayability.VipOnly;
        if (effectiveFee == 4) return TrackPlayability.PaidAlbum;
        if (NoCopyright) return TrackPlayability.NoCopyright;
        if (privilege?.St is < 0 && isLoggedIn) return TrackPlayability.Delisted;
        return TrackPlayability.Playable;
    }
}

/// <summary>Playability flags per track, returned in parallel <c>privileges</c> arrays.</summary>
public sealed class TrackPrivilege
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("fee")] public int? Fee { get; init; }
    [JsonPropertyName("pl")] public int? Pl { get; init; }
    [JsonPropertyName("st")] public int? St { get; init; }
    [JsonPropertyName("cs")] public bool? Cs { get; init; }
    [JsonPropertyName("maxbr")] public int? MaxBr { get; init; }

    /// <summary>Unavailable here (st &lt; 0) or not playable at any bit rate.</summary>
    [JsonIgnore] public bool IsGray => St is < 0 || Pl is 0;
}

public enum TrackPlayability
{
    Playable,
    VipOnly,
    PaidAlbum,
    NoCopyright,
    Delisted
}

public static class TrackPlayabilityExtensions
{
    public static string? Reason(this TrackPlayability playability) => playability switch
    {
        TrackPlayability.VipOnly => "VIP 专属",
        TrackPlayability.PaidAlbum => "付费专辑",
        TrackPlayability.NoCopyright => "无版权",
        TrackPlayability.Delisted => "已下架",
        _ => null
    };
}
