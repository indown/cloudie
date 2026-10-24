using Microsoft.UI.Xaml.Media.Imaging;

namespace Cloudie.App.Services;

/// <summary>
/// Decoded covers, shared by every Image that shows the same URL at the same size. A BitmapImage can back any
/// number of Image controls, so a recycled list row scrolling back to a cover it already had pays nothing.
/// Bounded by estimated bytes (width × height × 4), least recently used out first; a cover whose download
/// failed is dropped so the next request retries it. UI thread only, like the converters that call it.
/// </summary>
public static class CoverCache
{
    private const long Budget = 64L * 1024 * 1024;

    private sealed class Entry(string key, BitmapImage image, long bytes)
    {
        public string Key { get; } = key;
        public BitmapImage Image { get; } = image;
        public long Bytes { get; set; } = bytes;
    }

    private static readonly Dictionary<string, LinkedListNode<Entry>> Index = new();
    private static readonly LinkedList<Entry> Order = new();   // front = most recently used
    private static long _total;

    /// <summary>The cover at <paramref name="url"/>, sized by NetEase to <paramref name="size"/> px square.</summary>
    public static BitmapImage? Get(string? url, int size)
    {
        if (url is not { Length: > 0 }) return null;
        var key = $"{url}{(url.Contains('?') ? "&" : "?")}param={size}y{size}";
        if (Index.TryGetValue(key, out var node))
        {
            Order.Remove(node);
            Order.AddFirst(node);
            return node.Value.Image;
        }
        var image = new BitmapImage
        {
            // The server already resizes; the hint keeps memory at the requested size if a node ignores "param".
            DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = size,
            DecodePixelHeight = size,
            UriSource = new Uri(key)
        };
        var entry = new Entry(key, image, (long)size * size * 4);
        image.ImageFailed += (_, _) => Remove(key);
        image.ImageOpened += (_, _) => Resize(key, image);
        Index[key] = Order.AddFirst(entry);
        _total += entry.Bytes;
        Trim();
        return image;
    }

    /// <summary>Replaces the estimate with the decoded size once it is known.</summary>
    private static void Resize(string key, BitmapImage image)
    {
        if (!Index.TryGetValue(key, out var node) || image.PixelWidth <= 0) return;
        var actual = (long)image.PixelWidth * image.PixelHeight * 4;
        _total += actual - node.Value.Bytes;
        node.Value.Bytes = actual;
        Trim();
    }

    private static void Remove(string key)
    {
        if (!Index.Remove(key, out var node)) return;
        Order.Remove(node);
        _total -= node.Value.Bytes;
    }

    private static void Trim()
    {
        while (_total > Budget && Order.Last is { } last && last != Order.First)
        {
            Order.RemoveLast();
            Index.Remove(last.Value.Key);
            _total -= last.Value.Bytes;
        }
    }
}
