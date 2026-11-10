using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TOTP.Avalonia.Desktop.Controls;

public sealed class CameraScanOverlay : Control
{
    public static readonly StyledProperty<IBrush?> FrameBrushProperty =
        AvaloniaProperty.Register<CameraScanOverlay, IBrush?>(nameof(FrameBrush));

    private static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(105, 0, 0, 0));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);

    static CameraScanOverlay() => AffectsRender<CameraScanOverlay>(FrameBrushProperty);

    public IBrush? FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height) * 0.7;
        var left = (Bounds.Width - size) / 2;
        var top = (Bounds.Height - size) / 2;
        var right = left + size;
        var bottom = top + size;
        for (var third = 1; third <= 2; third++)
        {
            var x = Bounds.Width * third / 3;
            var y = Bounds.Height * third / 3;
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, Bounds.Height));
            context.DrawLine(GridPen, new Point(0, y), new Point(Bounds.Width, y));
        }

        context.FillRectangle(Shade, new Rect(0, 0, Bounds.Width, top));
        context.FillRectangle(Shade, new Rect(0, bottom, Bounds.Width, top));
        context.FillRectangle(Shade, new Rect(0, top, left, size));
        context.FillRectangle(Shade, new Rect(right, top, left, size));

        var pen = new Pen(FrameBrush, 3, lineCap: PenLineCap.Round);
        var corner = size * 0.16;
        DrawCorner(left, top, corner, corner);
        DrawCorner(right, top, -corner, corner);
        DrawCorner(left, bottom, corner, -corner);
        DrawCorner(right, bottom, -corner, -corner);

        void DrawCorner(double x, double y, double dx, double dy)
        {
            context.DrawLine(pen, new Point(x, y), new Point(x + dx, y));
            context.DrawLine(pen, new Point(x, y), new Point(x, y + dy));
        }
    }
}
