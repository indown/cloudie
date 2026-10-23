using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace Cloudie.App.Services;

/// <summary>Two colours drawn from the cover art, for the now-playing backdrop gradient.</summary>
public readonly record struct ArtworkColors(Color Primary, Color Secondary)
{
    public static readonly ArtworkColors Fallback = new(Color.FromArgb(255, 58, 58, 72), Color.FromArgb(255, 24, 24, 32));
}

public static class ArtworkPalette
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, ArtworkColors> Cache = new();

    /// <summary>
    /// Downsamples the image and picks the most saturated bucket of the hue histogram as the
    /// primary colour; the secondary is the same hue, darker, so the gradient stays calm.
    /// </summary>
    public static async Task<ArtworkColors> ExtractAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return ArtworkColors.Fallback;
        lock (Cache)
        {
            if (Cache.TryGetValue(url, out var cached)) return cached;
        }
        try
        {
            var bytes = await Http.GetByteArrayAsync(url + "?param=64y64", ct);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = 32, ScaledHeight = 32 }, ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var colors = Analyse(pixels.DetachPixelData());
            lock (Cache)
            {
                if (Cache.Count > 200) Cache.Clear();
                Cache[url] = colors;
            }
            return colors;
        }
        catch
        {
            return ArtworkColors.Fallback;
        }
    }

    private static ArtworkColors Analyse(byte[] bgra)
    {
        // 12 hue buckets weighted by saturation and brightness; grey pixels vote for the average.
        var weights = new double[12];
        var sums = new (double R, double G, double B)[12];
        double avgR = 0, avgG = 0, avgB = 0;
        var count = bgra.Length / 4;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            double b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            avgR += r; avgG += g; avgB += b;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var saturation = max == 0 ? 0 : (max - min) / max;
            if (saturation < 0.15 || max < 40) continue;
            var hue = Hue(r, g, b, max, min);
            var bucket = (int)(hue / 30) % 12;
            var weight = saturation * (max / 255.0);
            weights[bucket] += weight;
            sums[bucket] = (sums[bucket].R + r * weight, sums[bucket].G + g * weight, sums[bucket].B + b * weight);
        }
        if (count == 0) return ArtworkColors.Fallback;
        var best = Array.IndexOf(weights, weights.Max());
        Color primary;
        if (weights[best] <= 0)
        {
            primary = Color.FromArgb(255, (byte)(avgR / count), (byte)(avgG / count), (byte)(avgB / count));
        }
        else
        {
            var w = weights[best];
            primary = Color.FromArgb(255, (byte)(sums[best].R / w), (byte)(sums[best].G / w), (byte)(sums[best].B / w));
        }
        primary = Temper(primary);
        var secondary = Color.FromArgb(255, (byte)(primary.R * 0.35), (byte)(primary.G * 0.35), (byte)(primary.B * 0.35));
        return new ArtworkColors(primary, secondary);
    }

    /// <summary>Keeps the backdrop readable: very bright colours are pulled down, very dark ones lifted.</summary>
    private static Color Temper(Color color)
    {
        var luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        var factor = luminance > 150 ? 150 / luminance : luminance < 40 ? 1.6 : 1.0;
        return Color.FromArgb(255, (byte)Math.Min(255, color.R * factor), (byte)Math.Min(255, color.G * factor), (byte)Math.Min(255, color.B * factor));
    }

    private static double Hue(double r, double g, double b, double max, double min)
    {
        var delta = max - min;
        if (delta == 0) return 0;
        double hue;
        if (max == r) hue = 60 * (((g - b) / delta) % 6);
        else if (max == g) hue = 60 * ((b - r) / delta + 2);
        else hue = 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }
}
