using System.Security.Cryptography;

namespace EventRegistration.Api.Services;

/// <summary>PBKDF2 password hashing. Format: {iterations}.{salt}.{hash} (base64).</summary>
/// <remarks>
/// Passwords are hashed in Unicode NFC, so "ñ" typed as one character or as "n" plus a combining
/// tilde gives the same hash. Hashes made before that from un-normalized text still verify.
/// </remarks>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password.Normalize(), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.', 3);
        if (parts.Length != 3) return false;
        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var normalized = password.Normalize();
        return Matches(normalized, salt, iterations, expected)
            // A hash made before passwords were normalized, from the text exactly as typed.
            || (normalized != password && Matches(password, salt, iterations, expected));
    }

    private static bool Matches(string password, byte[] salt, int iterations, byte[] expected)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
