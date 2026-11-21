using System.Globalization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Shared.About;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class AboutViewModelTests
{
    [Fact]
    public async Task PendingPlatformHandoff_DisablesOtherActionsAndRestoresThemAfterFailure()
    {
        var handoff = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var model = new AboutViewModel(key => key, () => "1.0", _ => { calls++; return handoff.Task; });
        model.Entries[1].Command!.Execute(null);
        Assert.All(model.Entries.Skip(1), entry => Assert.False(entry.Command!.CanExecute(null)));
        Assert.False(model.CreditsCommand.CanExecute(null));
        model.Entries[3].Command!.Execute(null);
        Assert.Equal(1, calls);
        handoff.SetResult(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!model.Entries[1].Command!.CanExecute(null)) await Task.Delay(5, timeout.Token);
        Assert.True(model.HasStatus);
        Assert.All(model.Entries.Skip(1), entry => Assert.True(entry.Command!.CanExecute(null)));
        Assert.True(model.CreditsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void About_UsesInstalledVersionAndExactPlatformLinks_AndSharesOnlyThePublicMobileLink(bool mobile)
    {
        var strings = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));
        var opened = new List<Uri>();
        string? shareTitle = null, shareBody = null;
        var model = new AboutViewModel(strings.Get, () => "3.2.65535.0", uri =>
        { opened.Add(uri); return Task.FromResult(true); }, mobile, (title, body) =>
        { shareTitle = title; shareBody = body; return Task.FromResult(true); });
        Assert.Equal(mobile ? 5 : 4, model.Entries.Count);
        Assert.Same(model.Entries[0], model.VersionEntry);
        Assert.Same(model.Entries[1], model.RepositoryEntry);
        Assert.Same(model.Entries[2], model.LicenseEntry);
        Assert.Same(model.Entries[3], model.WebsiteEntry);
        Assert.Equal("3.2.65535.0", model.Entries[0].Value);
        Assert.Equal("GPL-3.0-only", model.Entries[2].Value);
        Assert.Equal(new Uri(mobile ? "https://legends.github.io/otp-harbor/android/" : "https://legends.github.io/otp-harbor/"), model.WebsiteUri);
        foreach (var entry in model.Entries) entry.Command?.Execute(null);
        Assert.Equal(new[] { AboutViewModel.RepositoryUri, AboutViewModel.LicenseUri, model.WebsiteUri }, opened);
        if (mobile)
        {
            Assert.Equal(strings.Get(AboutStringKeys.ShareTitle), shareTitle);
            Assert.Equal(string.Format(strings.Get(AboutStringKeys.ShareMessage), model.WebsiteUri.AbsoluteUri), shareBody);
        }
        else { Assert.Null(shareTitle); Assert.Null(shareBody); }
        Assert.False(model.HasStatus);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("de", true)]
    [InlineData("fr", false)]
    [InlineData("es", true)]
    public void FailedPlatformActions_ShowCompleteLocalizedFeedbackAndRemainRecoverable(string culture, bool throws)
    {
        var strings = new MobileStringCatalog(CultureInfo.GetCultureInfo(culture));
        var succeeds = false;
        var model = new AboutViewModel(strings.Get, () => "1.0", _ =>
            succeeds ? Task.FromResult(true) : throws ? Task.FromException<bool>(new InvalidOperationException()) : Task.FromResult(false));
        var command = model.Entries[1].Command!;
        command.Execute(null);
        Assert.Equal(strings.Get(AboutStringKeys.ActionFailed), model.Status);
        Assert.True(model.HasStatus);
        Assert.True(command.CanExecute(null));
        succeeds = true;
        command.Execute(null);
        Assert.False(model.HasStatus);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void ShareFailureAndLanguageChanges_UseCurrentLocaleWithoutChangingVersion(string culture)
    {
        var strings = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));
        var model = new AboutViewModel(strings.Get, () => "0.1.0-dev (1)", _ => Task.FromResult(true), true,
            (_, _) => Task.FromResult(false));
        strings.ApplyCulture(culture);
        var refreshed = 0;
        model.Entries[0].PropertyChanged += (_, _) => refreshed++;
        model.RefreshLanguage();
        model.Entries[^1].Command!.Execute(null);
        Assert.Equal(strings.Get(AboutStringKeys.Share), model.Entries[^1].Title);
        Assert.Equal(strings.Get(AboutStringKeys.ActionFailed), model.Status);
        Assert.Equal(strings.Get(AboutStringKeys.Version), model.Entries[0].Title);
        Assert.Equal("0.1.0-dev (1)", model.Entries[0].Value);
        Assert.True(refreshed > 0);
    }

    [Fact]
    public void UnavailableVersion_DoesNotPreventAboutLinks()
    {
        var model = new AboutViewModel(key => key, () => throw new InvalidOperationException(), _ => Task.FromResult(true));
        Assert.Equal("—", model.Entries[0].Value);
        Assert.All(model.Entries.Skip(1), entry => Assert.True(entry.Command!.CanExecute(null)));
    }

    [Fact]
    public void Credits_OpensExistingProjectNoticesAndRecoversFromFailedHandoff()
    {
        var strings = new MobileStringCatalog(CultureInfo.GetCultureInfo("de"));
        var opened = new List<Uri>();
        var succeeds = false;
        var model = new AboutViewModel(strings.Get, () => "3.0.3.65535", uri =>
        { opened.Add(uri); return Task.FromResult(succeeds); });
        model.CreditsCommand.Execute(null);
        Assert.Equal(new Uri("https://github.com/Legends/otp-harbor/blob/master/THIRD_PARTY_NOTICES.md"), Assert.Single(opened));
        Assert.Equal(strings.Get(AboutStringKeys.ActionFailed), model.Status);
        succeeds = true;
        model.CreditsCommand.Execute(null);
        Assert.False(model.HasStatus);
        Assert.True(model.RepositoryEntry.Command!.CanExecute(null));
    }
}
