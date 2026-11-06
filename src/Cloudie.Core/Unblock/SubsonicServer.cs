using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cloudie.Core.Unblock;

/// <summary>
/// One Navidrome (or any Subsonic-compatible) server the listener added. Settings keep them as a single JSON
/// list in the order they are asked; each can be switched off on its own, under the Navidrome source switch.
/// </summary>
public sealed record SubsonicServer(string Id, string Name, string Url, string User, string Password, bool Enabled = true)
{
    /// <summary>Id of the entry read from the single-server keys of earlier versions.</summary>
    public const string LegacyId = "default";

    /// <summary>The endpoint and credentials; null while the entry is incomplete.</summary>
    [JsonIgnore] public SubsonicConfiguration? Configuration => SubsonicConfiguration.TryCreate(Url, User, Password);

    /// <summary>The name the listener gave, or the host when they gave none.</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var name = Name.Trim();
            if (name.Length > 0) return name;
            var url = Url.Trim();
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : url;
        }
    }

    public static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    /// <summary>The stored list; entries without an id or an address are dropped, unreadable JSON reads as no servers.</summary>
    public static IReadOnlyList<SubsonicServer> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var stored = JsonSerializer.Deserialize<List<Stored>>(json, Json) ?? [];
            return stored
                .Where(static s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Url))
                .Select(static s => new SubsonicServer(s.Id!.Trim(), s.Name ?? "", s.Url!.Trim(), s.User ?? "", s.Password ?? "", s.Enabled ?? true))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string ToJson(IEnumerable<SubsonicServer> servers) => JsonSerializer.Serialize(servers.ToList(), Json);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The on-disk shape, lenient about fields an older or hand-edited file leaves out.</summary>
    private sealed class Stored
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Url { get; init; }
        public string? User { get; init; }
        public string? Password { get; init; }
        public bool? Enabled { get; init; }
    }
}
