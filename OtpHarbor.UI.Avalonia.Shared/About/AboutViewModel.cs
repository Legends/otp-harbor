using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace TOTP.Avalonia.Shared.About;

public sealed class AboutViewModel : INotifyPropertyChanged
{
    public static readonly Uri RepositoryUri = new("https://github.com/Legends/otp-harbor");
    public static readonly Uri LicenseUri = new("https://github.com/Legends/otp-harbor/blob/master/LICENSE.txt");
    public static readonly Uri CreditsUri = new("https://github.com/Legends/otp-harbor/blob/master/THIRD_PARTY_NOTICES.md");
    public static readonly Uri DesktopWebsiteUri = new("https://legends.github.io/otp-harbor/");
    public static readonly Uri AndroidWebsiteUri = new("https://legends.github.io/otp-harbor/android/");

    public sealed class Entry : INotifyPropertyChanged
    {
        private readonly AboutViewModel _owner;
        private readonly string _titleKey;
        private readonly string? _value;
        internal Entry(AboutViewModel owner, string titleKey, string? value, Controls.SymbolIconKind icon, Func<Task<bool>>? action)
        {
            _owner = owner; _titleKey = titleKey; _value = value; Icon = icon;
            Command = action is null ? null : new ActionCommand(owner, action);
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Title => _owner.Get(_titleKey);
        public string Value => _titleKey == AboutStringKeys.License ? _owner.Get(AboutStringKeys.LicenseName) : _value ?? string.Empty;
        public Controls.SymbolIconKind Icon { get; }
        public ICommand? Command { get; }
        public bool IsAction => Command is not null;
        public bool HasValue => Value.Length > 0;
        internal void Refresh() { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); (Command as ActionCommand)?.Refresh(); }
    }

    private readonly Func<string, string> _localize;
    private bool _busy;
    private bool _failed;
    private readonly ActionCommand _creditsCommand;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string AppName => Get(AboutStringKeys.AppName);
    public string Status => _failed ? Get(AboutStringKeys.ActionFailed) : string.Empty;
    public bool HasStatus => _failed;
    public IReadOnlyList<Entry> Entries { get; }
    public Entry VersionEntry { get; }
    public Entry RepositoryEntry { get; }
    public Entry LicenseEntry { get; }
    public Entry WebsiteEntry { get; }
    public ICommand CreditsCommand => _creditsCommand;
    public Uri WebsiteUri { get; }

    public AboutViewModel(Func<string, string> localize, Func<string> version,
        Func<Uri, Task<bool>> openUri, bool isMobile = false,
        Func<string, string, Task<bool>>? share = null)
    {
        _localize = localize;
        WebsiteUri = isMobile ? AndroidWebsiteUri : DesktopWebsiteUri;
        string installedVersion;
        try { installedVersion = version(); if (string.IsNullOrWhiteSpace(installedVersion)) installedVersion = "—"; }
        catch (Exception) { installedVersion = "—"; }
        VersionEntry = new(this, AboutStringKeys.Version, installedVersion, Controls.SymbolIconKind.Information, null);
        RepositoryEntry = new(this, AboutStringKeys.Repository, null, Controls.SymbolIconKind.ExternalLink, () => openUri(RepositoryUri));
        LicenseEntry = new(this, AboutStringKeys.License, null, Controls.SymbolIconKind.License, () => openUri(LicenseUri));
        WebsiteEntry = new(this, AboutStringKeys.Website, null, Controls.SymbolIconKind.ExternalLink, () => openUri(WebsiteUri));
        _creditsCommand = new(this, () => openUri(CreditsUri));
        var entries = new List<Entry> { VersionEntry, RepositoryEntry, LicenseEntry, WebsiteEntry };
        if (isMobile && share is not null)
            entries.Add(new(this, AboutStringKeys.Share, null, Controls.SymbolIconKind.Share,
                () => share(Get(AboutStringKeys.ShareTitle), string.Format(CultureInfo.CurrentCulture, Get(AboutStringKeys.ShareMessage), WebsiteUri.AbsoluteUri))));
        Entries = entries;
    }

    private string Get(string key) => _localize(key);
    public void RefreshLanguage()
    {
        foreach (var entry in Entries) entry.Refresh();
        _creditsCommand.Refresh();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private async Task RunAsync(Func<Task<bool>> action)
    {
        if (_busy) return;
        _busy = true; _failed = false; RefreshLanguage();
        try { _failed = !await action(); }
        catch (Exception) { _failed = true; }
        finally { _busy = false; RefreshLanguage(); }
    }
    private sealed class ActionCommand(AboutViewModel owner, Func<Task<bool>> action) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => !owner._busy;
        public async void Execute(object? parameter) { if (CanExecute(parameter)) await owner.RunAsync(action); }
        internal void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
