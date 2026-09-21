using Avalonia.Controls;
using Avalonia.Input;

namespace TOTP.Avalonia.Desktop.Controls;

public sealed class TypeAheadComboBox : ComboBox
{
    private const long PrefixTimeoutMilliseconds = 850;

    private string _typedPrefix = string.Empty;
    private long _lastKeyTimestamp;

    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (TrySelectByTypedPrefix(e))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private bool TrySelectByTypedPrefix(KeyEventArgs e)
    {
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)
            || e.KeySymbol is not { Length: 1 } keySymbol
            || char.IsControl(keySymbol[0]))
        {
            return false;
        }

        var now = Environment.TickCount64;
        _typedPrefix = now - _lastKeyTimestamp <= PrefixTimeoutMilliseconds
            ? _typedPrefix + keySymbol
            : keySymbol;
        _lastKeyTimestamp = now;

        var match = FindMatch(_typedPrefix);
        if (match is null && _typedPrefix.Length > 1)
        {
            _typedPrefix = keySymbol;
            match = FindMatch(_typedPrefix);
        }

        if (match is null)
            return false;

        SelectedItem = match;
        return true;
    }

    private object? FindMatch(string prefix) =>
        Items.FirstOrDefault(item =>
            item?.ToString()?.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase) == true);
}
