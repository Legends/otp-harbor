using Android.Content;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Platform.Android;

public sealed class AndroidFileSecurity : IPlatformFileSecurity
{
    private const UnixFileMode DirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode NonUserPermissions =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private readonly string[] _allowedRoots;

    public AndroidFileSecurity(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _allowedRoots =
        [
            GetRequiredCanonicalPath(context.FilesDir, "files"),
            GetRequiredCanonicalPath(context.CacheDir, "cache")
        ];
    }

    public void RestrictDirectoryToCurrentUser(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException("The Android application directory was not found.");

        Restrict(directoryPath, DirectoryMode);
    }

    public void RestrictFileToCurrentUser(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The Android application file was not found.", filePath);

        Restrict(filePath, FileMode);
    }

    private void Restrict(string path, UnixFileMode requiredMode)
    {
        var canonicalPath = new Java.IO.File(path).CanonicalPath
            ?? throw new IOException("The Android application path could not be resolved.");
        if (!_allowedRoots.Any(root => IsWithinRoot(canonicalPath, root)))
            throw new UnauthorizedAccessException("Sensitive files must remain in private app storage.");

        File.SetUnixFileMode(canonicalPath, requiredMode);
        var actualMode = File.GetUnixFileMode(canonicalPath);
        if ((actualMode & NonUserPermissions) != 0
            || (actualMode & requiredMode) != requiredMode)
        {
            throw new UnauthorizedAccessException("Private Android file permissions could not be enforced.");
        }
    }

    private static bool IsWithinRoot(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.Ordinal)
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string GetRequiredCanonicalPath(Java.IO.File? directory, string description) =>
        directory?.CanonicalPath is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException($"The Android {description} directory is unavailable.");
}
