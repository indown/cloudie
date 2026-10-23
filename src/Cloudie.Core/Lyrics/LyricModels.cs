namespace Cloudie.Core.Lyrics;

/// <summary>One timed word (or short run) inside a verbatim (yrc) lyric line.</summary>
public sealed record LyricWord(string Text, TimeSpan Start, TimeSpan Duration)
{
    public TimeSpan End => Start + Duration;
}

public sealed class LyricLine
{
    public required int Index { get; init; }
    public required TimeSpan Time { get; init; }
    public required string Text { get; init; }
    public string? Translation { get; set; }
    public string? Romaji { get; set; }
    /// <summary>Per-word timings for karaoke highlighting; null when only line timing is known.</summary>
    public IReadOnlyList<LyricWord>? Words { get; init; }
}

public sealed class ParsedLyrics
{
    public static readonly ParsedLyrics Empty = new();

    public IReadOnlyList<LyricLine> Lines { get; init; } = [];
    public bool IsInstrumental { get; init; }
    public string? Contributor { get; init; }
    public string? TranslationContributor { get; init; }
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Index of the line active at a playback position, or -1 before the first line.</summary>
    public int ActiveIndex(TimeSpan position)
    {
        int low = 0, high = Lines.Count - 1, result = -1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (Lines[mid].Time <= position)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return result;
    }
}
