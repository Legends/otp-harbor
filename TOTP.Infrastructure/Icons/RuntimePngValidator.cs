using TOTP.Core.Icons;

namespace TOTP.Infrastructure.Icons;

internal static class RuntimePngValidator
{
    internal const int Width = RuntimePngProfileValidator.Width;
    internal const int Height = RuntimePngProfileValidator.Height;
    internal const int MaximumEncodedBytes = RuntimePngProfileValidator.MaximumEncodedBytes;

    internal static void ValidateEncoded(ReadOnlySpan<byte> png, string expectedSha256) =>
        RuntimePngProfileValidator.ValidateEncoded(png, expectedSha256);
}
