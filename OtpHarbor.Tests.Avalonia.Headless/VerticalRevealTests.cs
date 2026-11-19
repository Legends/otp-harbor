using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System.Reflection;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class VerticalRevealTests
{
    [AvaloniaFact]
    public void AnimatedReveal_RemainsOpenAfterCompletionAndCanBeReversed()
    {
        // Avalonia's clock is internal; drive a dedicated instance to test real transitions
        // without wall-clock delays or changing the application's render timer.
        var clockType = typeof(Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", true)!;
        var clock = Activator.CreateInstance(clockType, nonPublic: true)!;
        var pulse = clockType.GetMethod("Pulse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var clockProperty = (AvaloniaProperty)typeof(Animatable)
            .GetField("ClockProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var fields = new StackPanel { Children = { new TextBox { MinHeight = 44 } } };
        var reveal = new VerticalReveal { Child = fields };
        reveal.SetValue(clockProperty, clock);
        var window = new Window { Width = 360, Height = 300, Content = new StackPanel { Children = { reveal } } };
        void Advance(int milliseconds)
        {
            pulse.Invoke(clock, [TimeSpan.FromMilliseconds(milliseconds)]);
            window.UpdateLayout();
        }
        try
        {
            window.Show();
            window.UpdateLayout();
            reveal.IsExpanded = true;
            Advance(0);
            Advance(110);
            Assert.InRange(reveal.RevealProgress, 0.01, 0.99);
            Advance(300);
            Advance(600);
            Assert.True(reveal.IsExpanded);
            Assert.Equal(1, reveal.RevealProgress);
            Assert.True(fields.IsEffectivelyVisible);
            Assert.True(reveal.Bounds.Height >= 44);

            reveal.IsExpanded = false;
            Advance(600);
            Advance(710);
            Assert.InRange(reveal.RevealProgress, 0.01, 0.99);
            reveal.IsExpanded = true;
            Advance(710);
            Advance(1000);
            Assert.Equal(1, reveal.RevealProgress);
            Assert.True(fields.IsEffectivelyVisible);

            reveal.IsExpanded = false;
            Advance(1000);
            Advance(1300);
            Assert.Equal(0, reveal.RevealProgress);
            Assert.Equal(0, reveal.Bounds.Height);
            Assert.False(fields.IsEffectivelyVisible);

            reveal.IsExpanded = true;
            Advance(1300);
            Advance(1410);
            Assert.InRange(reveal.RevealProgress, 0.01, 0.99);
            Advance(1600);
            Advance(1900);
            Assert.Equal(1, reveal.RevealProgress);
            Assert.True(fields.IsEffectivelyVisible);
            Assert.True(reveal.Bounds.Height >= 44);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(320)]
    [InlineData(360)]
    [InlineData(430)]
    public void RevealProgress_ReflowsFollowingContentWithoutScalingFields(int width)
    {
        var fields = new StackPanel { Spacing = 10 };
        for (var index = 0; index < 3; index++)
            fields.Children.Add(new RevealableSecretInput { MinHeight = 44, PlaceholderText = "Synthetic password field" });
        var reveal = new VerticalReveal { Child = fields };
        reveal.Transitions!.Clear();
        var actions = new Button { Content = "Synthetic action", MinHeight = 44 };
        var window = new Window
        {
            Width = width, Height = 400,
            Content = new StackPanel { Children = { reveal, actions } }
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.False(fields.IsEffectivelyVisible);
            Assert.Equal(0, reveal.Bounds.Height);

            reveal.IsExpanded = true;
            window.UpdateLayout();
            var fullHeight = reveal.Bounds.Height;
            Assert.True(fullHeight >= 152);
            Assert.True(fields.IsEffectivelyVisible);
            var inputs = fields.GetVisualDescendants().OfType<TextBox>().ToArray();
            Assert.Equal(3, inputs.Length);
            var inputHeights = inputs.Select(value => value.Bounds.Height).ToArray();

            reveal.SetValue(VerticalReveal.RevealProgressProperty, 0.5);
            window.UpdateLayout();

            Assert.True(reveal.ClipToBounds);
            Assert.Equal(fullHeight / 2, reveal.Bounds.Height, 1);
            Assert.Equal(fullHeight, fields.Bounds.Height, 1);
            Assert.Equal(reveal.Bounds.Bottom, actions.Bounds.Y, 1);
            Assert.Equal(inputHeights, inputs.Select(value => value.Bounds.Height));
            Assert.All(inputs, input => Assert.True(input.Bounds.Width <= width));

            reveal.IsExpanded = false;
            window.UpdateLayout();
            Assert.False(fields.IsEffectivelyVisible);
            Assert.False(inputs[0].Focus());
            Assert.Equal(0, reveal.Bounds.Height);
            Assert.Equal(0, actions.Bounds.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ExpandedContent_GrowsNaturallyAndRemainsScrollableAtConstrainedHeight()
    {
        var fields = new StackPanel { Spacing = 10 };
        var reveal = new VerticalReveal { Child = fields, IsExpanded = true };
        reveal.Transitions!.Clear();
        var actions = new Button { Content = "Synthetic action", MinHeight = 44 };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new StackPanel { Children = { reveal, actions } }
        };
        var window = new Window { Width = 360, Height = 160, Content = scroller };
        try
        {
            window.Show();
            window.UpdateLayout();
            var before = reveal.Bounds.Height;
            for (var index = 0; index < 5; index++) fields.Children.Add(new RevealableSecretInput { MinHeight = 44 });
            window.UpdateLayout();
            Assert.True(reveal.Bounds.Height > before);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            window.UpdateLayout();
            var actionBottom = actions.TranslatePoint(new Point(0, actions.Bounds.Height), window);
            Assert.NotNull(actionBottom);
            Assert.True(actionBottom.Value.Y <= window.ClientSize.Height + 1);
        }
        finally { window.Close(); }
    }
}
