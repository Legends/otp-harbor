using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class SymbolIconRenderingTests
{
    [AvaloniaTheory]
    [InlineData(SymbolIconKind.Palette)]
    [InlineData(SymbolIconKind.ImageStack)]
    [InlineData(SymbolIconKind.Vault)]
    [InlineData(SymbolIconKind.BackupRestore)]
    [InlineData(SymbolIconKind.Import)]
    [InlineData(SymbolIconKind.Diagnostics)]
    [InlineData(SymbolIconKind.Help)]
    public void SettingsCategoryIcon_HasRenderableGeometry(SymbolIconKind kind)
    {
        var icon = new SymbolIcon { Kind = kind };

        var geometry = Geometry.Parse(icon.IconData);

        Assert.True(geometry.Bounds.Width > 0);
        Assert.True(geometry.Bounds.Height > 0);
    }
}
