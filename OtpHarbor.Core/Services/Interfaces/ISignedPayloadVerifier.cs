namespace TOTP.Core.Services.Interfaces;

public interface ISignedPayloadVerifier
{
    bool Verify(ReadOnlySpan<byte> payload, string signature, string publicKey);
}
