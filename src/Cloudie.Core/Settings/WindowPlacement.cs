using System.Text.Json;

namespace Cloudie.Core.Settings;

/// <summary>The main window's last normal (un-maximized) bounds in screen pixels, plus whether it was maximized.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized)
{
    public static WindowPlacement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var placement = JsonSerializer.Deserialize<WindowPlacement>(json);
            return placement is { Width: > 0, Height: > 0 } ? placement : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// Keeps the window usable on the display it will appear on: no larger than the work area, no smaller
    /// than the minimum, and moved fully inside (a monitor that was unplugged must not swallow the window).
    /// </summary>
    public WindowPlacement FitInto(int areaX, int areaY, int areaWidth, int areaHeight, int minWidth, int minHeight)
    {
        var width = Math.Clamp(Width, Math.Min(minWidth, areaWidth), areaWidth);
        var height = Math.Clamp(Height, Math.Min(minHeight, areaHeight), areaHeight);
        var x = Math.Clamp(X, areaX, areaX + areaWidth - width);
        var y = Math.Clamp(Y, areaY, areaY + areaHeight - height);
        return this with { X = x, Y = y, Width = width, Height = height };
    }
}
