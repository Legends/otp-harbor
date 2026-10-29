using System.Text.Json;
using System.Text.Json.Nodes;
using TOTP.Infrastructure.Parser;

namespace TOTP.Tests.Services;

public sealed class ExternalBackupParserConformanceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Aegis_Parse_AcceptsEverySupportedDatabaseVersion(int version)
    {
        var root = ParseNode(AegisJson());
        root["db"]!["version"] = version;

        var accounts = ParseAegis(root);

        var account = Assert.Single(accounts);
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("alice", account.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", account.Secret);
        Assert.Equal(30, account.PeriodSeconds);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(1, 0)]
    [InlineData(1, 4)]
    public void Aegis_Parse_RejectsEveryUnsupportedVersionBoundary(
        int outerVersion,
        int databaseVersion)
    {
        var root = ParseNode(AegisJson());
        root["version"] = outerVersion;
        root["db"]!["version"] = databaseVersion;

        Assert.Throws<FormatException>(() => ParseAegis(root));
    }

    [Theory]
    [InlineData("missing-outer-version")]
    [InlineData("invalid-outer-version-type")]
    [InlineData("missing-db")]
    [InlineData("encrypted-db")]
    [InlineData("missing-database-version")]
    [InlineData("invalid-database-version-type")]
    [InlineData("missing-entries")]
    [InlineData("invalid-entries-type")]
    [InlineData("invalid-entry-type")]
    [InlineData("missing-type")]
    [InlineData("unsupported-hotp")]
    [InlineData("missing-info")]
    [InlineData("invalid-info-type")]
    [InlineData("missing-issuer")]
    [InlineData("missing-name")]
    [InlineData("missing-secret")]
    [InlineData("invalid-secret")]
    [InlineData("missing-algorithm")]
    [InlineData("invalid-algorithm-type")]
    [InlineData("missing-digits")]
    [InlineData("invalid-digits-type")]
    [InlineData("missing-period")]
    [InlineData("invalid-period-type")]
    public void Aegis_Parse_RejectsMalformedRequiredStructure(string scenario)
    {
        var root = ParseNode(AegisJson());
        var database = root["db"]!.AsObject();
        var entry = database["entries"]![0]!.AsObject();
        var info = entry["info"]!.AsObject();

        switch (scenario)
        {
            case "missing-outer-version": root.Remove("version"); break;
            case "invalid-outer-version-type": root["version"] = "1"; break;
            case "missing-db": root.Remove("db"); break;
            case "encrypted-db": root["db"] = "ciphertext"; break;
            case "missing-database-version": database.Remove("version"); break;
            case "invalid-database-version-type": database["version"] = "3"; break;
            case "missing-entries": database.Remove("entries"); break;
            case "invalid-entries-type": database["entries"] = new JsonObject(); break;
            case "invalid-entry-type": database["entries"]![0] = "entry"; break;
            case "missing-type": entry.Remove("type"); break;
            case "unsupported-hotp": entry["type"] = "hotp"; break;
            case "missing-info": entry.Remove("info"); break;
            case "invalid-info-type": entry["info"] = "info"; break;
            case "missing-issuer": entry.Remove("issuer"); break;
            case "missing-name": entry.Remove("name"); break;
            case "missing-secret": info.Remove("secret"); break;
            case "invalid-secret": info["secret"] = "INVALID1"; break;
            case "missing-algorithm": info.Remove("algo"); break;
            case "invalid-algorithm-type": info["algo"] = 1; break;
            case "missing-digits": info.Remove("digits"); break;
            case "invalid-digits-type": info["digits"] = "6"; break;
            case "missing-period": info.Remove("period"); break;
            case "invalid-period-type": info["period"] = "30"; break;
        }

        Assert.Throws<FormatException>(() => ParseAegis(root));
    }

    [Theory]
    [InlineData("totp", "SHA256", 6, 30)]
    [InlineData("totp", "SHA1", 8, 30)]
    [InlineData("totp", "SHA1", 6, 4)]
    [InlineData("totp", "SHA1", 6, 3601)]
    public void Aegis_Parse_RejectsEveryUnsupportedTotpParameterClass(
        string type,
        string algorithm,
        int digits,
        int period)
    {
        var root = ParseNode(AegisJson());
        var entry = root["db"]!["entries"]![0]!;
        entry["type"] = type;
        entry["info"]!["algo"] = algorithm;
        entry["info"]!["digits"] = digits;
        entry["info"]!["period"] = period;

        Assert.Throws<FormatException>(() => ParseAegis(root));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TwoFas_Parse_AcceptsEverySupportedSchemaVersion(int version)
    {
        var root = ParseNode(TwoFasJson());
        root["schemaVersion"] = version;

        var accounts = ParseTwoFas(root);

        var account = Assert.Single(accounts);
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("alice", account.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", account.Secret);
        Assert.Equal(30, account.PeriodSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void TwoFas_Parse_RejectsUnsupportedSchemaVersions(int version)
    {
        var root = ParseNode(TwoFasJson());
        root["schemaVersion"] = version;

        Assert.Throws<FormatException>(() => ParseTwoFas(root));
    }

    [Theory]
    [InlineData("missing-schema")]
    [InlineData("invalid-schema-type")]
    [InlineData("missing-services")]
    [InlineData("invalid-services-type")]
    [InlineData("invalid-service-type")]
    [InlineData("missing-otp")]
    [InlineData("invalid-otp-type")]
    [InlineData("missing-name")]
    [InlineData("missing-secret")]
    [InlineData("invalid-secret")]
    [InlineData("invalid-token-type")]
    [InlineData("invalid-algorithm-type")]
    [InlineData("invalid-digits-type")]
    [InlineData("invalid-period-type")]
    [InlineData("invalid-reference-type")]
    [InlineData("invalid-encrypted-services-type")]
    public void TwoFas_Parse_RejectsMalformedRequiredStructure(string scenario)
    {
        var root = ParseNode(TwoFasJson());
        var service = root["services"]![0]!.AsObject();
        var otp = service["otp"]!.AsObject();

        switch (scenario)
        {
            case "missing-schema": root.Remove("schemaVersion"); break;
            case "invalid-schema-type": root["schemaVersion"] = "4"; break;
            case "missing-services": root.Remove("services"); break;
            case "invalid-services-type": root["services"] = new JsonObject(); break;
            case "invalid-service-type": root["services"]![0] = "service"; break;
            case "missing-otp": service.Remove("otp"); break;
            case "invalid-otp-type": service["otp"] = "otp"; break;
            case "missing-name": service.Remove("name"); break;
            case "missing-secret": service.Remove("secret"); break;
            case "invalid-secret": service["secret"] = "INVALID1"; break;
            case "invalid-token-type": otp["tokenType"] = 2; break;
            case "invalid-algorithm-type": otp["algorithm"] = 1; break;
            case "invalid-digits-type": otp["digits"] = "6"; break;
            case "invalid-period-type": otp["period"] = "30"; break;
            case "invalid-reference-type": root["reference"] = 1; break;
            case "invalid-encrypted-services-type": root["servicesEncrypted"] = new JsonArray(); break;
        }

        Assert.Throws<FormatException>(() => ParseTwoFas(root));
    }

    [Theory]
    [InlineData("HOTP", "SHA1", 6, 30)]
    [InlineData("TOTP", "SHA256", 6, 30)]
    [InlineData("TOTP", "SHA1", 8, 30)]
    [InlineData("TOTP", "SHA1", 6, 4)]
    [InlineData("TOTP", "SHA1", 6, 3601)]
    public void TwoFas_Parse_RejectsEveryUnsupportedTotpParameterClass(
        string type,
        string algorithm,
        int digits,
        int period)
    {
        var root = ParseNode(TwoFasJson());
        var otp = root["services"]![0]!["otp"]!;
        otp["tokenType"] = type;
        otp["algorithm"] = algorithm;
        otp["digits"] = digits;
        otp["period"] = period;

        Assert.Throws<FormatException>(() => ParseTwoFas(root));
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("servicesEncrypted")]
    public void TwoFas_Parse_RejectsEachEncryptionMarker(string propertyName)
    {
        var root = ParseNode(TwoFasJson());
        root[propertyName] = "encrypted";

        Assert.Throws<FormatException>(() => ParseTwoFas(root));
    }

    [Fact]
    public void TwoFas_Parse_AppliesIdentityFallbacksInDefinedOrder()
    {
        var root = ParseNode(TwoFasJson());
        var service = root["services"]![0]!;
        var otp = service["otp"]!;
        service["name"] = "Service fallback";
        otp["issuer"] = null;
        otp["account"] = null;
        otp["label"] = "label fallback";

        var account = Assert.Single(ParseTwoFas(root));

        Assert.Equal("Service fallback", account.Issuer);
        Assert.Equal("label fallback", account.AccountName);
    }

    private static JsonObject ParseNode(string json) =>
        JsonNode.Parse(json)!.AsObject();

    private static IReadOnlyList<TOTP.Core.Models.Account> ParseAegis(JsonObject root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        return AegisVaultParser.Parse(document.RootElement);
    }

    private static IReadOnlyList<TOTP.Core.Models.Account> ParseTwoFas(JsonObject root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        return TwoFasBackupParser.Parse(document.RootElement);
    }

    private static string AegisJson() => """
        {
          "version": 1,
          "db": {
            "version": 3,
            "entries": [
              {
                "type": "totp",
                "name": " alice ",
                "issuer": " Example ",
                "info": {
                  "secret": "jbsw y3dp-ehpk3pxp====",
                  "algo": "SHA1",
                  "digits": 6,
                  "period": 30
                }
              }
            ]
          }
        }
        """;

    private static string TwoFasJson() => """
        {
          "services": [
            {
              "name": "Service fallback",
              "secret": "jbsw y3dp-ehpk3pxp====",
              "otp": {
                "label": "label fallback",
                "account": "alice",
                "issuer": "Example",
                "digits": 6,
                "period": 30,
                "algorithm": "SHA1",
                "tokenType": "TOTP"
              }
            }
          ],
          "schemaVersion": 4,
          "servicesEncrypted": null,
          "reference": null
        }
        """;
}
