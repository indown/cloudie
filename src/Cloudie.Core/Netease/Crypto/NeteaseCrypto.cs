using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Cloudie.Core.Netease.Crypto;

/// <summary>
/// NetEase Cloud Music request encryption.
/// <para>
/// weapi: two rounds of AES-128-CBC over the JSON payload, first with a preset key and
/// then with a client-chosen secret key; the secret key travels RSA-encrypted (raw modular
/// exponentiation, no padding) as <c>encSecKey</c>. The key is ours to choose, so one fixed
/// key and its precomputed ciphertext are reused for every request.
/// </para>
/// <para>
/// eapi: AES-128-ECB over "path + payload + md5 digest" with a fixed key, upper-case hex.
/// </para>
/// </summary>
public static class NeteaseCrypto
{
    private const string WeapiPresetKey = "0CoJUm6Qyw8W8jud";
    private const string WeapiIv = "0102030405060708";
    private const string WeapiModulusHex =
        "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7";
    private const int WeapiPublicExponent = 65537;
    private const string EapiKey = "e82ckenh8dichen8";

    /// <summary>The secret key used for every weapi request (16 ASCII characters).</summary>
    public const string DefaultWeapiSecretKey = "cloudie2026ABCDE";

    private static readonly Lazy<string> DefaultEncSecKey = new(() => EncSecKey(DefaultWeapiSecretKey));

    /// <summary>Encrypts a JSON payload for a <c>/weapi/...</c> endpoint and returns the form fields.</summary>
    public static IReadOnlyDictionary<string, string> Weapi(string json, string? secretKey = null)
    {
        secretKey ??= DefaultWeapiSecretKey;
        if (secretKey.Length != 16) throw new ArgumentException("The weapi secret key must be 16 characters.", nameof(secretKey));
        var first = Convert.ToBase64String(Aes128(Encoding.UTF8.GetBytes(json), WeapiPresetKey, WeapiIv));
        var second = Convert.ToBase64String(Aes128(Encoding.UTF8.GetBytes(first), secretKey, WeapiIv));
        var encSecKey = secretKey == DefaultWeapiSecretKey ? DefaultEncSecKey.Value : EncSecKey(secretKey);
        return new Dictionary<string, string> { ["params"] = second, ["encSecKey"] = encSecKey };
    }

    /// <summary>
    /// RSA-encrypts the secret key the way the web client does: the key's characters are
    /// reversed, interpreted as a big-endian integer and raised to the public exponent modulo
    /// NetEase's public modulus, without any padding. The result is 256 hex characters.
    /// </summary>
    public static string EncSecKey(string secretKey)
    {
        var reversed = new string(secretKey.Reverse().ToArray());
        var message = new BigInteger(Encoding.UTF8.GetBytes(reversed), isUnsigned: true, isBigEndian: true);
        var modulus = new BigInteger(Convert.FromHexString(WeapiModulusHex), isUnsigned: true, isBigEndian: true);
        var cipher = BigInteger.ModPow(message, WeapiPublicExponent, modulus);
        return cipher.ToString("x").TrimStart('0').PadLeft(256, '0');
    }

    /// <summary>Encrypts a JSON payload for an <c>/eapi/...</c> endpoint and returns the form fields.</summary>
    /// <param name="apiPath">The internal API path, e.g. <c>/api/song/enhance/player/url/v1</c>.</param>
    public static IReadOnlyDictionary<string, string> Eapi(string apiPath, string json)
    {
        var digest = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes($"nobody{apiPath}use{json}md5forencrypt")));
        var data = $"{apiPath}-36cd479b6b5-{json}-36cd479b6b5-{digest}";
        var encrypted = Aes128(Encoding.UTF8.GetBytes(data), EapiKey, iv: null);
        return new Dictionary<string, string> { ["params"] = Convert.ToHexString(encrypted) };
    }

    /// <summary>Decrypts an eapi body (hex) back to its plain text; used by tests and diagnostics.</summary>
    public static string EapiDecrypt(string hex)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(EapiKey);
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        return Encoding.UTF8.GetString(aes.DecryptEcb(Convert.FromHexString(hex), PaddingMode.PKCS7));
    }

    /// <summary>Decrypts one weapi round (CBC with the preset IV); used by tests.</summary>
    public static byte[] WeapiDecryptRound(byte[] data, string key)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key);
        return aes.DecryptCbc(data, Encoding.UTF8.GetBytes(WeapiIv), PaddingMode.PKCS7);
    }

    private static byte[] Aes128(byte[] data, string key, string? iv)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key);
        return iv is null
            ? aes.EncryptEcb(data, PaddingMode.PKCS7)
            : aes.EncryptCbc(data, Encoding.UTF8.GetBytes(iv), PaddingMode.PKCS7);
    }
}
