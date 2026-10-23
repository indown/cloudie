using System.Text;
using Cloudie.Core.Netease.Crypto;

namespace Cloudie.Core.Tests;

public class NeteaseCryptoTests
{
    // Reference values produced with UnblockNeteaseMusic's crypto.js (node) for the same inputs.
    private const string EapiPath = "/api/song/enhance/player/url/v1";
    private const string EapiJson = """{"ids":"[\"185811\"]","level":"exhigh","encodeType":"flac"}""";
    private const string EapiExpectedHex =
        "FA90B329E9614F79E79598F37DC2EDB487F00D1BC4C9B24CD57E6C318B9073569338432CD7D98D1A3626E997A2C5312161DEAB4FD1EDB4524065FA6638E925AC8D4EBDF181CDBCD4213E43962F96C98CDE7F7DC50670ED7C820E146A12F67B090E8E9F65B9FBA177C7E46C7641D675201256E71EF4735D1EFD0A73E96336EC7567E8FFD99DCFC0C01BFBF44E129D903A95896686B14CE7140BB8C6DD470C9112";

    [Fact]
    public void Eapi_matches_the_reference_implementation()
    {
        var form = NeteaseCrypto.Eapi(EapiPath, EapiJson);
        Assert.Equal(EapiExpectedHex, form["params"]);
        Assert.Single(form);
    }

    [Fact]
    public void Eapi_body_round_trips_through_decrypt()
    {
        var plain = NeteaseCrypto.EapiDecrypt(NeteaseCrypto.Eapi(EapiPath, EapiJson)["params"]);
        Assert.Equal($"{EapiPath}-36cd479b6b5-{EapiJson}-36cd479b6b5-826ae6edb7b22114789ee9760cfc276b", plain);
    }

    [Fact]
    public void Weapi_params_match_the_reference_for_the_project_key()
    {
        // Produced with node's crypto for the same key: aes-128-cbc(preset) then aes-128-cbc(key), base64.
        var form = NeteaseCrypto.Weapi("""{"csrf_token":""}""", "cloudie2026ABCDE");
        Assert.Equal("a9Xhwks2hRWlrGdwjOZ6WswBh/QGhWZicH1tS+os1a33jWKRcp7M92z66ZbqQ2Bg", form["params"]);
    }

    [Fact]
    public void EncSecKey_matches_an_independent_big_integer_computation()
    {
        // (reverse(key) as big-endian integer) ^ 65537 mod N, computed with node's BigInt.
        const string expected =
            "39cc534c32d411a2137556b002a1bee18b62978b958f5aa0ceed893aae125a29" +
            "74127fb4c190e8854a00a45677ea374532ef07ddc396f5344843ae4eea89dc8c" +
            "875e3e33b92f208f9f06066c7d0c04cba68ac862f7fbbaad54d8169e56359cd9" +
            "729e5baec6bfda4d62c82577189f154568560e7c9639a3700834bbd357292aef";
        Assert.Equal(expected, NeteaseCrypto.EncSecKey("cloudie2026ABCDE"));
        Assert.Equal("cloudie2026ABCDE", NeteaseCrypto.DefaultWeapiSecretKey);
        Assert.Equal(256, NeteaseCrypto.EncSecKey(NeteaseCrypto.DefaultWeapiSecretKey).Length);
    }

    [Fact]
    public void Weapi_params_decrypt_back_to_the_payload()
    {
        const string json = """{"ids":"[1]","csrf_token":"abc"}""";
        var form = NeteaseCrypto.Weapi(json);
        var inner = NeteaseCrypto.WeapiDecryptRound(Convert.FromBase64String(form["params"]), NeteaseCrypto.DefaultWeapiSecretKey);
        var plain = NeteaseCrypto.WeapiDecryptRound(Convert.FromBase64String(Encoding.UTF8.GetString(inner)), "0CoJUm6Qyw8W8jud");
        Assert.Equal(json, Encoding.UTF8.GetString(plain));
    }

    [Fact]
    public void Weapi_rejects_a_key_that_is_not_sixteen_characters()
    {
        Assert.Throws<ArgumentException>(() => NeteaseCrypto.Weapi("{}", "short"));
    }
}
