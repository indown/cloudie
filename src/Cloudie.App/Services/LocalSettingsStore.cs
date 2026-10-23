using Cloudie.Core.Settings;
using Windows.Storage;

namespace Cloudie.App.Services;

/// <summary>Backs the core settings with the package's roaming-free local settings container.</summary>
public sealed class LocalSettingsStore : ISettingsStore
{
    private readonly ApplicationDataContainer _container = ApplicationData.Current.LocalSettings;

    public string? GetString(string key) => _container.Values.TryGetValue(key, out var value) ? value as string : null;

    public void SetString(string key, string? value)
    {
        if (value is null) _container.Values.Remove(key);
        else _container.Values[key] = value;
    }
}
