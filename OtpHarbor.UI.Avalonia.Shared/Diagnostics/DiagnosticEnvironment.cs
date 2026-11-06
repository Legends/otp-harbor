using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace TOTP.Avalonia.Shared.Diagnostics;

public sealed record DiagnosticDevice(string Manufacturer, string Model, string OperatingSystem);

public sealed record DiagnosticEnvironment(
    DateTimeOffset CollectedUtc, string AppVersion, string Build, string Configuration,
    string Distribution, string OperatingSystem, string Architecture, string Runtime,
    string Display, bool IsDarkTheme, string Language, string? Device, bool? IsPortrait = null);

public interface IDiagnosticEnvironmentProvider
{
    DiagnosticEnvironment Capture();
}

public sealed class DiagnosticEnvironmentProvider(
    Assembly applicationAssembly,
    string distribution,
    Func<TopLevel?> getTopLevel,
    Func<DiagnosticDevice?>? getDevice = null,
    Func<string?>? getApplicationVersion = null,
    TimeProvider? timeProvider = null,
    Func<string?>? getDistribution = null) : IDiagnosticEnvironmentProvider
{
    public DiagnosticEnvironment Capture()
    {
        var topLevel = Read(getTopLevel);
        var device = getDevice is null ? null : Read(getDevice);
        var version = getApplicationVersion is null ? null : Read(getApplicationVersion);
        var channel = getDistribution is null ? distribution : Read(getDistribution);
        var display = topLevel is null ? "—" : FormattableString.Invariant(
            $"{topLevel.ClientSize.Width:0} × {topLevel.ClientSize.Height:0}; {topLevel.RenderScaling:0.##}×");
        return new(
            (timeProvider ?? TimeProvider.System).GetUtcNow(),
            Clean(version ?? applicationAssembly.GetName().Version?.ToString()),
            Clean(applicationAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
            Clean(applicationAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration),
            channel is "store" or "direct" or "github" or "play" or "debug" ? channel : "—",
            Clean(device?.OperatingSystem ?? RuntimeInformation.OSDescription),
            $"{RuntimeInformation.ProcessArchitecture} / {RuntimeInformation.OSArchitecture}",
            $"{RuntimeInformation.FrameworkDescription}; Avalonia {typeof(Application).Assembly.GetName().Version}",
            display, (topLevel?.ActualThemeVariant ?? Application.Current?.ActualThemeVariant) == ThemeVariant.Dark,
            CultureInfo.CurrentUICulture.Name,
            device is null ? null : Clean($"{device.Manufacturer} {device.Model}"),
            topLevel is null ? null : topLevel.ClientSize.Width < topLevel.ClientSize.Height);
    }

    private static T? Read<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch { return null; } // Optional OS/UI metadata must not prevent a support draft.
    }

    private static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "—"
        : new string(value.Where(character => !char.IsControl(character)).Take(160).ToArray());
}

public static class DiagnosticEmailBody
{
    public static string Format(string introduction, string summaryTemplate, string deviceTemplate,
        string darkTheme, string lightTheme, string logLevel, IDiagnosticEnvironmentProvider? provider,
        string? appLanguage = null, string? portrait = null, string? landscape = null)
    {
        if (provider is null) return introduction;
        DiagnosticEnvironment info;
        try { info = provider.Capture(); }
        catch { return introduction; }
        var device = info.Device is null ? string.Empty
            : string.Format(CultureInfo.CurrentUICulture, deviceTemplate, info.Device) + Environment.NewLine;
        var orientation = info.IsPortrait is { } isPortrait ? isPortrait ? portrait : landscape : null;
        var display = info.Display + (orientation is null ? string.Empty : $"; {orientation}");
        return introduction + Environment.NewLine + Environment.NewLine
            + string.Format(CultureInfo.CurrentUICulture, summaryTemplate,
                info.CollectedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
                info.AppVersion, info.Build, info.Configuration, info.Distribution, info.OperatingSystem,
                info.Architecture, info.Runtime, display, info.IsDarkTheme ? darkTheme : lightTheme,
                appLanguage ?? info.Language, logLevel, device);
    }
}
