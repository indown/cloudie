using Cloudie.Core.Lyrics;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Tests;

public class LyricsParserTests
{
    [Fact]
    public void Lrc_handles_multiple_timestamps_and_both_separators()
    {
        var lines = LyricsParser.ParseLrc("[00:01.50][00:05:25]hello\n[00:03.000] world \nno tag here\n");
        Assert.Equal(3, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(1.5), lines[0].Time);
        Assert.Equal("hello", lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(3), lines[1].Time);
        Assert.Equal("world", lines[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(5.25), lines[2].Time);
    }

    [Fact]
    public void Yrc_lines_carry_word_timings_and_skip_metadata()
    {
        const string yrc = """
            {"t":0,"c":[{"tx":"作词: 方文山"}]}
            [1000,2000](1000,500,0)青(1500,500,0)花(2000,1000,0)瓷
            [4000,1000](4000,1000,0)
            """;
        var lines = LyricsParser.ParseYrc(yrc);
        var line = Assert.Single(lines);
        Assert.Equal("青花瓷", line.Text);
        Assert.Equal(TimeSpan.FromSeconds(1), line.Time);
        Assert.Equal(3, line.Words!.Count);
        Assert.Equal(TimeSpan.FromSeconds(2), line.Words[2].Start);
        Assert.Equal(TimeSpan.FromSeconds(3), line.Words[2].End);
    }

    [Fact]
    public void Parse_prefers_yrc_and_attaches_translations_by_nearest_time()
    {
        var response = new LyricResponse
        {
            Lrc = new LyricBody { Lyric = "[00:01.00]hello\n[00:03.00]world" },
            Yrc = new LyricBody { Lyric = "[1005,1000](1005,500,0)hel(1505,500,0)lo\n[3002,1000](3002,1000,0)world" },
            Tlyric = new LyricBody { Lyric = "[00:01.00]你好\n[00:03.00]世界\n[00:09.00]far" },
            LyricUser = new LyricContributor { Nickname = "someone" }
        };
        var parsed = LyricsParser.Parse(response);
        Assert.Equal(2, parsed.Lines.Count);
        Assert.NotNull(parsed.Lines[0].Words);
        Assert.Equal("你好", parsed.Lines[0].Translation);
        Assert.Equal("世界", parsed.Lines[1].Translation);
        Assert.Equal("someone", parsed.Contributor);
        Assert.Equal(-1, parsed.ActiveIndex(TimeSpan.Zero));
        Assert.Equal(0, parsed.ActiveIndex(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, parsed.ActiveIndex(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Instrumental_marker_is_recognised()
    {
        var parsed = LyricsParser.Parse(new LyricResponse { Lrc = new LyricBody { Lyric = "[00:00.00]纯音乐，请欣赏\n[00:00.00]作曲 : 某某" } });
        Assert.True(parsed.IsInstrumental);
        Assert.True(parsed.IsEmpty);
    }
}
