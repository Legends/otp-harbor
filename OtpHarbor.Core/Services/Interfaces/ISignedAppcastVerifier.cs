namespace TOTP.Core.Services.Interfaces;

public enum SignedAppcastCheckStatus
{
    InvalidSignature = 0,
    InvalidFormat,
    NoApplicableUpdate,
    UpdateAvailable
}

public sealed record SignedAppcastCheckRequest(
    ReadOnlyMemory<byte> AppcastBytes,
    string Signature,
    string PublicKey,
    Version CurrentVersion,
    string OperatingSystem,
    string Architecture,
    bool RequireExplicitTarget = false,
    string Channel = "stable");

public sealed record SignedAppcastCheckResult(
    SignedAppcastCheckStatus Status,
    Version? Version = null,
    Uri? ArtifactUri = null,
    string? ArtifactSignature = null,
    string? ReleaseNotes = null);

public interface ISignedAppcastVerifier
{
    SignedAppcastCheckResult Verify(SignedAppcastCheckRequest request);
}
