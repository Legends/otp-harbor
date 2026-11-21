using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataBackupMarketingView_ShowsImportsAndRecoveryWithoutNativeServices(bool dark)
    {
        var window = new SettingsWindow { Width = 1200, Height = 900 };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        var backups = new LayoutBackups();
        using var model = new NativeFilePickerViewModel(
            DispatchProxy.Create<IAvaloniaFilePicker, UnexpectedBackupService>(),
            DispatchProxy.Create<IExportService, UnexpectedBackupService>(),
            DispatchProxy.Create<IAccountManager, UnexpectedBackupService>(),
            DispatchProxy.Create<IAccountImportService, UnexpectedBackupService>(),
            new LayoutDialogs(), new PasswordValidationService(),
            DispatchProxy.Create<IPlatformFileSecurity, UnexpectedBackupService>(), new TestSettingsService(),
            DispatchProxy.Create<IPlatformFolderLauncher, UnexpectedBackupService>(), localization,
            automaticBackup: backups);
        try
        {
            localization.ApplyCulture("en");
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 3;
            var host = window.FindControl<ContentControl>("ImportExportSettingsHost")!;
            host.Content = model;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            foreach (var name in new[] { "ImportAccountsCard", "BackupsSettingsCard", "AutomaticBackupLocationSection" })
            {
                var control = Named<Control>(host, name);
                Assert.True(control.IsEffectivelyVisible);
                var position = control.TranslatePoint(default, window)!.Value;
                Assert.InRange(position.Y, 0, window.Height);
                Assert.True(position.Y + control.Bounds.Height <= window.Height);
            }
            Assert.False(model.IsAutomaticBackupConfigured);
            Assert.Equal(0, backups.ConfigureCalls);

            var output = Environment.GetEnvironmentVariable("OTP_HARBOR_MARKETING_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var directory = Path.Combine(output, dark ? "dark" : "light");
                Directory.CreateDirectory(directory);
                var frame = window.FindControl<Border>("SettingsWindowFrame")!;
                using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(frame.Bounds.Size, 2), new Vector(192, 192));
                bitmap.Render(frame);
                bitmap.Save(Path.Combine(directory, "data-backup.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }
}
