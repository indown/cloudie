using System.Text;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Lyrics;

/// <summary>
/// Turns NetEase's lyric payloads into timed lines.
/// <para>
/// LRC: every line may start with one or more <c>[mm:ss.xx]</c> tags followed by the text; a line
/// with several tags repeats its text at each time. YRC (verbatim): a content line is
/// <c>[lineStartMs,lineDurationMs]</c> followed by <c>(startMs,durationMs,flag)text</c> runs; the
/// JSON credit lines NetEase prepends have no such header and are ignored.
/// </para>
/// </summary>
public static class LyricsParser
{
    /// <summary>Translations / romaji are attached to the main line closest in time, if within this.</summary>
    private static readonly TimeSpan SecondaryTolerance = TimeSpan.FromMilliseconds(300);

    private const string InstrumentalMarker = "纯音乐，请欣赏";

    /// <summary>Parses an LRC body into timed lines, sorted by time.</summary>
    public static List<(TimeSpan Time, string Text)> ParseLrc(string lrc)
    {
        var result = new List<(TimeSpan, string)>();
        foreach (var rawLine in EnumerateLines(lrc))
        {
            var line = rawLine.AsSpan().Trim();
            var times = new List<TimeSpan>();
            var position = 0;
            // Consume every leading time tag; anything that is not a tag ends the prefix.
            while (position < line.Length && line[position] == '[')
            {
                var close = line[position..].IndexOf(']');
                if (close < 0) break;
                var tag = line.Slice(position + 1, close - 1);
                if (!TryParseClock(tag, out var time)) break;
                times.Add(time);
                position += close + 1;
            }
            if (times.Count == 0) continue;
            var text = line[position..].Trim().ToString();
            foreach (var time in times) result.Add((time, text));
        }
        result.Sort(static (a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }

    /// <summary>Parses NetEase's verbatim (word-timed) format.</summary>
    public static List<LyricLine> ParseYrc(string yrc)
    {
        var lines = new List<LyricLine>();
        foreach (var rawLine in EnumerateLines(yrc))
        {
            var line = rawLine.AsSpan().Trim();
            if (line.Length == 0 || line[0] != '[') continue;
            var close = line.IndexOf(']');
            if (close < 0) continue;
            if (!TryParseMillisecondPair(line.Slice(1, close - 1), out var lineStart, out _)) continue;

            var words = new List<LyricWord>();
            var text = new StringBuilder();
            var rest = line[(close + 1)..];
            while (rest.Length > 0)
            {
                var open = rest.IndexOf('(');
                if (open < 0) break;
                var end = rest[open..].IndexOf(')');
                if (end < 0) break;
                var timing = rest.Slice(open + 1, end - 1);
                var afterTiming = rest[(open + end + 1)..];
                var next = afterTiming.IndexOf('(');
                var piece = (next < 0 ? afterTiming : afterTiming[..next]).ToString();
                if (TryParseWordTiming(timing, out var start, out var duration))
                {
                    words.Add(new LyricWord(piece, start, duration));
                    text.Append(piece);
                }
                rest = next < 0 ? ReadOnlySpan<char>.Empty : afterTiming[next..];
            }
            var content = text.ToString().Trim();
            if (words.Count == 0 || content.Length == 0) continue;
            lines.Add(new LyricLine { Index = lines.Count, Time = lineStart, Text = content, Words = words });
        }
        return lines;
    }

    public static ParsedLyrics Parse(LyricResponse response)
    {
        var contributor = response.LyricUser?.Nickname;
        var translator = response.TransUser?.Nickname;
        var lrc = response.Lrc?.Lyric;
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return new ParsedLyrics { Contributor = contributor, TranslationContributor = translator };
        }

        var timed = ParseLrc(lrc);
        var instrumental = false;
        if (timed.Count <= 10 && timed.Exists(static l => l.Text.Contains(InstrumentalMarker)))
        {
            instrumental = true;
            timed.RemoveAll(static l => l.Text.Contains(InstrumentalMarker) || IsCreditLine(l.Text, requireEmpty: false));
            if (timed.Count == 0)
            {
                return new ParsedLyrics { IsInstrumental = true, Contributor = contributor, TranslationContributor = translator };
            }
        }
        timed.RemoveAll(static l => IsCreditLine(l.Text, requireEmpty: true));

        List<LyricLine> lines;
        var verbatim = string.IsNullOrWhiteSpace(response.Yrc?.Lyric) ? [] : ParseYrc(response.Yrc.Lyric);
        if (verbatim.Count > 0)
        {
            lines = verbatim;
        }
        else
        {
            lines = new List<LyricLine>(timed.Count);
            foreach (var (time, text) in timed)
            {
                lines.Add(new LyricLine { Index = lines.Count, Time = time, Text = text });
            }
        }

        Attach(lines, response.Ytlrc?.Lyric ?? response.Tlyric?.Lyric, static (line, text) => line.Translation = text);
        Attach(lines, response.Yromalrc?.Lyric ?? response.Romalrc?.Lyric, static (line, text) => line.Romaji = text);

        return new ParsedLyrics
        {
            Lines = lines,
            IsInstrumental = instrumental,
            Contributor = contributor,
            TranslationContributor = translator
        };
    }

    /// <summary>Gives every main line the secondary text (translation, romaji) nearest in time, within tolerance.</summary>
    private static void Attach(List<LyricLine> lines, string? body, Action<LyricLine, string> assign)
    {
        if (string.IsNullOrWhiteSpace(body) || lines.Count == 0) return;
        var secondary = ParseLrc(body);
        secondary.RemoveAll(static l => l.Text.Length == 0);
        if (secondary.Count == 0) return;
        var times = secondary.Select(static l => l.Time).ToArray();
        foreach (var line in lines)
        {
            // Binary search for the insertion point, then compare the neighbours on both sides.
            var at = Array.BinarySearch(times, line.Time);
            if (at < 0) at = ~at;
            var best = -1;
            foreach (var candidate in new[] { at - 1, at })
            {
                if (candidate < 0 || candidate >= times.Length) continue;
                if (best < 0 || (times[candidate] - line.Time).Duration() < (times[best] - line.Time).Duration()) best = candidate;
            }
            if (best >= 0 && (times[best] - line.Time).Duration() <= SecondaryTolerance) assign(line, secondary[best].Text);
        }
    }

    private static IEnumerable<string> EnumerateLines(string body)
    {
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i == body.Length || body[i] == '\n')
            {
                yield return body[start..i];
                start = i + 1;
            }
        }
    }

    /// <summary>"mm:ss", "mm:ss.xx", "mm:ss.xxx" or "mm:ss:xx" inside an LRC tag.</summary>
    private static bool TryParseClock(ReadOnlySpan<char> tag, out TimeSpan time)
    {
        time = default;
        var colon = tag.IndexOf(':');
        if (colon <= 0 || !int.TryParse(tag[..colon], out var minutes)) return false;
        var rest = tag[(colon + 1)..];
        var separator = rest.IndexOfAny('.', ':');
        var secondsPart = separator < 0 ? rest : rest[..separator];
        if (!int.TryParse(secondsPart, out var seconds)) return false;
        var fraction = 0.0;
        if (separator >= 0)
        {
            var digits = rest[(separator + 1)..];
            if (digits.Length == 0 || !int.TryParse(digits, out var value)) return false;
            fraction = value / Math.Pow(10, digits.Length);
        }
        time = TimeSpan.FromSeconds(minutes * 60 + seconds + fraction);
        return true;
    }

    private static bool TryParseMillisecondPair(ReadOnlySpan<char> text, out TimeSpan first, out TimeSpan second)
    {
        first = second = default;
        var comma = text.IndexOf(',');
        if (comma < 0) return false;
        if (!int.TryParse(text[..comma], out var a) || !int.TryParse(text[(comma + 1)..], out var b)) return false;
        first = TimeSpan.FromMilliseconds(a);
        second = TimeSpan.FromMilliseconds(b);
        return true;
    }

    /// <summary>"startMs,durationMs,flag" — the flag is ignored.</summary>
    private static bool TryParseWordTiming(ReadOnlySpan<char> text, out TimeSpan start, out TimeSpan duration)
    {
        start = duration = default;
        var first = text.IndexOf(',');
        if (first < 0) return false;
        var secondPart = text[(first + 1)..];
        var second = secondPart.IndexOf(',');
        var durationText = second < 0 ? secondPart : secondPart[..second];
        if (!int.TryParse(text[..first], out var s) || !int.TryParse(durationText, out var d)) return false;
        start = TimeSpan.FromMilliseconds(s);
        duration = TimeSpan.FromMilliseconds(d);
        return true;
    }

    /// <summary>"作词 : …" / "作曲：…" credit lines; with <paramref name="requireEmpty"/> only the ones whose value is 无.</summary>
    private static bool IsCreditLine(string text, bool requireEmpty)
    {
        var trimmed = text.AsSpan().Trim();
        if (trimmed.Length < 3 || trimmed[0] != '作' || (trimmed[1] != '词' && trimmed[1] != '曲')) return false;
        var rest = trimmed[2..].TrimStart();
        if (rest.Length == 0 || (rest[0] != ':' && rest[0] != '：')) return false;
        return !requireEmpty || rest[1..].Trim().SequenceEqual("无");
    }
}
