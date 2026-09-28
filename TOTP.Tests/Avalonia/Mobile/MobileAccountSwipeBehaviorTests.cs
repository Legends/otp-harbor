using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileAccountSwipeBehaviorTests
{
    [Theory]
    [InlineData(0, 9)]
    [InlineData(7, 7)]
    public void ResolveIntent_BelowDirectionThresholdWaitsForMoreMovement(
        double deltaX,
        double deltaY)
    {
        Assert.Equal(
            MobileAccountGestureIntent.Undetermined,
            MobileAccountSwipeBehavior.ResolveIntent(deltaX, deltaY));
    }

    [Theory]
    [InlineData(2, 12)]
    [InlineData(10, 20)]
    [InlineData(-8, -18)]
    public void ResolveIntent_VerticalMovementRemainsNativeScrolling(
        double deltaX,
        double deltaY)
    {
        Assert.Equal(
            MobileAccountGestureIntent.VerticalScroll,
            MobileAccountSwipeBehavior.ResolveIntent(deltaX, deltaY));
    }

    [Theory]
    [InlineData(15, 2)]
    [InlineData(-20, 5)]
    public void ResolveIntent_HorizontalMovementActivatesAccountSwipe(
        double deltaX,
        double deltaY)
    {
        Assert.Equal(
            MobileAccountGestureIntent.HorizontalSwipe,
            MobileAccountSwipeBehavior.ResolveIntent(deltaX, deltaY));
    }

    [Theory]
    [InlineData(120, 120, MobileAccountSwipeCompletion.RevealQrAndEdit)]
    [InlineData(-60, -60, MobileAccountSwipeCompletion.ConfirmDelete)]
    public void ApplyPointerDelta_TracksFingerDirectionAndClamps(
        double pointerDeltaX,
        double expectedOffset,
        MobileAccountSwipeCompletion expectedCompletion)
    {
        var offset = MobileAccountSwipeBehavior.ApplyPointerDelta(0, pointerDeltaX);

        Assert.Equal(expectedOffset, offset);
        Assert.Equal(expectedCompletion, MobileAccountSwipeBehavior.Complete(offset));
    }

    [Theory]
    [InlineData(-55)]
    [InlineData(0)]
    [InlineData(55)]
    public void Complete_BelowThresholdPerformsNoAction(double offset)
    {
        Assert.Equal(
            MobileAccountSwipeCompletion.None,
            MobileAccountSwipeBehavior.Complete(offset));
    }
}
