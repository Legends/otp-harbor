using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class RevealableSecretInput : TemplatedControl
{
    private const char MaskCharacter = '●';
    private Button? _revealButton;
    private TextBox? _textBox;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<RevealableSecretInput, string>(
            nameof(Text),
            string.Empty,
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<RevealableSecretInput, string>(
            nameof(PlaceholderText),
            "Secret");

    public static readonly StyledProperty<string> AccessibleNameProperty =
        AvaloniaProperty.Register<RevealableSecretInput, string>(
            nameof(AccessibleName),
            "Secret");

    public static readonly StyledProperty<string> HelpTextProperty =
        AvaloniaProperty.Register<RevealableSecretInput, string>(
            nameof(HelpText),
            string.Empty);

    public static readonly StyledProperty<string> RevealButtonAccessibleNameProperty =
        AvaloniaProperty.Register<RevealableSecretInput, string>(
            nameof(RevealButtonAccessibleName),
            string.Empty);

    public static readonly StyledProperty<string> RevealButtonHelpTextProperty =
        AvaloniaProperty.Register<RevealableSecretInput, string>(
            nameof(RevealButtonHelpText),
            string.Empty);

    public static readonly StyledProperty<bool> IsRequiredProperty =
        AvaloniaProperty.Register<RevealableSecretInput, bool>(nameof(IsRequired));

    public static readonly StyledProperty<bool> IsRevealedProperty =
        AvaloniaProperty.Register<RevealableSecretInput, bool>(nameof(IsRevealed));

    static RevealableSecretInput()
    {
        TextProperty.Changed.AddClassHandler<RevealableSecretInput>(
            static (control, _) => control.OnTextChanged());
        IsRevealedProperty.Changed.AddClassHandler<RevealableSecretInput>(
            static (control, _) => control.UpdateDisclosureState());
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value ?? string.Empty);
    }

    public string AccessibleName
    {
        get => GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value ?? string.Empty);
    }

    public string HelpText
    {
        get => GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value ?? string.Empty);
    }

    public string RevealButtonAccessibleName
    {
        get => GetValue(RevealButtonAccessibleNameProperty);
        set => SetValue(RevealButtonAccessibleNameProperty, value ?? string.Empty);
    }

    public string RevealButtonHelpText
    {
        get => GetValue(RevealButtonHelpTextProperty);
        set => SetValue(RevealButtonHelpTextProperty, value ?? string.Empty);
    }

    public bool IsRequired
    {
        get => GetValue(IsRequiredProperty);
        set => SetValue(IsRequiredProperty, value);
    }

    public bool IsRevealed
    {
        get => GetValue(IsRevealedProperty);
        private set => SetValue(IsRevealedProperty, value);
    }

    public void Conceal() => IsRevealed = false;

    public void FocusInput()
    {
        ApplyTemplate();
        _textBox?.Focus();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        DetachRevealHandlers();
        base.OnApplyTemplate(e);

        _textBox = e.NameScope.Find<TextBox>("PART_Input");
        _revealButton = e.NameScope.Find<Button>("PART_RevealButton");
        AttachRevealHandlers();
        UpdateDisclosureState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Conceal();
        DetachRevealHandlers();
        _textBox = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachRevealHandlers()
    {
        if (_revealButton is null) return;
        _revealButton.Click += OnRevealClick;
    }

    private void DetachRevealHandlers()
    {
        if (_revealButton is null) return;

        _revealButton.Click -= OnRevealClick;
        _revealButton = null;
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        IsRevealed = !IsRevealed;
        if (_textBox is null) return;
        _textBox.Focus();
        _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
    }

    private void OnTextChanged()
    {
        if (string.IsNullOrEmpty(Text)) Conceal();
    }

    private void UpdateDisclosureState()
    {
        PseudoClasses.Set(":revealed", IsRevealed);
        if (_textBox is not null)
        {
            _textBox.PasswordChar = IsRevealed ? '\0' : MaskCharacter;
        }
    }
}
