using System.Globalization;
using TOTP.Tests.Common;

namespace TOTP.Tests.Updater;

[Collection(NonParallelCollectionDefinition.NonParallel)]
public sealed class UpdateInstallerViewModelTests
{
    [Fact]
    public async Task RunInstallAsync_WhenInstallationFails_DoesNotExposeExceptionDetails()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var directory = Directory.CreateTempSubdirectory("otp-harbor-updater-test-");
        const string sensitiveMarker = "sensitive-package-name.zip";

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var arguments = new TOTP.Updater.UpdateInstallArguments
            {
                PackagePath = Path.Combine(directory.FullName, sensitiveMarker),
                TargetDirectory = directory.FullName,
                ExecutablePath = Path.Combine(directory.FullName, "nonexistent-otp-harbor.exe"),
                ParentProcessId = 0,
                LogPath = Path.Combine(directory.FullName, "updater.log")
            };
            var viewModel = new TOTP.Updater.UpdateInstallerViewModel(
                new TOTP.Updater.UpdateInstallerService(arguments));

            await viewModel.RunInstallAsync();

            Assert.Equal(TOTP.Updater.UpdaterText.UpdateFailedDetail, viewModel.DetailText);
            Assert.DoesNotContain(sensitiveMarker, viewModel.DetailText, StringComparison.Ordinal);
            Assert.DoesNotContain(
                sensitiveMarker,
                await File.ReadAllTextAsync(arguments.LogPath, TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
            Assert.True(viewModel.IsCloseEnabled);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            directory.Delete(recursive: true);
        }
    }
}
