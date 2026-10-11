using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class SymbolIconTests
{
    [Theory]
    [InlineData(SymbolIconKind.Add)]
    [InlineData(SymbolIconKind.ArrowLeft)]
    [InlineData(SymbolIconKind.BackupRestore)]
    [InlineData(SymbolIconKind.Camera)]
    [InlineData(SymbolIconKind.Close)]
    [InlineData(SymbolIconKind.Conceal)]
    [InlineData(SymbolIconKind.Codes)]
    [InlineData(SymbolIconKind.Copy)]
    [InlineData(SymbolIconKind.Delete)]
    [InlineData(SymbolIconKind.Diagnostics)]
    [InlineData(SymbolIconKind.Edit)]
    [InlineData(SymbolIconKind.Favorite)]
    [InlineData(SymbolIconKind.Folder)]
    [InlineData(SymbolIconKind.FolderAdd)]
    [InlineData(SymbolIconKind.Help)]
    [InlineData(SymbolIconKind.ImageStack)]
    [InlineData(SymbolIconKind.Import)]
    [InlineData(SymbolIconKind.Information)]
    [InlineData(SymbolIconKind.Lock)]
    [InlineData(SymbolIconKind.Palette)]
    [InlineData(SymbolIconKind.QrCode)]
    [InlineData(SymbolIconKind.Reveal)]
    [InlineData(SymbolIconKind.Search)]
    [InlineData(SymbolIconKind.Settings)]
    [InlineData(SymbolIconKind.Sort)]
    [InlineData(SymbolIconKind.Vault)]
    public void Kind_ProvidesScalableVectorGeometry(SymbolIconKind kind)
    {
        var sut = new SymbolIcon { Kind = kind };

        Assert.StartsWith("M", sut.IconData, StringComparison.Ordinal);
        Assert.Contains("z", sut.IconData, StringComparison.OrdinalIgnoreCase);
    }
}
