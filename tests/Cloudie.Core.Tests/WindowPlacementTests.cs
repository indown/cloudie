using Cloudie.Core.Settings;

namespace Cloudie.Core.Tests;

public class WindowPlacementTests
{
    [Fact]
    public void Round_trips_through_the_settings_store()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        Assert.Null(settings.WindowPlacement);
        settings.WindowPlacement = new WindowPlacement(100, 50, 1280, 820, IsMaximized: true);
        Assert.Equal(new WindowPlacement(100, 50, 1280, 820, true), settings.WindowPlacement);
        settings.WindowPlacement = null;
        Assert.Null(settings.WindowPlacement);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("""{"X":0,"Y":0,"Width":0,"Height":600,"IsMaximized":false}""")]
    public void Garbage_reads_as_null(string json) => Assert.Null(WindowPlacement.Parse(json));

    [Fact]
    public void Fit_moves_the_window_onto_the_display_and_clamps_its_size()
    {
        // Saved on a monitor to the right that is gone; the primary work area is 1920x1040 at the origin.
        var offscreen = new WindowPlacement(2500, 300, 1280, 820, false);
        Assert.Equal(new WindowPlacement(640, 220, 1280, 820, false), offscreen.FitInto(0, 0, 1920, 1040, 960, 600));

        var huge = new WindowPlacement(-50, -50, 4000, 3000, true);
        Assert.Equal(new WindowPlacement(0, 0, 1920, 1040, true), huge.FitInto(0, 0, 1920, 1040, 960, 600));

        var tiny = new WindowPlacement(10, 10, 200, 100, false);
        Assert.Equal(new WindowPlacement(10, 10, 960, 600, false), tiny.FitInto(0, 0, 1920, 1040, 960, 600));
    }
}
