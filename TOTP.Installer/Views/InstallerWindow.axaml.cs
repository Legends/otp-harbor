using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TOTP.Installer.Presentation;

namespace TOTP.Installer.Views;

internal sealed partial class InstallerWindow : Window
{
    private InstallerViewModel? _viewModel;
    private bool _allowClose;

    public InstallerWindow()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (_viewModel is null || _allowClose) return;
            args.Cancel = true;
            _viewModel.RequestClose();
        };
    }

    internal void Attach(InstallerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.BrowseRequested += OnBrowseRequested;
    }

    internal void AllowClose() => _allowClose = true;

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Source is Visual visual && visual.GetVisualAncestors().OfType<Button>().Any()) return;
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;

    private async void OnBrowseRequested(object? sender, EventArgs args)
    {
        if (_viewModel is null) return;
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _viewModel.Text.Location,
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_viewModel.InstallFolder)
        });
        if (result.Count == 1) _viewModel.InstallFolder = result[0].Path.LocalPath;
    }
}
