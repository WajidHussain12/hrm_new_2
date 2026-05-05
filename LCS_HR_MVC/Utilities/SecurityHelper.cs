using System;
using System.Security.Cryptography;
using System.Text;

namespace LCS_HR_MVC.Utilities
{
    public static class SecurityHelper
    {
        public static string HashPassword(string password)
        {
            // Migrated from legacy LCS.cs to match existing hashes
            byte[] salt = Encoding.UTF8.GetBytes("FixedSaltValue@FSV110#");

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA1))
            {
                byte[] hash = pbkdf2.GetBytes(32);
                byte[] hashBytes = new byte[salt.Length + hash.Length];

                Buffer.BlockCopy(salt, 0, hashBytes, 0, salt.Length);
                Buffer.BlockCopy(hash, 0, hashBytes, salt.Length, hash.Length);

                return Convert.ToBase64String(hashBytes);
            }
        }

        public static bool VerifyPassword(string enteredPassword, string storedHash)
        {
            byte[] hashBytes = Convert.FromBase64String(storedHash);

            byte[] salt = new byte[22]; // "FixedSaltValue@FSV110#".Length is 22
            Buffer.BlockCopy(hashBytes, 0, salt, 0, salt.Length);

            using (var pbkdf2 = new Rfc2898DeriveBytes(enteredPassword, salt, 100000, HashAlgorithmName.SHA1))
            {
                byte[] hash = pbkdf2.GetBytes(32);

                for (int i = 0; i < hash.Length; i++)
                {
                    if (hashBytes[salt.Length + i] != hash[i])
                        return false;
                }
            }

            return true;
        }
    }
}
