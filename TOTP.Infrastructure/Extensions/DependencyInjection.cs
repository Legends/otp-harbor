using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TOTP.Core.Common;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.DAL.Services;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;

namespace TOTP.Infrastructure.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity)
    {
        services.TryAddSingleton<IPlatformUnattendedUnlock,
            UnavailablePlatformUnattendedUnlock>();
        ArgumentNullException.ThrowIfNull(fileSecurity);
        services.AddSingleton(fileSecurity);

        var rawVaultPath = configuration[StringsConstants.TokensStorageFilePathConfigKey];
        var resolvedVaultPath = string.IsNullOrWhiteSpace(rawVaultPath)
            ? applicationPaths.VaultFilePath
            : Environment.ExpandEnvironmentVariables(rawVaultPath);
        services.AddSingleton<IAuthorizationEnvelopeStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AuthorizationEnvelopeStore>>();
            var fileSecurity = sp.GetRequiredService<IPlatformFileSecurity>();
            return new AuthorizationEnvelopeStore(
                applicationPaths.AuthorizationEnvelopeFilePath,
                logger,
                fileSecurity);
        });
        services.AddSingleton<IAppPreferencesStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AppPreferencesStore>>();
            var fileSecurity = sp.GetRequiredService<IPlatformFileSecurity>();
            return new AppPreferencesStore(applicationPaths.PreferencesFilePath, logger, fileSecurity);
        });

        services.AddSingleton<ISettingsService, PortableSettingsService>();
        services.AddSingleton<ITotpGenerator, OtpNetTotpGenerator>();
        services.AddSingleton<IAccountTotpService, AccountTotpService>();
        services.AddSingleton<IQrCodeService, QrCodeService>();
        services.AddSingleton<IAccountQrCodeService, AccountQrCodeService>();
        services.AddSingleton<IQrPayloadValidator, QrPayloadValidator>();
        services.AddSingleton<IQrAccountImportService, QrAccountImportService>();
        services.AddSingleton<IAccountImportService, AccountImportService>();
        services.AddSingleton<IStartupDiagnostics, StartupDiagnostics>();
        services.AddSingleton<ISupportDiagnosticsService, SupportDiagnosticsService>();
        services.AddSingleton<ISignedAppcastVerifier, SignedAppcastVerifier>();
        services.AddSingleton<ISignedPayloadVerifier, SignedPayloadVerifier>();
        services.AddSingleton<IApplicationVersionProvider>(
            new AssemblyApplicationVersionProvider(
                Assembly.GetEntryAssembly() ?? typeof(DependencyInjection).Assembly));
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<IPortableUpdateService, PortableUpdateService>();
        services.AddSingleton<IUpdateInstallerLauncher, UnavailableUpdateInstallerLauncher>();

        // 1. Master Password & Security Context
        services.AddSingleton<ISecurityContext, SecurityContext>();
        services.AddTransient<IMasterPasswordService, MasterPasswordService>();
        services.AddSingleton<IPasswordValidationService, PasswordValidationService>();
        services.AddSingleton<IAuthorizationEnvelopeActivator, AuthorizationEnvelopeActivator>();
        services.AddSingleton<IAuthorizationEnvelopePasswordLifecycle, AuthorizationEnvelopePasswordLifecycle>();
        services.AddSingleton<IAuthorizationEnvelopeSession, AuthorizationEnvelopeSession>();

        // 2. The Vault & DAL logic
        services.AddSingleton<VaultService>();
        services.AddSingleton<IVaultService>(sp => sp.GetRequiredService<VaultService>());
        services.AddSingleton<IVaultKeyVerifier>(sp => sp.GetRequiredService<VaultService>());
        services.AddSingleton<IStoredVaultKeyVerifier>(sp => new StoredVaultKeyVerifier(
            resolvedVaultPath,
            sp.GetRequiredService<IVaultKeyVerifier>(),
            sp.GetRequiredService<ILogger<StoredVaultKeyVerifier>>(),
            sp.GetRequiredService<IPlatformFileSecurity>()));
        services.AddSingleton<IExportService, ExportService>();

        services.AddSingleton<IAccountDAL>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AccountDAL>>();
            var vault = sp.GetRequiredService<IVaultService>();
            var fileSecurity = sp.GetRequiredService<IPlatformFileSecurity>();
            return new AccountDAL(logger, vault, resolvedVaultPath, fileSecurity);
        });

        // 3. Authorization Logic (The bridge)
        services.AddSingleton<IAuthorizationService, PortableAuthorizationService>();
        services.AddSingleton<IPlatformQuickUnlockEnrollment, PlatformQuickUnlockEnrollment>();
        services.AddSingleton<IPlatformUnattendedUnlockEnrollment,
            PlatformUnattendedUnlockEnrollment>();
        services.AddSingleton<AuthorizationState>();
        services.AddSingleton<IAccountManager, AccountManager>();

        return services;
    }
}
