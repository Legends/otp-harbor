namespace TOTP.Avalonia.Mobile.Presentation;

public enum MobileAccountSwipeCompletion
{
    None,
    RevealQrAndEdit,
    ConfirmDelete
}

public static class MobileAccountSwipeBehavior
{
    public const double ActionThreshold = 56d;
    public const double QrAndEditRevealOffset = 120d;
    public const double DeleteRevealOffset = -60d;

    public static double ApplyAvaloniaDelta(double currentOffset, double deltaX) =>
        Math.Clamp(
            currentOffset - deltaX,
            DeleteRevealOffset,
            QrAndEditRevealOffset);

    public static MobileAccountSwipeCompletion Complete(double offset) => offset switch
    {
        >= ActionThreshold => MobileAccountSwipeCompletion.RevealQrAndEdit,
        <= -ActionThreshold => MobileAccountSwipeCompletion.ConfirmDelete,
        _ => MobileAccountSwipeCompletion.None
    };
}
