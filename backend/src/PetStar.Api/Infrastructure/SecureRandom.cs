using System.Security.Cryptography;
using PetStar.Rules;

namespace PetStar.Api.Infrastructure;

/// <summary>CSPRNG-backed random source for pack openings and battle seeds.</summary>
public sealed class SecureRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);

    public static long NewSeed()
    {
        Span<byte> b = stackalloc byte[8];
        RandomNumberGenerator.Fill(b);
        return BitConverter.ToInt64(b);
    }

    public static string Token(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
