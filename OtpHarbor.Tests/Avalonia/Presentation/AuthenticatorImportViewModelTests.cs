using System.Globalization;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class AuthenticatorImportViewModelTests
{
    [Fact]
    public async Task FileFirstFlow_DetectsIndependentlyOfHelpAndWaitsForExplicitReview()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));
        var model = new AuthenticatorImportViewModel(catalog.Get, new Command(), new Command());
        Assert.Null(model.SelectedSource);
        Assert.True(model.CanImportFile);
        Assert.True(model.CanImportQr);
        var waiting = model.WaitForFileReviewAsync(new(23, nonOtpCount: 1) { Format = AccountImportFormat.AegisJson }, "renamed.csv", TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        Assert.Equal(24, model.TotalEntries);
        Assert.Equal(23, model.ValidEntries);
        Assert.Equal(1, model.AttentionEntries);
        Assert.Contains("Aegis", model.DetectedFormatText);
        model.SelectedSource = model.Sources.Single(source => source.Source == AuthenticatorImportSource.BitwardenVault);
        Assert.Contains("Aegis", model.DetectedFormatText);
        model.ReviewFileCommand.Execute(null);
        Assert.True(await waiting);
        Assert.False(model.HasSelectedFile);
    }

    [Fact]
    public async Task FileSelection_CancellationClearsThePendingMetadata()
    {
        var model = new AuthenticatorImportViewModel(key => key, new Command(), new Command());
        using var cancellation = new CancellationTokenSource();
        var waiting = model.WaitForFileReviewAsync(new(1), "selected.json", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        Assert.False(model.HasSelectedFile);
        Assert.Empty(model.SelectedFileName);
    }

    [Fact]
    public async Task Ambiguity_RequiresAChoiceAmongDetectedCandidatesOnly()
    {
        var model = new AuthenticatorImportViewModel(key => key, new Command(), new Command());
        var waiting = model.ChooseDetectedFormatAsync([AccountImportFormat.AegisJson, AccountImportFormat.TwoFasJson], "ambiguous.json", TestContext.Current.CancellationToken);
        Assert.True(model.HasFormatChoice);
        Assert.False(model.UseFormatCommand.CanExecute(null));
        model.UseFormatCommand.Execute(null);
        Assert.False(waiting.IsCompleted);
        model.SelectedFormat = model.FormatOptions.Single(option => option.Format == AccountImportFormat.TwoFasJson);
        model.UseFormatCommand.Execute(null);
        Assert.Equal(AccountImportFormat.TwoFasJson, await waiting);
        Assert.False(model.HasFormatChoice);
        Assert.Empty(model.FormatOptions);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Sources_ProvideLocalizedGuidanceAndOnlySupportedImportMethods(string language)
    {
        var desktop = new AvaloniaStringCatalog();
        var mobile = new MobileStringCatalog(CultureInfo.GetCultureInfo(language));
        foreach (var localize in new Func<string, string>[] { key => desktop.Get(key, CultureInfo.GetCultureInfo(language)), mobile.Get })
        {
            var file = new Command();
            var qr = new Command();
            var model = new AuthenticatorImportViewModel(localize, file, qr);
            foreach (var source in model.Sources)
            {
                model.SelectedSource = source;
                Assert.DoesNotContain("Migration", source.Name);
                Assert.DoesNotContain("Migration", model.Instructions);
                Assert.False(string.IsNullOrWhiteSpace(model.Instructions));
                var guideOnly = source.Source is AuthenticatorImportSource.Microsoft or AuthenticatorImportSource.KeePassXC;
                Assert.True(model.HasImportMethod);
                Assert.True(model.CanImportFile);
                Assert.True(model.CanImportQr);
                Assert.Equal(guideOnly, model.SupportNotice == localize(MigrationStringKeys.GuideOnly));
            }
            Assert.Same(file, model.FileCommand);
            Assert.Same(qr, model.QrCommand);
        }
    }

    [Fact]
    public void LanguageChange_RetainsSourceAndRefreshesGuidanceAndNames()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));
        var model = new AuthenticatorImportViewModel(catalog.Get, new Command(), new Command());
        model.SelectedSource = model.Sources.Single(source => source.Source == AuthenticatorImportSource.Proton);
        var changed = false;
        model.PropertyChanged += (_, _) => changed = true;
        catalog.ApplyCulture("de");
        model.RefreshLanguage();
        Assert.True(changed);
        Assert.Equal(AuthenticatorImportSource.Proton, model.SelectedSource!.Source);
        Assert.Equal("Konten importieren", model.Title);
        Assert.Contains("ohne Passwort", model.Instructions);
    }

    [Fact]
    public void Review_OnlyContainsSupportedMetadataAndExplicitCounts()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("de"));
        var preview = new AccountImportPreview(1, 0, TOTP.Core.Models.ImportConflictStrategy.SkipExisting)
        {
            Entries = [new AccountImportEntryPreview("Demo", "user@example.com", "SHA512", 8, 60)]
        };
        var text = ImportReviewFormatter.Format(catalog.Get, preview, new AccountImportReadSummary(1, nonOtpCount: 2));
        Assert.Contains("Einträge ohne TOTP: 2", text);
        Assert.Contains("SHA512, 8 Ziffern, 60 Sekunden", text);
        Assert.Contains("Ziel: der aktuelle OTP-Harbor-Tresor.", text);
        Assert.DoesNotContain("JBSWY", text);
        Assert.Contains("keine Konten importiert", ImportReviewFormatter.Blocked(catalog.Get,
            new AccountImportReadSummary(1, invalidCount: 2, unsupportedCount: 3)));
    }

    private sealed class Command : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
