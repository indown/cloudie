using System.Numerics;
using Cloudie.App.Converters;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Cloudie.App.Controls;

/// <summary>
/// A square cover with a title under it: songs, playlists, albums and charts on the browse pages. Hovering
/// lifts the cover and shows a play disc; the GridView item around it draws no highlight of its own.
/// </summary>
public sealed partial class CoverCard : UserControl
{
    public static readonly DependencyProperty ImageUrlProperty = DependencyProperty.Register(
        nameof(ImageUrl), typeof(string), typeof(CoverCard), new PropertyMetadata(null, (d, _) => ((CoverCard)d).UpdateImage()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CoverCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(CoverCard), new PropertyMetadata(null));

    /// <summary>Short text in the cover's corner, such as a play count; hidden when null.</summary>
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
        nameof(Badge), typeof(string), typeof(CoverCard), new PropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(CoverCard), new PropertyMetadata(150.0, (d, _) => ((CoverCard)d).OnSizeChanged()));

    /// <summary>The cover plus the air the hover scale grows into.</summary>
    public static readonly DependencyProperty HostSizeProperty = DependencyProperty.Register(
        nameof(HostSize), typeof(double), typeof(CoverCard), new PropertyMetadata(158.0));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(CoverCard), new PropertyMetadata(new CornerRadius(12)));

    /// <summary>Lines the title may take before it is trimmed; playlist names get two.</summary>
    public static readonly DependencyProperty TitleLinesProperty = DependencyProperty.Register(
        nameof(TitleLines), typeof(int), typeof(CoverCard), new PropertyMetadata(1));

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(CoverCard), new PropertyMetadata(null));

    private bool _pointerOver;
    private bool _pressed;

    public CoverCard() => InitializeComponent();

    public string? ImageUrl { get => (string?)GetValue(ImageUrlProperty); set => SetValue(ImageUrlProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? Badge { get => (string?)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double HostSize { get => (double)GetValue(HostSizeProperty); private set => SetValue(HostSizeProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public int TitleLines { get => (int)GetValue(TitleLinesProperty); set => SetValue(TitleLinesProperty, value); }
    public ImageSource? ImageSource { get => (ImageSource?)GetValue(ImageSourceProperty); private set => SetValue(ImageSourceProperty, value); }

    private void OnSizeChanged()
    {
        HostSize = Size + 8;
        UpdateImage();
    }

    /// <summary>Covers are fetched at twice the displayed size, which looks sharp at 150 % and 200 % scaling.</summary>
    private void UpdateImage() => ImageSource = CoverUrlConverter.ImageFor(ImageUrl, (int)(Size * 2));

    /// <summary>
    /// Animates the UIElement facades (Scale, Opacity) with Composition key-frame animations. The hand-in
    /// visual route (ElementCompositionPreview) was tried first and left the scale unchanged on this stack.
    /// </summary>
    private void Apply()
    {
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        var half = (float)(Size / 2);
        Cover.CenterPoint = new Vector3(half, half, 0);
        var factor = _pressed ? 1.0f : _pointerOver ? 1.04f : 1.0f;
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Target = "Scale";
        scale.InsertKeyFrame(1f, new Vector3(factor, factor, 1));
        scale.Duration = TimeSpan.FromMilliseconds(200);
        Cover.StartAnimation(scale);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(1f, _pointerOver ? 1f : 0f);
        fade.Duration = TimeSpan.FromMilliseconds(160);
        Shade.StartAnimation(fade);
        PlayDisc.StartAnimation(fade);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = true;
        Apply();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        _pressed = false;
        Apply();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressed = true;
        Apply();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        Apply();
    }
}
