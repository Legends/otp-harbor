using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using FluentResults;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Storage;

public sealed class StorageLocationsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IApplicationStorageLocations _locations;
    private readonly IAsyncClipboardService _clipboard;
    private readonly IPlatformFolderLauncher? _folders;
    private readonly Func<string, string> _localize;
    private bool _busy;
    private bool _disposed;
    private StorageLocationViewModel? _statusItem;
    private string? _statusKey;
    private NotificationSeverity _statusSeverity;

    public StorageLocationsViewModel(
        IApplicationStorageLocations locations,
        IAsyncClipboardService clipboard,
        Func<string, string> localize,
        IPlatformFolderLauncher? folders = null)
    {
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _localize = localize ?? throw new ArgumentNullException(nameof(localize));
        _folders = folders;
        _locations.Changed += LocationsChanged;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<StorageLocationViewModel> Locations { get; private set; } = [];

    public void Refresh()
    {
        if (_disposed) return;
        _statusItem = null;
        _statusKey = null;
        Locations = _locations.GetLocations()
            .Where(location => location.Kind == ApplicationStorageLocationKind.DataDirectory)
            .Select(location => new StorageLocationViewModel(this, location))
            .ToArray();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Locations)));
    }

    public void RefreshLanguage()
    {
        if (_disposed) return;
        foreach (var location in Locations) location.NotifyChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _locations.Changed -= LocationsChanged;
        NotifyCommands();
    }

    private void LocationsChanged(object? sender, EventArgs args) => Refresh();

    private void NotifyCommands()
    {
        foreach (var location in Locations) location.NotifyCommands();
    }

    private async Task RunActionAsync(
        StorageLocationViewModel item,
        Func<Task<Result>> action,
        string successKey,
        string failureKey)
    {
        if (_busy || _disposed || !Locations.Contains(item)) return;
        _busy = true;
        _statusKey = null;
        _statusItem?.NotifyChanged();
        _statusItem = item;
        NotifyCommands();
        var succeeded = false;
        try
        {
            succeeded = (await action()).IsSuccess;
        }
        catch (Exception)
        {
            // Clipboard and OS-launch failures are recoverable presentation boundaries.
        }
        finally
        {
            _busy = false;
            if (!_disposed && Locations.Contains(item))
            {
                _statusKey = succeeded ? successKey : failureKey;
                _statusSeverity = succeeded ? NotificationSeverity.Success : NotificationSeverity.Error;
                item.NotifyChanged();
            }
            NotifyCommands();
        }
    }

    public sealed class StorageLocationViewModel : INotifyPropertyChanged
    {
        private readonly StorageLocationsViewModel _owner;
        private readonly LocationCommand _copy;
        private readonly LocationCommand _open;

        internal StorageLocationViewModel(StorageLocationsViewModel owner, ApplicationStorageLocation location)
        {
            _owner = owner;
            Location = location;
            _copy = new LocationCommand(CopyPathAsync,
                () => !_owner._disposed && !_owner._busy
                    && (_owner._clipboard.Capabilities & ClipboardCapabilities.WriteText) != 0);
            _open = new LocationCommand(OpenFolderAsync,
                () => !_owner._disposed && !_owner._busy && CanOpenFolder);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ApplicationStorageLocation Location { get; }
        public string Path => Location.Path;
        public string Label => _owner._localize(StorageLocationStringKeys.ForKind(Location.Kind));
        public string CopyText => _owner._localize(StorageLocationStringKeys.CopyPath);
        public string OpenText => _owner._localize(StorageLocationStringKeys.OpenFolder);
        public string CopyAccessibleName => string.Format(CultureInfo.CurrentCulture,
            _owner._localize(StorageLocationStringKeys.CopyLabelFormat), Label);
        public string OpenAccessibleName => string.Format(CultureInfo.CurrentCulture,
            _owner._localize(StorageLocationStringKeys.OpenLabelFormat), Label);
        public bool CanOpenFolder => _owner._folders is not null && !string.IsNullOrEmpty(Location.FolderPath);
        public ICommand CopyCommand => _copy;
        public ICommand OpenFolderCommand => _open;
        public bool HasStatus => ReferenceEquals(_owner._statusItem, this) && _owner._statusKey is not null;
        public string StatusText => HasStatus ? _owner._localize(_owner._statusKey!) : string.Empty;
        public NotificationSeverity StatusSeverity => _owner._statusSeverity;

        public Task CopyPathAsync() => _owner.RunActionAsync(this,
            () => _owner._clipboard.CopyAsync(Path),
            StorageLocationStringKeys.CopySucceeded, StorageLocationStringKeys.CopyFailed);

        public Task OpenFolderAsync()
        {
            if (!CanOpenFolder) return Task.CompletedTask;
            return _owner.RunActionAsync(this,
                () => _owner._folders!.OpenFolderAsync(Location.FolderPath!),
                StorageLocationStringKeys.FolderOpened, StorageLocationStringKeys.FolderOpenFailed);
        }

        internal void NotifyCommands()
        {
            _copy.NotifyCanExecuteChanged();
            _open.NotifyCanExecuteChanged();
        }

        internal void NotifyChanged()
        {
            foreach (var property in new[]
                     {
                         nameof(Label), nameof(CopyText), nameof(OpenText), nameof(CopyAccessibleName),
                         nameof(OpenAccessibleName), nameof(HasStatus), nameof(StatusText), nameof(StatusSeverity)
                     })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    private sealed class LocationCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter)) await execute();
        }
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
