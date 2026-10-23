namespace Cloudie.Core.Settings;

/// <summary>Flat key/value persistence for preferences; the app backs it with ApplicationData.</summary>
public interface ISettingsStore
{
    string? GetString(string key);
    void SetString(string key, string? value);
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = new();
    public string? GetString(string key) => _values.GetValueOrDefault(key);
    public void SetString(string key, string? value)
    {
        if (value is null) _values.Remove(key);
        else _values[key] = value;
    }
}

public static class SettingsStoreExtensions
{
    public static bool GetBool(this ISettingsStore store, string key, bool fallback) =>
        bool.TryParse(store.GetString(key), out var value) ? value : fallback;

    public static void SetBool(this ISettingsStore store, string key, bool value) => store.SetString(key, value ? "true" : "false");

    public static int GetInt(this ISettingsStore store, string key, int fallback) =>
        int.TryParse(store.GetString(key), out var value) ? value : fallback;

    public static void SetInt(this ISettingsStore store, string key, int value) => store.SetString(key, value.ToString());

    public static double GetDouble(this ISettingsStore store, string key, double fallback) =>
        double.TryParse(store.GetString(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public static void SetDouble(this ISettingsStore store, string key, double value) =>
        store.SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
