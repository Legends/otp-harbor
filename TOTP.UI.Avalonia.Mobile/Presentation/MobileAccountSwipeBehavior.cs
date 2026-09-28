namespace TOTP.Avalonia.Mobile.Presentation;

public enum MobileAccountSwipeCompletion
{
    None,
    RevealQrAndEdit,
    ConfirmDelete
}

public enum MobileAccountGestureIntent
{
    Undetermined,
    VerticalScroll,
    HorizontalSwipe
}

public static class MobileAccountSwipeBehavior
{
    public const double DirectionLockThreshold = 10d;
    public const double HorizontalDominanceRatio = 1.2d;
    public const double ActionThreshold = 56d;
    public const double QrAndEditRevealOffset = 120d;
    public const double DeleteRevealOffset = -60d;

    public static double ApplyPointerDelta(double startOffset, double pointerDeltaX) =>
        Math.Clamp(
            startOffset + pointerDeltaX,
            DeleteRevealOffset,
            QrAndEditRevealOffset);

    public static MobileAccountGestureIntent ResolveIntent(double deltaX, double deltaY)
    {
        var horizontalDistance = Math.Abs(deltaX);
        var verticalDistance = Math.Abs(deltaY);
        if (Math.Max(horizontalDistance, verticalDistance) < DirectionLockThreshold)
            return MobileAccountGestureIntent.Undetermined;

        return horizontalDistance >= verticalDistance * HorizontalDominanceRatio
            ? MobileAccountGestureIntent.HorizontalSwipe
            : MobileAccountGestureIntent.VerticalScroll;
    }

    public static MobileAccountSwipeCompletion Complete(double offset) => offset switch
    {
        >= ActionThreshold => MobileAccountSwipeCompletion.RevealQrAndEdit,
        <= -ActionThreshold => MobileAccountSwipeCompletion.ConfirmDelete,
        _ => MobileAccountSwipeCompletion.None
    };
}
