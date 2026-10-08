using Android.Graphics;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Android;

internal sealed class NativeBrandIcon : IDisposable
{
    private readonly Layer[] _layers;

    private NativeBrandIcon(Layer[] layers) => _layers = layers;

    public static NativeBrandIcon? TryCreate(
        BrandInfo brand,
        RectF target,
        Color monochromeColor)
    {
        ArgumentNullException.ThrowIfNull(brand);
        ArgumentNullException.ThrowIfNull(target);

        if (target.Width() <= 0 || target.Height() <= 0) return null;
        if (brand.IconLayers is { Count: > 0 } layers
            && layers.Any(static layer => layer.FillColor is not null || layer.Stroke is not null))
        {
            var colored = TryCreateLayers(layers, target);
            if (colored is not null) return colored;
        }

        if (brand.IconData is not { Length: > 0 } pathData) return null;
        using var sourcePath = NativeBrandPathFactory.TryCreate(pathData);
        if (sourcePath is null) return null;
        ApplyTransform(sourcePath, ToMatrix(brand.SourceTransform));
        using var sourceBounds = new RectF();
        sourcePath.ComputeBounds(sourceBounds, true);
        if (sourceBounds.Width() <= 0 || sourceBounds.Height() <= 0) return null;

        using var fit = new Matrix();
        fit.SetRectToRect(sourceBounds, target, Matrix.ScaleToFit.Center);
        var fittedPath = new global::Android.Graphics.Path();
        sourcePath.Transform(fit, fittedPath);
        return new NativeBrandIcon([new Layer(fittedPath, monochromeColor, null, 0)]);
    }

    public void Draw(Canvas canvas, Paint fill, Paint stroke)
    {
        foreach (var layer in _layers)
        {
            if (layer.FillColor is Color fillColor)
            {
                fill.Color = fillColor;
                canvas.DrawPath(layer.Path, fill);
            }

            if (layer.StrokeColor is not Color strokeColor) continue;
            stroke.Color = strokeColor;
            stroke.StrokeWidth = layer.StrokeWidth;
            canvas.DrawPath(layer.Path, stroke);
        }
    }

    public void Dispose()
    {
        foreach (var layer in _layers) layer.Path.Dispose();
    }

    private static NativeBrandIcon? TryCreateLayers(
        IReadOnlyList<BrandIconLayer> sourceLayers,
        RectF target)
    {
        var parsed = new List<ParsedLayer>(sourceLayers.Count);
        RectF? viewport = null;
        try
        {
            foreach (var layer in sourceLayers)
            {
                var path = NativeBrandPathFactory.TryCreate(layer.PathData);
                if (path is null) continue;
                ApplyTransform(path, ToMatrix(layer.Transform));

                var fillColor = ParseFill(layer.FillColor);
                var strokeColor = ParseStroke(layer.Stroke);
                if (!fillColor.IsValid || !strokeColor.IsValid
                    || (fillColor.Color is null && strokeColor.Color is null))
                {
                    path.Dispose();
                    continue;
                }

                parsed.Add(new ParsedLayer(
                    path,
                    fillColor.Color,
                    strokeColor.Color,
                    layer.Stroke?.Width ?? 0));
                if (viewport is null && layer.Viewport is { Width: > 0, Height: > 0 } sourceViewport)
                {
                    viewport = new RectF(
                        (float)sourceViewport.X,
                        (float)sourceViewport.Y,
                        (float)(sourceViewport.X + sourceViewport.Width),
                        (float)(sourceViewport.Y + sourceViewport.Height));
                }
            }

            if (parsed.Count == 0) return null;
            viewport ??= BoundsFor(parsed);
            if (viewport.Width() <= 0 || viewport.Height() <= 0) return null;

            using var fit = new Matrix();
            fit.SetRectToRect(viewport, target, Matrix.ScaleToFit.Center);
            var scale = Math.Min(target.Width() / viewport.Width(), target.Height() / viewport.Height());
            var fitted = new Layer[parsed.Count];
            for (var index = 0; index < parsed.Count; index++)
            {
                var source = parsed[index];
                var path = new global::Android.Graphics.Path();
                source.Path.Transform(fit, path);
                fitted[index] = new Layer(
                    path,
                    source.FillColor,
                    source.StrokeColor,
                    Math.Max(0, (float)source.StrokeWidth * scale));
            }

            return new NativeBrandIcon(fitted);
        }
        finally
        {
            foreach (var layer in parsed) layer.Path.Dispose();
            viewport?.Dispose();
        }
    }

    private static RectF BoundsFor(IEnumerable<ParsedLayer> layers)
    {
        RectF? combined = null;
        foreach (var layer in layers)
        {
            using var bounds = new RectF();
            layer.Path.ComputeBounds(bounds, true);
            if (bounds.Width() <= 0 || bounds.Height() <= 0) continue;
            if (combined is null)
                combined = new RectF(bounds);
            else
                combined.Union(bounds);
        }

        return combined ?? new RectF();
    }

    private static PaintColor ParseFill(string? value)
    {
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            return new PaintColor(true, null);
        return string.IsNullOrWhiteSpace(value)
            ? new PaintColor(true, Color.Black)
            : TryParseColor(value);
    }

    private static PaintColor ParseStroke(BrandIconStroke? stroke) => stroke is null
        ? new PaintColor(true, null)
        : TryParseColor(stroke.Color);

    private static PaintColor TryParseColor(string value)
    {
        try
        {
            return new PaintColor(true, Color.ParseColor(value));
        }
        catch (ArgumentException)
        {
            return new PaintColor(false, null);
        }
    }

    private static Matrix? ToMatrix(BrandIconTransform? transform)
    {
        if (transform is null) return null;
        var matrix = new Matrix();
        matrix.SetValues(
        [
            (float)transform.M11, (float)transform.M21, (float)transform.M31,
            (float)transform.M12, (float)transform.M22, (float)transform.M32,
            0, 0, 1
        ]);
        return matrix;
    }

    private static void ApplyTransform(global::Android.Graphics.Path path, Matrix? transform)
    {
        if (transform is null) return;
        using (transform) path.Transform(transform);
    }

    private sealed record Layer(
        global::Android.Graphics.Path Path,
        Color? FillColor,
        Color? StrokeColor,
        float StrokeWidth);

    private sealed record ParsedLayer(
        global::Android.Graphics.Path Path,
        Color? FillColor,
        Color? StrokeColor,
        double StrokeWidth);

    private readonly record struct PaintColor(bool IsValid, Color? Color);
}
