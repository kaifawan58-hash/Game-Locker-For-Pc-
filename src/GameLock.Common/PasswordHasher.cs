using System;
using System.Security.Cryptography;

namespace GameLock.Common
{
    /// <summary>
    /// PBKDF2-SHA256 password hashing. Only the salt + derived hash are ever persisted.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        public const int DefaultIterations = 200_000;

        public static (string hashBase64, string saltBase64, int iterations) CreateHash(string password, int iterations = DefaultIterations)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be empty.", nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] hash = Derive(password, salt, iterations);
            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), iterations);
        }

        public static bool Verify(string password, string hashBase64, string saltBase64, int iterations)
        {
            if (string.IsNullOrEmpty(password)) return false;
            byte[] salt = Convert.FromBase64String(saltBase64);
            byte[] expected = Convert.FromBase64String(hashBase64);
            byte[] actual = Derive(password, salt, iterations);

            // constant-time compare
            if (actual.Length != expected.Length) return false;
            int diff = 0;
            for (int i = 0; i < actual.Length; i++)
                diff |= actual[i] ^ expected[i];
            return diff == 0;
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(HashSizeBytes);
        }
    }
}
