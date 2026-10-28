using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class NotificationBanner : TemplatedControl
{
    private bool _hasTitle;

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<NotificationBanner, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<NotificationBanner, string>(nameof(Text), string.Empty);

    public static readonly DirectProperty<NotificationBanner, bool> HasTitleProperty =
        AvaloniaProperty.RegisterDirect<NotificationBanner, bool>(
            nameof(HasTitle),
            control => control.HasTitle);

    public static readonly StyledProperty<NotificationSeverity> SeverityProperty =
        AvaloniaProperty.Register<NotificationBanner, NotificationSeverity>(
            nameof(Severity),
            NotificationSeverity.Information);

    public static readonly StyledProperty<AutomationLiveSetting> LiveSettingProperty =
        AvaloniaProperty.Register<NotificationBanner, AutomationLiveSetting>(
            nameof(LiveSetting),
            AutomationLiveSetting.Polite);

    static NotificationBanner()
    {
        TitleProperty.Changed.AddClassHandler<NotificationBanner>(
            static (control, _) => control.UpdateVisualState());
        TextProperty.Changed.AddClassHandler<NotificationBanner>(
            static (control, _) => control.UpdateVisualState());
        SeverityProperty.Changed.AddClassHandler<NotificationBanner>(
            static (control, _) => control.UpdateVisualState());
    }

    public NotificationBanner()
    {
        UpdateVisualState();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value ?? string.Empty);
    }

    public bool HasTitle
    {
        get => _hasTitle;
        private set => SetAndRaise(HasTitleProperty, ref _hasTitle, value);
    }

    public NotificationSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public AutomationLiveSetting LiveSetting
    {
        get => GetValue(LiveSettingProperty);
        private set => SetValue(LiveSettingProperty, value);
    }

    private void UpdateVisualState()
    {
        HasTitle = !string.IsNullOrWhiteSpace(Title);
        IsVisible = !string.IsNullOrWhiteSpace(Text);
        LiveSetting = Severity == NotificationSeverity.Error
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite;
        PseudoClasses.Set(":information", Severity == NotificationSeverity.Information);
        PseudoClasses.Set(":success", Severity == NotificationSeverity.Success);
        PseudoClasses.Set(":warning", Severity == NotificationSeverity.Warning);
        PseudoClasses.Set(":error", Severity == NotificationSeverity.Error);
    }
}
