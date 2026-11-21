using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace TOTP.Avalonia.Desktop.Controls;

public sealed class CollapseThenFadeTransition : IPageTransition
{
    private LayoutState? _layout;
    private int _generation;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        _layout?.Restore();
        _layout = null;
        cancellationToken.ThrowIfCancellationRequested();
        var owner = from?.GetVisualAncestors().OfType<CollapsingContentControl>().FirstOrDefault();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => lifetime.Cancel();
        if (owner is not null) owner.DetachedFromVisualTree += Detached;
        try
        {
            if (from is Control outgoing && to is ContentPresenter { Child: { } child } incoming
                && owner is not null)
            {
                child.Measure(new Size(Math.Max(1, incoming.Bounds.Width), double.PositiveInfinity));
                var targetHeight = child.DesiredSize.Height + incoming.Padding.Top + incoming.Padding.Bottom;
                var startingHeight = owner.Bounds.Height;
                if (double.IsFinite(targetHeight) && targetHeight > 0 && startingHeight > targetHeight + .5)
                {
                    _layout = new LayoutState(owner, outgoing, incoming);
                    owner.SetCurrentValue(InputElement.IsEnabledProperty, false);
                    owner.SetCurrentValue(Control.HeightProperty, startingHeight);
                    outgoing.SetCurrentValue(Control.HeightProperty, startingHeight);
                    outgoing.SetCurrentValue(Control.VerticalAlignmentProperty, VerticalAlignment.Top);
                    incoming.SetCurrentValue(Control.HeightProperty, targetHeight);
                    incoming.SetCurrentValue(Control.VerticalAlignmentProperty, VerticalAlignment.Top);
                    incoming.IsVisible = false;

                    var collapse = new Animation
                    {
                        Duration = TimeSpan.FromMilliseconds(180),
                        Easing = new CubicEaseOut(),
                        FillMode = FillMode.Forward,
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Control.HeightProperty, startingHeight) } },
                            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Control.HeightProperty, targetHeight) } }
                        }
                    };
                    await collapse.RunAsync(owner, lifetime.Token);
                    lifetime.Token.ThrowIfCancellationRequested();
                    owner.SetCurrentValue(Control.HeightProperty, targetHeight);
                }
            }

            if (_layout is null && owner is not null) owner.ReleaseHeightPin(owner.HeightPinVersion);

            await new CrossFade { Duration = TimeSpan.FromMilliseconds(200) }
                .Start(from, to, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
        }
        finally
        {
            if (owner is not null) owner.DetachedFromVisualTree -= Detached;
            if (generation == _generation)
            {
                // CrossFade hides the outgoing presenter before automatic sizing is restored.
                _layout?.Restore();
                _layout = null;
            }
        }
    }

    private sealed class LayoutState(CollapsingContentControl owner, Control outgoing, Control incoming)
    {
        private readonly int _pinVersion = owner.HeightPinVersion;
        private readonly double _ownerHeight = owner.UnpinnedHeight;
        private readonly bool _ownerEnabled = owner.IsEnabled;
        private readonly double _outgoingHeight = outgoing.Height;
        private readonly VerticalAlignment _outgoingAlignment = outgoing.VerticalAlignment;
        private readonly double _incomingHeight = incoming.Height;
        private readonly VerticalAlignment _incomingAlignment = incoming.VerticalAlignment;

        public void Restore()
        {
            outgoing.SetCurrentValue(Control.HeightProperty, _outgoingHeight);
            outgoing.SetCurrentValue(Control.VerticalAlignmentProperty, _outgoingAlignment);
            incoming.SetCurrentValue(Control.HeightProperty, _incomingHeight);
            incoming.SetCurrentValue(Control.VerticalAlignmentProperty, _incomingAlignment);
            // Cancellation can apply an animation's final height after the detach handler released its pin.
            owner.ReleaseHeightPin(_pinVersion, _ownerHeight);
            owner.SetCurrentValue(InputElement.IsEnabledProperty, _ownerEnabled);
        }
    }
}
