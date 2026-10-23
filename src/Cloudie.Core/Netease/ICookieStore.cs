using System.Text.Json;

namespace Cloudie.Core.Netease;

/// <summary>Persists the NetEase cookie jar (MUSIC_U, __csrf, ...) between runs.</summary>
public interface ICookieStore
{
    IReadOnlyDictionary<string, string> Load();
    void Save(IReadOnlyDictionary<string, string> cookies);
}

/// <summary>Keeps the cookies in memory only; the default for tests and tools.</summary>
public sealed class InMemoryCookieStore : ICookieStore
{
    private Dictionary<string, string> _cookies = new();
    public IReadOnlyDictionary<string, string> Load() => _cookies;
    public void Save(IReadOnlyDictionary<string, string> cookies) => _cookies = new(cookies);
}

/// <summary>Stores the cookies as a JSON object in a file, written atomically.</summary>
public sealed class JsonFileCookieStore(string path) : ICookieStore
{
    public string Path { get; } = path;

    public IReadOnlyDictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(Path)) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path)) ?? new();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public void Save(IReadOnlyDictionary<string, string> cookies)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(cookies));
        File.Move(temp, Path, overwrite: true);
    }
}
