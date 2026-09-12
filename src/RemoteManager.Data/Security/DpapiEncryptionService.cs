using System.Security.Cryptography;
using System.Text;
using RemoteManager.Core.Interfaces;

namespace RemoteManager.Data.Security;

public class DpapiEncryptionService : IEncryptionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RemoteManager::Entropy::v1");

    internal Func<byte[], byte[]?, DataProtectionScope, byte[]> ProtectFunc { get; set; } = ProtectedData.Protect;
    internal Func<byte[], byte[]?, DataProtectionScope, byte[]> UnprotectFunc { get; set; } = ProtectedData.Unprotect;

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectFunc(
                plainBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to encrypt data using DPAPI.", ex);
        }
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        try
        {
            var cipherBytes = Convert.FromBase64String(cipherText);
            var decryptedBytes = UnprotectFunc(
                cipherBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to decrypt data using DPAPI.", ex);
        }
    }
}
