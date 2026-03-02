using System.Security.Cryptography;

namespace MindMap.Backend.Services;

public class PasswordService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public (byte[] Hash, byte[] Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (hash, salt);
    }

    public bool VerifyPassword(string password, byte[] storedHash, byte[] storedSalt)
    {
        if (storedHash.Length == 0 || storedSalt.Length == 0)
        {
            return false;
        }

        var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, storedSalt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }
}
