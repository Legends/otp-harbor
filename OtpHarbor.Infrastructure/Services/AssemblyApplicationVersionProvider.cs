using System.Reflection;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class AssemblyApplicationVersionProvider(Assembly assembly) : IApplicationVersionProvider
{
    private readonly Assembly _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));

    public Version CurrentVersion
    {
        get
        {
            var fileVersion = _assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            return Version.TryParse(fileVersion, out var parsed)
                ? parsed
                : _assembly.GetName().Version ?? new Version();
        }
    }
}
