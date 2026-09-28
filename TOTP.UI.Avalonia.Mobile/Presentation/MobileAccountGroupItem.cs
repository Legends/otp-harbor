using System.Windows.Input;
using Avalonia.Media;
using TOTP.Core.Models;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed class MobileAccountGroupItem(
    AccountGroup group,
    int accountCount,
    bool isSelected,
    ICommand selectCommand,
    ICommand editCommand)
{
    public AccountGroup Group { get; } = group;
    public Guid Id => Group.Id;
    public string Name => Group.Name;
    public int AccountCount { get; } = accountCount;
    public bool IsSelected { get; } = isSelected;
    public ICommand SelectCommand { get; } = selectCommand;
    public ICommand EditCommand { get; } = editCommand;

    private Color StrongColor => Color.Parse(Group.Color);

    public IBrush Background => new SolidColorBrush(Color.FromArgb(
        52,
        StrongColor.R,
        StrongColor.G,
        StrongColor.B));

    public IBrush Foreground => new SolidColorBrush(StrongColor);
}
