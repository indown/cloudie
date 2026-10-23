using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Controls;

public enum NavigationKind { Header, Separator, Home, DailySongs, Fm, Playlist, NewPlaylist, Search, Settings }

/// <summary>One row of the sidebar: a fixed destination, a header, or one of the account's playlists.</summary>
public sealed class NavigationEntry
{
    public required NavigationKind Kind { get; init; }
    public required string Title { get; init; }
    public string Glyph { get; init; } = "";
    public long Id { get; init; }
    /// <summary>One of the account's own playlists other than "我喜欢的音乐": it can be renamed or deleted.</summary>
    public bool CanEdit { get; init; }

    public static NavigationEntry Header(string title) => new() { Kind = NavigationKind.Header, Title = title };
    public static NavigationEntry Separator() => new() { Kind = NavigationKind.Separator, Title = string.Empty };
}

public sealed partial class NavigationItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ItemTemplate { get; set; }
    /// <summary>Like <see cref="ItemTemplate"/>, with a rename / delete context menu.</summary>
    public DataTemplate? EditableItemTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? SeparatorTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is NavigationEntry entry
        ? entry.Kind switch
        {
            NavigationKind.Header => HeaderTemplate,
            NavigationKind.Separator => SeparatorTemplate,
            NavigationKind.Playlist when entry.CanEdit => EditableItemTemplate ?? ItemTemplate,
            _ => ItemTemplate
        }
        : ItemTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
