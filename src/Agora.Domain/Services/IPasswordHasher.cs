namespace Agora.Domain.Services;

/// <summary>Hashes and verifies account passwords (salted, one-way).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
