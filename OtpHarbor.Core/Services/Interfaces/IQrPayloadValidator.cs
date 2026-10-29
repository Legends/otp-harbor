namespace TOTP.Core.Services.Interfaces;

public enum QrPayloadKind
{
    StandardAccount,
    GoogleAuthenticatorMigration
}

public sealed record QrPayloadValidationResult(
    bool IsValid,
    string Issuer,
    string AccountName,
    QrPayloadKind Kind = QrPayloadKind.StandardAccount,
    int AccountCount = 1)
{
    public static QrPayloadValidationResult Invalid { get; } = new(
        false,
        string.Empty,
        string.Empty,
        QrPayloadKind.StandardAccount,
        0);
}

public interface IQrPayloadValidator
{
    QrPayloadValidationResult Validate(string decodedPayload);
}
