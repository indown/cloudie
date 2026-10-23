using System.Text.Json;

namespace Cloudie.Core.Settings;

/// <summary>
/// Flat key/value settings in one JSON file, written atomically. Works with or without MSIX
/// package identity, so packaged and unpackaged builds share the same code.
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _values;

    public JsonFileSettingsStore(string path)
    {
        Path = path;
        _values = Load(path);
    }

    public string Path { get; }

    public string? GetString(string key)
    {
        lock (_gate) return _values.GetValueOrDefault(key);
    }

    public void SetString(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null)
            {
                if (!_values.Remove(key)) return;
            }
            else
            {
                if (_values.TryGetValue(key, out var existing) && existing == value) return;
                _values[key] = value;
            }
            Save();
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path, overwrite: true);
    }
}
