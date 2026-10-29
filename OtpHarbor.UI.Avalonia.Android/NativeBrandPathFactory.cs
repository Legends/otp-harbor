using System.Runtime.CompilerServices;
using AndroidX.Core.Graphics;

namespace TOTP.Avalonia.Android;

internal static class NativeBrandPathFactory
{
    private static readonly ConditionalWeakTable<string, InvalidPathMarker> InvalidPathData = new();

    public static global::Android.Graphics.Path? TryCreate(string? pathData)
    {
        if (string.IsNullOrWhiteSpace(pathData) || InvalidPathData.TryGetValue(pathData, out _))
            return null;

        try
        {
            return PathParser.CreatePathFromPathData(pathData);
        }
        catch (Exception ex) when (IsPathParsingFailure(ex))
        {
            InvalidPathData.GetValue(pathData, static _ => InvalidPathMarker.Instance);
            return null;
        }
    }

    private static bool IsPathParsingFailure(Exception exception) =>
        exception is Java.Lang.RuntimeException
            or ArgumentException
            or FormatException
            or IndexOutOfRangeException;

    private sealed class InvalidPathMarker
    {
        public static InvalidPathMarker Instance { get; } = new();
    }
}
