using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Playback;

/// <summary>Everything needed to put the queue back after a restart, minus the position.</summary>
public sealed record PlaybackSnapshot(
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<int>? PlayOrder,
    bool IsShuffled,
    RepeatMode RepeatMode,
    long SourceId,
    bool IsFm);

/// <summary>Where playback was: written often, so it lives apart from the (large) queue.</summary>
public sealed record PlaybackPosition(long TrackId, int CurrentIndex, TimeSpan Position);

/// <summary>Persists the queue and the position as two JSON files, each written atomically.</summary>
public sealed class PlaybackStore(string queuePath, string positionPath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() }
    };

    public string QueuePath { get; } = queuePath;
    public string PositionPath { get; } = positionPath;

    public void SaveQueue(PlaybackSnapshot snapshot) => WriteAtomically(QueuePath, JsonSerializer.Serialize(snapshot, Options));

    public PlaybackSnapshot? LoadQueue() => Read<PlaybackSnapshot>(QueuePath);

    public void SavePosition(PlaybackPosition position) => WriteAtomically(PositionPath, JsonSerializer.Serialize(position, Options));

    public PlaybackPosition? LoadPosition() => Read<PlaybackPosition>(PositionPath);

    public void Clear()
    {
        File.Delete(QueuePath);
        File.Delete(PositionPath);
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
