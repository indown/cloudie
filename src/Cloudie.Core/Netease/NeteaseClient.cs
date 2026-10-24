using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudie.Core.Netease.Crypto;

namespace Cloudie.Core.Netease;

/// <summary>
/// Transport layer for NetEase Cloud Music: owns the cookie jar and performs weapi / eapi
/// encrypted POSTs, mirroring what the desktop clients send.
/// </summary>
public sealed class NeteaseClient : IDisposable
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    /// <summary>The domestic address sent as <c>X-Real-IP</c> when <see cref="SpoofDomesticIp"/> is on.</summary>
    public const string DomesticIp = "118.88.88.88";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new LenientNumberConverter() }
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ICookieStore _cookieStore;
    private readonly object _cookieLock = new();
    private Dictionary<string, string> _cookies;

    public NeteaseClient(ICookieStore? cookieStore = null, HttpMessageHandler? handler = null)
    {
        _cookieStore = cookieStore ?? new InMemoryCookieStore();
        _cookies = new Dictionary<string, string>(_cookieStore.Load());
        if (handler is null)
        {
            handler = new SocketsHttpHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            _ownsHttp = true;
        }
        _http = new HttpClient(handler, disposeHandler: _ownsHttp) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>Send NetEase a domestic IP so an account used abroad can play region-locked songs.</summary>
    public bool SpoofDomesticIp { get; set; } = true;

    public string UserAgent { get; set; } = DefaultUserAgent;

    public bool IsLoggedIn => Cookie("MUSIC_U") is { Length: > 0 };

    public string? Cookie(string name)
    {
        lock (_cookieLock) return _cookies.GetValueOrDefault(name);
    }

    public IReadOnlyDictionary<string, string> Cookies
    {
        get { lock (_cookieLock) return new Dictionary<string, string>(_cookies); }
    }

    public void SetCookies(IReadOnlyDictionary<string, string> cookies)
    {
        lock (_cookieLock)
        {
            foreach (var (name, value) in cookies) _cookies[name] = value;
            _cookieStore.Save(new Dictionary<string, string>(_cookies));
        }
    }

    public void ClearAuthCookies()
    {
        lock (_cookieLock)
        {
            _cookies.Remove("MUSIC_U");
            _cookies.Remove("__csrf");
            _cookieStore.Save(new Dictionary<string, string>(_cookies));
        }
    }

    /// <summary>POSTs to <c>https://music.163.com/weapi{path}</c> with weapi encryption.</summary>
    public async Task<string> WeapiAsync(string path, IReadOnlyDictionary<string, object?>? payload = null,
        IReadOnlyDictionary<string, string>? cookieOverrides = null, bool absorbCookies = true,
        CancellationToken cancellationToken = default)
    {
        var body = payload is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(payload);
        body["csrf_token"] = cookieOverrides?.GetValueOrDefault("__csrf") ?? Cookie("__csrf") ?? string.Empty;
        var form = NeteaseCrypto.Weapi(JsonSerializer.Serialize(body, PayloadOptions));
        var url = $"https://music.163.com/weapi{path}";
        return await PostAsync(url, form, cookieOverrides, absorbCookies, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>POSTs to <c>https://interface.music.163.com/eapi{path}</c> with eapi encryption.</summary>
    public async Task<string> EapiAsync(string path, IReadOnlyDictionary<string, object?>? payload = null,
        IReadOnlyDictionary<string, string>? cookieOverrides = null, bool absorbCookies = true,
        CancellationToken cancellationToken = default)
    {
        var apiPath = "/api" + (path.Contains('?') ? path[..path.IndexOf('?')] : path);
        var body = payload is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(payload);
        var header = new Dictionary<string, string>
        {
            ["os"] = "pc",
            ["appver"] = "3.1.17",
            ["osver"] = "Microsoft-Windows-10-Professional-build-26100-64bit",
            ["deviceId"] = "cloudie",
            ["requestId"] = Random.Shared.Next(20_000_000, 30_000_000).ToString(),
            ["clientSign"] = string.Empty,
            ["versioncode"] = "140",
            ["buildver"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["resolution"] = "1920x1080",
            ["channel"] = string.Empty
        };
        if (Cookie("MUSIC_U") is { } musicU) header["MUSIC_U"] = musicU;
        if (Cookie("__csrf") is { } csrf) header["__csrf"] = csrf;
        body["header"] = header;
        var form = NeteaseCrypto.Eapi(apiPath, JsonSerializer.Serialize(body, PayloadOptions));
        var url = $"https://interface.music.163.com/eapi{path}";
        return await PostAsync(url, form, cookieOverrides, absorbCookies, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deserializes a reply, surfacing non-200 business codes as <see cref="NeteaseApiException"/>.</summary>
    public static T Decode<T>(string json)
    {
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("code", out var codeElement) &&
                codeElement.TryGetInt32(out var code) && code != 200)
            {
                var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()
                    : root.TryGetProperty("msg", out var m2) && m2.ValueKind == JsonValueKind.String ? m2.GetString() : null;
                throw new NeteaseApiException(code, code == 301 ? "需要登录" : message);
            }
        }
        return JsonSerializer.Deserialize<T>(json, ResponseOptions)
               ?? throw new NeteaseApiException(-1, "数据加载失败");
    }

    private async Task<string> PostAsync(string url, IReadOnlyDictionary<string, string> form,
        IReadOnlyDictionary<string, string>? cookieOverrides, bool absorbCookies, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://music.163.com");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(cookieOverrides));
        if (SpoofDomesticIp)
        {
            request.Headers.TryAddWithoutValidation("X-Real-IP", DomesticIp);
        }
        request.Content = new FormUrlEncodedContent(form);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        if (absorbCookies) AbsorbSetCookies(response);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new NeteaseApiException((int)response.StatusCode, $"网络错误 ({(int)response.StatusCode})");
        }
        return text;
    }

    private string BuildCookieHeader(IReadOnlyDictionary<string, string>? overrides)
    {
        Dictionary<string, string> all;
        lock (_cookieLock) all = new Dictionary<string, string>(_cookies);
        all.TryAdd("os", "pc");
        all.TryAdd("appver", "3.1.17");
        if (overrides is not null)
        {
            foreach (var (name, value) in overrides) all[name] = value;
        }
        return string.Join("; ", all.Select(static c => $"{c.Key}={c.Value}"));
    }

    private void AbsorbSetCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
        var fresh = new Dictionary<string, string>();
        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var name = pair[..eq].Trim();
            var value = pair[(eq + 1)..].Trim();
            if (value.Length == 0 || value == "\"\"") continue;
            fresh[name] = value;
        }
        if (fresh.Count > 0) SetCookies(fresh);
    }

    public void Dispose() => _http.Dispose();
}
