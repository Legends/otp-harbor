namespace TOTP.Avalonia.Mobile.Presentation;

public sealed record MobileAccountRevealRequest(
    Guid AccountId,
    int Revision,
    bool Highlight = true,
    bool AlignToTop = false);
