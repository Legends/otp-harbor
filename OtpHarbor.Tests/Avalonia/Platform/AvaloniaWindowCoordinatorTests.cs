using Avalonia.Controls;
using System.Runtime.CompilerServices;
using TOTP.Avalonia.Desktop.Platform;

namespace TOTP.Tests.Avalonia.Platform;

public sealed class AvaloniaWindowCoordinatorTests
{
    [Fact]
    public void NestedOwnedDialogs_TrackTheActiveActivationTarget()
    {
        var sut = new AvaloniaWindowCoordinator();
        var main = CreateWindowIdentity();
        var dialog = CreateWindowIdentity();
        sut.RegisterMainWindow(main);

        using (sut.RegisterOwnedDialog(dialog))
        {
            Assert.Same(dialog, sut.CurrentActivationTarget);
            Assert.Same(dialog, sut.GetRequiredDialogOwner());
            var nested = CreateWindowIdentity();
            using (sut.RegisterOwnedDialog(nested))
            {
                Assert.Same(nested, sut.CurrentActivationTarget);
                Assert.Same(nested, sut.GetRequiredDialogOwner());
            }
            Assert.Same(dialog, sut.CurrentActivationTarget);
        }

        Assert.Same(main, sut.CurrentActivationTarget);
    }

    [Fact]
    public void DialogWithoutMainOwner_FailsClosed()
    {
        var sut = new AvaloniaWindowCoordinator();

        Assert.Throws<InvalidOperationException>(() => sut.RegisterOwnedDialog(CreateWindowIdentity()));
        Assert.Throws<InvalidOperationException>(() => sut.GetRequiredMainWindow());
    }

    private static Window CreateWindowIdentity() =>
        (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
}
