namespace core.Application.Common.Interfaces;

/// <summary>Argon2id-based password hashing contract.</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plain-text password using Argon2id.</summary>
    string Hash(string password);

    /// <summary>Verifies a plain-text password against an Argon2id hash.</summary>
    bool Verify(string password, string hash);
}
