using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PetStar.Api.Infrastructure;

/// <summary>Web Push message encryption (RFC 8291, "aes128gcm" content coding of RFC 8188) and VAPID
/// authentication (RFC 8292), implemented with the .NET platform crypto only (P-256 ECDH, HKDF/HMAC-SHA256,
/// AES-128-GCM, ES256). Push services only ever see ciphertext.</summary>
public static class WebPushCrypto
{
    public const int RecordSize = 4096;

    public static string B64Url(ReadOnlySpan<byte> b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    public static ECParameters Params(byte[] uncompressedPublic, byte[]? privateD = null)
    {
        if (uncompressedPublic.Length != 65 || uncompressedPublic[0] != 0x04) throw new ArgumentException("P-256 public key must be 65 bytes, uncompressed");
        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uncompressedPublic[1..33], Y = uncompressedPublic[33..65] },
            D = privateD,
        };
    }

    public static byte[] PublicBytes(ECParameters p) => [0x04, .. p.Q.X!, .. p.Q.Y!];

    /// <summary>Encrypts one push message. <paramref name="asKey"/> and <paramref name="salt"/> are only passed by tests
    /// (RFC 8291 Appendix A vector); production uses a fresh ephemeral key and random salt for every message.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] uaPublic, byte[] authSecret, ECDiffieHellman? asKey = null, byte[]? salt = null)
    {
        if (authSecret.Length != 16) throw new ArgumentException("auth secret must be 16 bytes");
        if (plaintext.Length > RecordSize - 17 - 86) throw new ArgumentException("payload too large for one record");
        using var eph = asKey == null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var asK = asKey ?? eph!;
        salt ??= RandomNumberGenerator.GetBytes(16);
        var asPublic = PublicBytes(asK.ExportParameters(false));

        using var ua = ECDiffieHellman.Create(Params(uaPublic));
        byte[] ecdhSecret = asK.DeriveRawSecretAgreement(ua.PublicKey);

        // IKM = HKDF(salt=auth_secret, ikm=ecdh_secret, info="WebPush: info" 0x00 ua_public as_public, L=32)
        byte[] keyInfo = [.. Encoding.ASCII.GetBytes("WebPush: info"), 0x00, .. uaPublic, .. asPublic];
        byte[] ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);
        // CEK / NONCE = HKDF(salt, IKM, "Content-Encoding: aes128gcm|nonce" 0x00)
        byte[] prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        byte[] cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, [.. Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm"), 0x00]);
        byte[] nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, [.. Encoding.ASCII.GetBytes("Content-Encoding: nonce"), 0x00]);

        byte[] record = [.. plaintext, 0x02]; // single (last) record delimiter, no padding
        var cipher = new byte[record.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(nonce, record, cipher, tag);

        var header = new byte[16 + 4 + 1 + asPublic.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), RecordSize);
        header[20] = (byte)asPublic.Length;
        asPublic.CopyTo(header, 21);
        return [.. header, .. cipher, .. tag];
    }

    /// <summary>VAPID: a short-lived ES256 JWT for the push service origin, plus our public key.</summary>
    public static string VapidAuthorization(string endpoint, string subject, ECDsa vapidKey, string vapidPublicB64, DateTimeOffset now)
    {
        var aud = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        string head = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" }));
        string body = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { aud, exp = now.AddHours(12).ToUnixTimeSeconds(), sub = subject }));
        // .NET signs in IEEE P1363 (r||s, 64 bytes) which is exactly the JWS ES256 format
        byte[] sig = vapidKey.SignData(Encoding.ASCII.GetBytes(head + "." + body), HashAlgorithmName.SHA256);
        return $"vapid t={head}.{body}.{B64Url(sig)}, k={vapidPublicB64}";
    }

    public static (string publicKey, string privateKey) GenerateVapidKeys()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = k.ExportParameters(true);
        return (B64Url(PublicBytes(p)), B64Url(p.D!));
    }
}
