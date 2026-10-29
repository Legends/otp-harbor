using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Reflection;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.DAL.Services;
using TOTP.Infrastructure.Extensions;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;
using TOTP.Platform.Windows;

namespace TOTP.Tests.Infrastructure.Extensions;

public sealed class DependencyInjectionPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"totp-path-di-tests-{Guid.NewGuid():N}");

    [Fact]
    public void AddInfrastructure_WhenStorageOverridesAreMissing_UsesPlatformDefaults()
    {
        var paths = new WindowsApplicationPaths(_root, _root);
        using var provider = BuildProvider(new ConfigurationBuilder().Build(), paths);

        var accountDal = Assert.IsType<AccountDAL>(provider.GetRequiredService<IAccountDAL>());
        var envelopeStore = Assert.IsType<AuthorizationEnvelopeStore>(
            provider.GetRequiredService<IAuthorizationEnvelopeStore>());
        var preferencesStore = Assert.IsType<AppPreferencesStore>(
            provider.GetRequiredService<IAppPreferencesStore>());
        var vaultService = provider.GetRequiredService<IVaultService>();
        var vaultKeyVerifier = provider.GetRequiredService<IVaultKeyVerifier>();
        var storedVaultVerifier = Assert.IsType<StoredVaultKeyVerifier>(
            provider.GetRequiredService<IStoredVaultKeyVerifier>());
        Assert.IsType<AuthorizationEnvelopeActivator>(
            provider.GetRequiredService<IAuthorizationEnvelopeActivator>());
        Assert.IsType<AuthorizationEnvelopePasswordLifecycle>(
            provider.GetRequiredService<IAuthorizationEnvelopePasswordLifecycle>());
        Assert.IsType<AuthorizationEnvelopeSession>(
            provider.GetRequiredService<IAuthorizationEnvelopeSession>());
        Assert.IsType<PortableSettingsService>(provider.GetRequiredService<ISettingsService>());
        Assert.IsType<PortableAuthorizationService>(provider.GetRequiredService<IAuthorizationService>());
        Assert.IsType<PlatformQuickUnlockEnrollment>(
            provider.GetRequiredService<IPlatformQuickUnlockEnrollment>());
        Assert.IsType<AccountManager>(provider.GetRequiredService<IAccountManager>());
        Assert.IsType<AccountTotpService>(provider.GetRequiredService<IAccountTotpService>());
        Assert.IsType<AccountListSnapshotService>(
            provider.GetRequiredService<IAccountListSnapshotService>());
        Assert.IsType<AccountQrCodeService>(provider.GetRequiredService<IAccountQrCodeService>());

        Assert.IsType<WindowsFileSecurity>(provider.GetRequiredService<IPlatformFileSecurity>());
        Assert.Same(vaultService, vaultKeyVerifier);
        Assert.Equal(paths.VaultFilePath, ReadPath(accountDal, "_secretsPath"));
        Assert.Equal(paths.VaultFilePath, ReadPath(storedVaultVerifier, "_path"));
        Assert.Equal(paths.AuthorizationEnvelopeFilePath, ReadPath(envelopeStore, "_path"));
        Assert.Equal(paths.PreferencesFilePath, ReadPath(preferencesStore, "_path"));
    }

    [Fact]
    public void AddInfrastructure_WhenVaultOverrideExists_UsesConfiguredVaultPath()
    {
        var paths = new WindowsApplicationPaths(_root, _root);
        var configuredVaultPath = Path.Combine(_root, "custom", "accounts.totp");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:StorageFilePath"] = configuredVaultPath
            })
            .Build();

        using var provider = BuildProvider(configuration, paths);

        Assert.Equal(
            paths.PreferencesFilePath,
            ReadPath(Assert.IsType<AppPreferencesStore>(
                provider.GetRequiredService<IAppPreferencesStore>()), "_path"));
        Assert.Equal(
            configuredVaultPath,
            ReadPath(Assert.IsType<AccountDAL>(provider.GetRequiredService<IAccountDAL>()), "_secretsPath"));
        Assert.Equal(
            configuredVaultPath,
            ReadPath(Assert.IsType<StoredVaultKeyVerifier>(
                provider.GetRequiredService<IStoredVaultKeyVerifier>()), "_path"));
    }

    [Fact]
    public void AddInfrastructure_WhenFileSecurityIsMissing_RejectsRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var paths = new WindowsApplicationPaths(_root, _root);

        var act = () => services.AddInfrastructure(configuration, paths, null!);

        Assert.Throws<ArgumentNullException>(act);
    }

    private static ServiceProvider BuildProvider(
        IConfiguration configuration,
        IPlatformApplicationPaths paths)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IPlatformQuickUnlock>());
        services.AddInfrastructure(configuration, paths, new WindowsFileSecurity());
        return services.BuildServiceProvider();
    }

    private static string ReadPath(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<string>(field!.GetValue(instance));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
