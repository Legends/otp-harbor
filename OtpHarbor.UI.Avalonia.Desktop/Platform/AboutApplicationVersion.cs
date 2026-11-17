using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Platform;

internal static class AboutApplicationVersion
{
    public static string Read(IApplicationVersionProvider fallback)
    {
#if WINDOWS
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception) { /* Unpackaged desktop apps use their assembly file version. */ }
#endif
        return fallback.CurrentVersion.ToString();
    }
}
