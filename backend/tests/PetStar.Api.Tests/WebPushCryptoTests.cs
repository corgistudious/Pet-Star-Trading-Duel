using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Tests;

public class WebPushCryptoTests
{
    private static byte[] B(string s) => WebPushCrypto.FromB64Url(s);

    /// <summary>RFC 8291 Appendix A: encrypting with the published keys and salt must give the published message byte for byte.</summary>
    [Fact]
    public void Matches_rfc8291_appendix_a_vector()
    {
        var plaintext = Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon");
        var asPub = B("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var asPriv = B("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        var uaPub = B("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var salt = B("DGv6ra1nlYgDCS1FRnbzlw");
        var auth = B("BTBZMqHH6r4Tts7J_aSIgg");
        using var asKey = ECDiffieHellman.Create(WebPushCrypto.Params(asPub, asPriv));
        var body = WebPushCrypto.Encrypt(plaintext, uaPub, auth, asKey, salt);
        Assert.Equal("DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebPushCrypto.B64Url(body));
    }

    [Fact]
    public void Random_messages_decrypt_like_a_browser_would()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var uaPub = WebPushCrypto.PublicBytes(ua.ExportParameters(false));
        var auth = RandomNumberGenerator.GetBytes(16);
        var msg = Encoding.UTF8.GetBytes("{\"title\":\"Pet Star\",\"body\":\"Xin chào ✨\"}");
        var a = WebPushCrypto.Encrypt(msg, uaPub, auth);
        var b = WebPushCrypto.Encrypt(msg, uaPub, auth);
        Assert.NotEqual(a, b); // fresh salt + ephemeral key per message
        Assert.Equal(msg, PushTestDecryptor.Decrypt(a, ua, uaPub, auth));
    }

    [Fact]
    public void Vapid_jwt_is_es256_signed_for_the_push_origin()
    {
        var (pub, priv) = WebPushCrypto.GenerateVapidKeys();
        using var key = ECDsa.Create(WebPushCrypto.Params(B(pub), B(priv)));
        var header = WebPushCrypto.VapidAuthorization("https://fcm.googleapis.com/fcm/send/abc:def", "mailto:ops@example.com", key, pub, DateTimeOffset.UtcNow);
        Assert.StartsWith("vapid t=", header);
        Assert.EndsWith(", k=" + pub, header);
        var jwt = header[8..header.IndexOf(',')].Split('.');
        using var verify = ECDsa.Create(WebPushCrypto.Params(B(pub)));
        Assert.True(verify.VerifyData(Encoding.ASCII.GetBytes(jwt[0] + "." + jwt[1]), B(jwt[2]), HashAlgorithmName.SHA256));
        var claims = JsonDocument.Parse(B(jwt[1])).RootElement;
        Assert.Equal("https://fcm.googleapis.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:ops@example.com", claims.GetProperty("sub").GetString());
        Assert.InRange(claims.GetProperty("exp").GetInt64() - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 3600, 24 * 3600);
    }
}

/// <summary>Receiver side of RFC 8291 (what the browser does), used only to verify our sender.</summary>
public static class PushTestDecryptor
{
    public static byte[] Decrypt(byte[] body, ECDiffieHellman ua, byte[] uaPub, byte[] auth)
    {
        var salt = body[..16];
        int idlen = body[20];
        var asPub = body[21..(21 + idlen)];
        var ct = body[(21 + idlen)..];
        using var asKey = ECDiffieHellman.Create(WebPushCrypto.Params(asPub));
        var ecdh = ua.DeriveRawSecretAgreement(asKey.PublicKey);
        byte[] info = [.. Encoding.ASCII.GetBytes("WebPush: info"), 0x00, .. uaPub, .. asPub];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, auth, info);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, [.. Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm"), 0x00]);
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, [.. Encoding.ASCII.GetBytes("Content-Encoding: nonce"), 0x00]);
        var plain = new byte[ct.Length - 16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Decrypt(nonce, ct[..^16], ct[^16..], plain);
        int end = Array.LastIndexOf(plain, (byte)0x02);
        return plain[..end];
    }
}
