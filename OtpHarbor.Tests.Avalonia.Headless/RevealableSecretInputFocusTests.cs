using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class RevealableSecretInputFocusTests
{
    [AvaloniaTheory]
    [InlineData(360, false)]
    [InlineData(720, false)]
    [InlineData(360, true)]
    [InlineData(720, true)]
    public void TabFromRevealButtonToConfirmation_KeepsCenteredFormAndTextInPlace(int width, bool dark)
    {
        var password = new RevealableSecretInput { Text = "synthetic password" };
        var confirmation = new RevealableSecretInput { PlaceholderText = "Synthetic confirmation" };
        var form = new StackPanel
        {
            Width = 280, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { password, confirmation, new Button { Content = "Synthetic submit" } }
        };
        var window = new Window
        {
            Width = width, Height = 540, Content = form,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var passwordText = Assert.Single(password.GetVisualDescendants().OfType<TextBox>());
            var passwordEye = Assert.Single(password.GetVisualDescendants().OfType<Button>());
            var confirmationText = Assert.Single(confirmation.GetVisualDescendants().OfType<TextBox>());
            var presenter = Assert.Single(confirmationText.GetVisualDescendants().OfType<TextPresenter>());
            passwordText.Focus(NavigationMethod.Pointer);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.UpdateLayout();
            Assert.True(passwordEye.IsFocused);
            var formBefore = BoundsInWindow(form, window);
            var passwordBefore = BoundsInWindow(password, window);
            var confirmationBefore = BoundsInWindow(confirmation, window);
            var textBefore = BoundsInWindow(presenter, window);

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.UpdateLayout();

            Assert.True(confirmationText.IsFocused);
            Assert.False(passwordEye.IsFocused);
            Assert.Equal(formBefore, BoundsInWindow(form, window));
            Assert.Equal(passwordBefore, BoundsInWindow(password, window));
            Assert.Equal(confirmationBefore, BoundsInWindow(confirmation, window));
            Assert.Equal(textBefore, BoundsInWindow(presenter, window));
            Assert.False(password.IsRevealed);
            Assert.False(confirmation.IsRevealed);

            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            window.UpdateLayout();
            Assert.True(passwordEye.IsFocused);
            Assert.Equal(formBefore, BoundsInWindow(form, window));
            Assert.Equal(confirmationBefore, BoundsInWindow(confirmation, window));
        }
        finally { window.Close(); }
    }

    private static Rect BoundsInWindow(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
}
