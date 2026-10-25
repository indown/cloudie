using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Library;

/// <summary>
/// The home page as last shown, so it can be painted before the network answers: 每日推荐歌单, the charts and the
/// first 每日推荐 songs. <see cref="Day"/> is the recommendation day they belong to and <see cref="UserId"/> the
/// account (0 when signed out). NetEase renews the daily picks once a day, so the same day and account mean
/// there is nothing to fetch; this differs from a playlist, which the listener can change at any moment.
/// </summary>
public sealed record HomeSnapshot(
    long UserId,
    DateOnly Day,
    IReadOnlyList<PlaylistSummary> RecommendedPlaylists,
    IReadOnlyList<ToplistItem> Toplists,
    IReadOnlyList<Track> DailySongs,
    DateTimeOffset SavedAt)
{
    /// <summary>The daily picks turn over at 06:00 Beijing time (UTC+8), wherever the listener is.</summary>
    public static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    public static readonly TimeSpan RenewalHour = TimeSpan.FromHours(6);

    /// <summary>The day whose picks are current at <paramref name="now"/>: the date of the last 06:00 in Beijing.</summary>
    public static DateOnly RecommendationDay(DateTimeOffset now) =>
        DateOnly.FromDateTime((now.ToOffset(BeijingOffset) - RenewalHour).DateTime);

    public bool IsCurrent(long userId, DateTimeOffset now) => UserId == userId && Day == RecommendationDay(now);
}

/// <summary>One JSON file, written atomically. Disk trouble never reaches the page: unreadable means absent.</summary>
public sealed class HomeSnapshotStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; } = filePath;

    public HomeSnapshot? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<HomeSnapshot>(File.ReadAllText(FilePath), Options) : null;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(HomeSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
