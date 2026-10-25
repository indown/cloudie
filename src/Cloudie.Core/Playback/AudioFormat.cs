namespace Cloudie.Core.Playback;

/// <summary>
/// Works out the MIME type of a downloaded song. Media Foundation picks its byte-stream handler from the type
/// when a source is created from a stream, and CDNs often answer <c>application/octet-stream</c>, so the first
/// bytes are the most trustworthy witness, then the response header, then what the source said it served.
/// </summary>
public static class AudioFormat
{
    public const string Mpeg = "audio/mpeg";
    public const string Flac = "audio/flac";
    public const string Mp4 = "audio/mp4";
    public const string Aac = "audio/aac";
    public const string Ogg = "audio/ogg";
    public const string Wav = "audio/wav";

    /// <summary>The type from the file's magic bytes, or null when they are not a known audio container.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4)
        {
            if (head[..4].SequenceEqual("fLaC"u8)) return Flac;
            if (head[..3].SequenceEqual("ID3"u8)) return Mpeg;
            if (head[..4].SequenceEqual("OggS"u8)) return Ogg;
        }
        if (head.Length >= 12)
        {
            if (head[4..8].SequenceEqual("ftyp"u8)) return Mp4;
            if (head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WAVE"u8)) return Wav;
        }
        if (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)
        {
            // Frame sync. ADTS (AAC) has layer bits 00; MPEG audio has a non-zero layer.
            return (head[1] & 0x06) == 0 ? Aac : Mpeg;
        }
        return null;
    }

    /// <summary>A usable type from a Content-Type header: audio types pass, catch-alls do not.</summary>
    public static string? FromContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return null;
        var type = mediaType.Trim().ToLowerInvariant();
        return type switch
        {
            "audio/x-flac" => Flac,
            "audio/mp3" or "audio/x-mpeg" or "audio/mpeg3" => Mpeg,
            "audio/x-m4a" or "audio/m4a" => Mp4,
            _ when type.StartsWith("audio/", StringComparison.Ordinal) => type,
            _ => null
        };
    }

    /// <summary>A guess from what the source called the stream: a NetEase level name or a file suffix.</summary>
    public static string? FromServedQuality(string? quality) => quality?.Trim().ToLowerInvariant() switch
    {
        "flac" or "lossless" or "hires" or "jymaster" or "sky" or "jyeffect" or "dolby" => Flac,
        "mp3" or "standard" or "higher" or "exhigh" => Mpeg,
        "m4a" or "aac" or "mp4" => Mp4,
        "ogg" or "opus" => Ogg,
        "wav" => Wav,
        _ => null
    };
}
