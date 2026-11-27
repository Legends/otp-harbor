using TOTP.Avalonia.Mobile.Localization;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Theory]
    [InlineData("en", "Unlock")]
    [InlineData("de", "Entsperren")]
    [InlineData("fr", "Déverrouiller")]
    [InlineData("es", "Desbloquear")]
    public void BackupPasswordAction_UsesShortLocalizedUnlockLabel(string culture, string expected)
    {
        var context = CreateContext(isConfigured: true, cultureName: culture);
        using var sut = context.Sut;
        Assert.Equal(expected, sut.ImportBackupUnlockText);
        Assert.Equal(context.Strings.Get(MobileStringKeys.Unlock), sut.ImportBackupUnlockText);
    }
}
