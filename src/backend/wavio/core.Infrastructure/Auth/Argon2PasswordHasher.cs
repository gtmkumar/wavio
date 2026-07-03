using System.Security.Cryptography;
using System.Text;
using core.Application.Common.Interfaces;
using Konscious.Security.Cryptography;

namespace core.Infrastructure.Auth;

/// <summary>
/// Argon2id password hasher. Parameters are OWASP-aligned minimums.
/// Format stored: "v1$saltBase64$hashBase64"
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int DegreeOfParallelism = 1;
    private const int Iterations = 2;
    private const int MemorySize = 65536; // 64 MB
    private const int HashLength = 32;
    private const int SaltLength = 16;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = ComputeHash(Encoding.UTF8.GetBytes(password), salt);

        return $"v1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;

        var parts = hash.Split('$');
        if (parts.Length != 3 || parts[0] != "v1")
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var storedHash = Convert.FromBase64String(parts[2]);
            var computedHash = ComputeHash(Encoding.UTF8.GetBytes(password), salt);
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ComputeHash(byte[] password, byte[] salt)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            Iterations = Iterations,
            MemorySize = MemorySize
        };
        return argon2.GetBytes(HashLength);
    }
}
