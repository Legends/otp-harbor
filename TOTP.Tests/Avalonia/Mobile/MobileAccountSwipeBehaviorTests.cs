using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileAccountSwipeBehaviorTests
{
    [Fact]
    public void ApplyAvaloniaDelta_LeftSwipeMovesRowTowardDeleteAction()
    {
        var offset = MobileAccountSwipeBehavior.ApplyAvaloniaDelta(0, 60);

        Assert.Equal(MobileAccountSwipeBehavior.DeleteRevealOffset, offset);
        Assert.Equal(
            MobileAccountSwipeCompletion.ConfirmDelete,
            MobileAccountSwipeBehavior.Complete(offset));
    }

    [Fact]
    public void ApplyAvaloniaDelta_RightSwipeMovesRowTowardQrAndEditActions()
    {
        var offset = MobileAccountSwipeBehavior.ApplyAvaloniaDelta(0, -120);

        Assert.Equal(MobileAccountSwipeBehavior.QrAndEditRevealOffset, offset);
        Assert.Equal(
            MobileAccountSwipeCompletion.RevealQrAndEdit,
            MobileAccountSwipeBehavior.Complete(offset));
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
