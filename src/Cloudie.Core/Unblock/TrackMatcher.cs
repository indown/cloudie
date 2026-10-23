using System.Globalization;
using System.Text;

namespace Cloudie.Core.Unblock;

/// <summary>A search hit from a third-party source, in a source-neutral shape.</summary>
public sealed record MatchCandidate(string Id, string Title, string? Artist, int? DurationSeconds, string? Suffix = null, int? BitRate = null);

/// <summary>
/// Picks the library track that is the same recording as a NetEase song. The title has to match
/// exactly after normalization, and either the artist or the duration has to confirm it; a bare
/// title match is never enough, because a personal library must not fall back to "the first result".
/// </summary>
public static class TrackMatcher
{
    private static readonly char[] ArtistSeparators = ['/', '&', '、', ',', '，', ';', '；'];

    public static MatchCandidate? Pick(IEnumerable<MatchCandidate> candidates, UnblockTrack track)
    {
        var wantedTitle = Normalize(track.Name);
        if (wantedTitle.Length == 0) return null;
        var wantedArtists = track.Artists.Select(Normalize).Where(static a => a.Length > 0).ToList();

        MatchCandidate? best = null;
        var bestScore = 0.0;
        foreach (var candidate in candidates)
        {
            if (Normalize(candidate.Title) != wantedTitle) continue;
            double score = (SameArtist(candidate, wantedArtists) ? 2 : 0) + (SameDuration(candidate, track) ? 1 : 0);
            if (score == 0) continue;
            score += FormatRank(candidate);
            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    private static bool SameArtist(MatchCandidate candidate, List<string> wanted) =>
        ArtistNames(candidate.Artist).Any(name => wanted.Any(w => name.Contains(w) || w.Contains(name)));

    private static bool SameDuration(MatchCandidate candidate, UnblockTrack track) =>
        candidate.DurationSeconds is > 0 && track.DurationMs > 0 &&
        Math.Abs(candidate.DurationSeconds.Value * 1000 - track.DurationMs) < 5000;

    /// <summary>
    /// Tie-breaker between otherwise equal copies: FLAC and MP3 are known to play everywhere,
    /// Apple Lossless in m4a (lossless bit rates) is the least certain.
    /// </summary>
    private static double FormatRank(MatchCandidate candidate) => (candidate.Suffix ?? string.Empty).ToLowerInvariant() switch
    {
        "flac" => 0.3,
        "mp3" => 0.2,
        "m4a" or "aac" => candidate.BitRate > 400 ? 0 : 0.2,
        _ => 0.1
    };

    public static IReadOnlyList<string> ArtistNames(string? value) =>
        (value ?? string.Empty).Split(ArtistSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(static n => n.Length > 0).ToList();

    /// <summary>Compatibility-folded, lower-cased letters and digits only; CJK characters count as letters.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var folded = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(folded.Length);
        foreach (var rune in folded.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetterOrDigit(rune) || category is UnicodeCategory.OtherLetter)
            {
                builder.Append(rune.ToString());
            }
        }
        return builder.ToString();
    }
}
