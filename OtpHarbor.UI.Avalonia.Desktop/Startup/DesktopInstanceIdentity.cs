using System.Security.Cryptography;
using System.Text;

namespace TOTP.Avalonia.Desktop.Startup;

internal static class DesktopInstanceIdentity
{
    private static readonly string UserScope = CreateUserScope();

    public static string PipeName => $"totp-manager-avalonia-v2-{UserScope}-activation";
    public static string MutexName => $"totp-manager-avalonia-v2-{UserScope}-instance";

    private static string CreateUserScope()
    {
        var bytes = Encoding.UTF8.GetBytes(Environment.UserName);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes))[..16];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
