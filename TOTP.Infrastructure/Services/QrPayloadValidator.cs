using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Parser;

namespace TOTP.Infrastructure.Services;

public sealed class QrPayloadValidator : IQrPayloadValidator
{
    public QrPayloadValidationResult Validate(string decodedPayload)
    {
        var kind = !string.IsNullOrWhiteSpace(decodedPayload)
            && GoogleAuthenticatorMigrationParser.IsMigrationPayload(decodedPayload)
                ? QrPayloadKind.GoogleAuthenticatorMigration
                : QrPayloadKind.StandardAccount;
        if (string.IsNullOrWhiteSpace(decodedPayload) || decodedPayload.Length > 4096)
            return Invalid(kind);

        try
        {
            if (kind == QrPayloadKind.GoogleAuthenticatorMigration)
            {
                var migration = GoogleAuthenticatorMigrationParser.Parse(decodedPayload);
                var first = migration.Accounts[0];
                return new QrPayloadValidationResult(
                    true,
                    first.Issuer?.Trim() ?? string.Empty,
                    first.Label.Trim(),
                    QrPayloadKind.GoogleAuthenticatorMigration,
                    migration.Accounts.Count);
            }

            var parsed = OtpauthParser.Parse(decodedPayload);
            if (!OtpAuthSupportPolicy.IsSupported(parsed)) return Invalid(kind);
            return new QrPayloadValidationResult(
                true,
                parsed.Issuer?.Trim() ?? string.Empty,
                parsed.Label.Trim());
        }
        catch (Exception)
        {
            return Invalid(kind);
        }
    }

    private static QrPayloadValidationResult Invalid(QrPayloadKind kind) => new(
        false,
        string.Empty,
        string.Empty,
        kind,
        0);
}
