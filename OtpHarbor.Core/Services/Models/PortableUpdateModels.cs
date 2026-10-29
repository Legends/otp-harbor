namespace TOTP.Core.Services.Models;

public enum PortableUpdateCheckStatus
{
    Disabled,
    NoUpdate,
    UpdateAvailable
}

public sealed record PortableUpdateOffer(
    Version Version,
    Uri ArtifactUri,
    string ArtifactSignature,
    string ReleaseNotes);

public sealed record PortableUpdateCheckResult(
    PortableUpdateCheckStatus Status,
    PortableUpdateOffer? Offer = null);

public sealed record PortableUpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public int? Percentage => TotalBytes is > 0
        ? (int)Math.Clamp(BytesReceived * 100 / TotalBytes.Value, 0, 100)
        : null;
}

public sealed record PortableUpdatePackage(
    Version Version,
    string FilePath,
    string ExpectedSignature,
    string PublicKey);
