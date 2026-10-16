using Avalonia.Styling;

namespace TOTP.Avalonia.Desktop.Startup;

public sealed record OnboardingPreviewOptions(
    bool ShowOnStartup,
    ThemeVariant? ThemeOverride = null);
