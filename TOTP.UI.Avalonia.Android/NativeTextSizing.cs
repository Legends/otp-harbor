namespace TOTP.Avalonia.Android;

internal static class NativeTextSizing
{
    internal const float MaximumDenseFontScale = 1.15f;
    internal const float MinimumViewportScale = 0.85f;

    internal static float ResolvePixels(
        float textSp,
        float density,
        float fontScale,
        float viewportWidthDp,
        float referenceWidthDp)
    {
        if (textSp <= 0 || density <= 0) return 0;

        var effectiveFontScale = Math.Clamp(fontScale, 0.5f, MaximumDenseFontScale);
        var viewportScale = referenceWidthDp <= 0
            ? 1f
            : Math.Clamp(viewportWidthDp / referenceWidthDp, MinimumViewportScale, 1f);
        return textSp * density * effectiveFontScale * viewportScale;
    }

    internal static float FitPixels(
        float preferredPixels,
        float measuredWidth,
        float availableWidth,
        float minimumPixels)
    {
        if (preferredPixels <= 0 || availableWidth <= 0) return Math.Max(0, minimumPixels);
        if (measuredWidth <= availableWidth || measuredWidth <= 0) return preferredPixels;

        return Math.Clamp(
            preferredPixels * availableWidth / measuredWidth,
            Math.Min(minimumPixels, preferredPixels),
            preferredPixels);
    }
}
