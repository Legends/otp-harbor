using Avalonia.Media;
using System.Windows.Input;
using TOTP.Core.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class AccountGroupListItemViewModel(
    AccountGroup group,
    int accountCount,
    bool isSelected,
    ICommand selectCommand,
    ICommand editCommand,
    ICommand deleteCommand)
{
    public AccountGroup Group { get; } = group;
    public Guid Id => Group.Id;
    public string Name => Group.Name;
    public int AccountCount { get; } = accountCount;
    public bool IsSelected { get; } = isSelected;
    private Color StrongColor => Color.Parse(Group.Color);
    public IBrush Background => new SolidColorBrush(Color.FromArgb(
        52,
        StrongColor.R,
        StrongColor.G,
        StrongColor.B));
    public IBrush Foreground => new SolidColorBrush(StrongColor);
    public ICommand SelectCommand { get; } = selectCommand;
    public ICommand EditCommand { get; } = editCommand;
    public ICommand DeleteCommand { get; } = deleteCommand;
}
