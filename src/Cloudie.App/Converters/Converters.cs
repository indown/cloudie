using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cloudie.App.Converters;

public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

public sealed partial class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Collapsed;
}

public sealed partial class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class TimeSpanToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not TimeSpan span) return "0:00";
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Appends NetEase's resize parameter so covers are fetched at the size they are shown.</summary>
public sealed partial class CoverUrlConverter : IValueConverter
{
    public static BitmapImage? ImageFor(string? url, int size) => Services.CoverCache.Get(url, size);

    public object? Convert(object value, Type targetType, object parameter, string language) =>
        ImageFor(value as string, int.TryParse(parameter as string, out var size) ? size : 300);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class PlayCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var count = value switch { long l => l, int i => i, _ => 0L };
        return count switch
        {
            >= 100_000_000 => $"{count / 100_000_000.0:0.#}亿",
            >= 10_000 => $"{count / 10_000.0:0.#}万",
            _ => count.ToString()
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
