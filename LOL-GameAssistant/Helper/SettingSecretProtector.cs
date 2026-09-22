using System.Security.Cryptography;
using System.Text;

namespace LOL_GameAssistant.Helper;

/// <summary>
/// 仅用于当前 Windows 用户下的本地设置密钥保护。
/// </summary>
public static class SettingSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LOL-GameAssistant.settings.v1");

    public static string Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        byte[] plain = Encoding.UTF8.GetBytes(value.Trim());
        byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    public static string Unprotect(string? encryptedValue)
    {
        if (string.IsNullOrWhiteSpace(encryptedValue)) return "";

        try
        {
            byte[] cipher = Convert.FromBase64String(encryptedValue);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return "";
        }
        catch (FormatException)
        {
            return "";
        }
    }
}