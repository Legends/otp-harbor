using System.Text;
using TOTP.Infrastructure.Parser;

namespace TOTP.Tests.Services;

public sealed class GoogleAuthenticatorMigrationParserConformanceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("otpauth-migration://example?data=AA%3D%3D")]
    [InlineData("https://offline?data=AA%3D%3D")]
    [InlineData("otpauth-migration://offline")]
    [InlineData("otpauth-migration://offline?data=")]
    [InlineData("otpauth-migration://offline?data=not-base64")]
    public void Parse_RejectsMalformedUriEnvelope(string payload)
    {
        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Parse_AcceptsEverySupportedVersion(int version)
    {
        var batch = GoogleAuthenticatorMigrationParser.Parse(
            CreatePayload([CreateAccount()], version: version));

        var account = Assert.Single(batch.Accounts);
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("alice", account.Label);
        Assert.Equal("AEAQ", account.SecretBase32);
        Assert.Equal("SHA1", account.Algorithm);
        Assert.Equal(6, account.Digits);
        Assert.Equal(30, account.Period);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Parse_RejectsEveryUnsupportedVersionBoundary(int version)
    {
        var payload = CreatePayload([CreateAccount()], version: version);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(100, 99)]
    public void Parse_AcceptsValidBatchMetadataBoundaries(int batchSize, int batchIndex)
    {
        var batch = GoogleAuthenticatorMigrationParser.Parse(
            CreatePayload([CreateAccount()], batchSize: batchSize, batchIndex: batchIndex));

        Assert.Equal(batchSize, batch.BatchSize);
        Assert.Equal(batchIndex, batch.BatchIndex);
        Assert.Equal(42, batch.BatchId);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(101, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Parse_RejectsInvalidBatchMetadataBoundaries(int batchSize, int batchIndex)
    {
        var payload = CreatePayload(
            [CreateAccount()],
            batchSize: batchSize,
            batchIndex: batchIndex);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    [Fact]
    public void Parse_AcceptsMaximumAccountCount()
    {
        var accounts = Enumerable.Range(0, 100)
            .Select(index => CreateAccount(name: $"a{index}", issuer: "I"))
            .ToArray();

        var batch = GoogleAuthenticatorMigrationParser.Parse(CreatePayload(accounts));

        Assert.Equal(100, batch.Accounts.Count);
    }

    [Fact]
    public void Parse_RejectsMoreThanMaximumAccountCount()
    {
        var accounts = Enumerable.Range(0, 101)
            .Select(index => CreateAccount(name: $"a{index}", issuer: "I"))
            .ToArray();
        var payload = CreatePayload(accounts);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public void Parse_AcceptsSecretLengthBoundaries(int secretLength)
    {
        var secret = Enumerable.Range(0, secretLength).Select(index => (byte)index).ToArray();

        var batch = GoogleAuthenticatorMigrationParser.Parse(
            CreatePayload([CreateAccount(secret: secret)]));

        Assert.Single(batch.Accounts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    public void Parse_RejectsSecretLengthsOutsideBoundaries(int secretLength)
    {
        var secret = new byte[secretLength];
        var payload = CreatePayload([CreateAccount(secret: secret)]);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 2, 2)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 1, 3)]
    public void Parse_RejectsEveryUnsupportedOtpParameterClass(
        int algorithm,
        int digits,
        int type)
    {
        var payload = CreatePayload(
            [CreateAccount(algorithm: algorithm, digits: digits, type: type)]);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    [Theory]
    [InlineData("Example:alice", "Example", "Example", "alice")]
    [InlineData("Example:alice", "", "Example", "alice")]
    [InlineData("alice", "Example", "Example", "alice")]
    [InlineData("Example", "", "Example", "")]
    public void Parse_NormalizesSupportedIdentityForms(
        string name,
        string issuer,
        string expectedIssuer,
        string expectedName)
    {
        var batch = GoogleAuthenticatorMigrationParser.Parse(
            CreatePayload([CreateAccount(name: name, issuer: issuer)]));

        var account = Assert.Single(batch.Accounts);
        Assert.Equal(expectedIssuer, account.Issuer);
        Assert.Equal(expectedName, account.Label);
    }

    [Theory]
    [MemberData(nameof(InvalidIdentities))]
    public void Parse_RejectsInvalidNormalizedIdentities(string name, string issuer)
    {
        var payload = CreatePayload([CreateAccount(name: name, issuer: issuer)]);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    public static IEnumerable<object[]> InvalidIdentities()
    {
        yield return [string.Empty, string.Empty];
        yield return ["account", new string('I', 257)];
        yield return [new string('a', 257), "Issuer"];
    }

    [Fact]
    public void Parse_AcceptsUrlSafeBase64WithoutPadding()
    {
        var payload = CreatePayload([CreateAccount()], urlSafeWithoutPadding: true);

        var batch = GoogleAuthenticatorMigrationParser.Parse(payload);

        Assert.Single(batch.Accounts);
    }

    [Fact]
    public void Parse_AcceptsCaseInsensitiveDataParameterAmongUnrelatedParameters()
    {
        var original = CreatePayload([CreateAccount()]);
        var encoded = original[(original.IndexOf("data=", StringComparison.Ordinal) + 5)..];
        var payload = $"otpauth-migration://offline?ignored=value&DATA={encoded}&other=value";

        var batch = GoogleAuthenticatorMigrationParser.Parse(payload);

        Assert.Single(batch.Accounts);
    }

    [Fact]
    public void Parse_IgnoresUnknownSupportedWireTypes()
    {
        var account = CreateAccount().ToList();
        WriteVarintField(account, 7, 123);
        WriteFixedField(account, 8, 8);
        WriteBytesField(account, 9, [1, 2, 3]);
        WriteFixedField(account, 10, 4);
        var payloadBytes = CreatePayloadBytes([account.ToArray()]);
        WriteVarintField(payloadBytes, 6, 123);

        var batch = GoogleAuthenticatorMigrationParser.Parse(ToUri(payloadBytes));

        Assert.Single(batch.Accounts);
    }

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void Parse_RejectsMalformedProtobuf(byte[] protobuf)
    {
        var payload = ToUri(protobuf);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    [Fact]
    public void Parse_RejectsPayloadAboveMaximumDecodedSize()
    {
        var payload = ToUri(new byte[4097]);

        Assert.Throws<FormatException>(() => GoogleAuthenticatorMigrationParser.Parse(payload));
    }

    public static IEnumerable<object[]> MalformedPayloads()
    {
        yield return [Array.Empty<byte>()];
        yield return [new byte[] { 0 }];
        yield return [new byte[] { 0x80 }];
        yield return [new byte[] { 0x0A, 0x02, 0x01 }];
        yield return [new byte[] { 0x0B }];

        var invalidUtf8Account = CreateAccount().ToList();
        WriteBytesField(invalidUtf8Account, 2, [0xC3, 0x28]);
        yield return [CreatePayloadBytes([invalidUtf8Account.ToArray()]).ToArray()];

        var oversizedTextAccount = CreateAccount(name: new string('a', 1025));
        yield return [CreatePayloadBytes([oversizedTextAccount]).ToArray()];
    }

    private static byte[] CreateAccount(
        byte[]? secret = null,
        string name = "Example:alice",
        string issuer = "Example",
        int algorithm = 1,
        int digits = 1,
        int type = 2)
    {
        var account = new List<byte>();
        WriteBytesField(account, 1, secret ?? [1, 1]);
        if (name.Length > 0) WriteStringField(account, 2, name);
        if (issuer.Length > 0) WriteStringField(account, 3, issuer);
        WriteVarintField(account, 4, (ulong)algorithm);
        WriteVarintField(account, 5, (ulong)digits);
        WriteVarintField(account, 6, (ulong)type);
        return account.ToArray();
    }

    private static string CreatePayload(
        IReadOnlyList<byte[]> accounts,
        int version = 1,
        int batchSize = 1,
        int batchIndex = 0,
        bool urlSafeWithoutPadding = false)
    {
        var protobuf = CreatePayloadBytes(accounts, version, batchSize, batchIndex);
        return ToUri(protobuf, urlSafeWithoutPadding);
    }

    private static List<byte> CreatePayloadBytes(
        IReadOnlyList<byte[]> accounts,
        int version = 1,
        int batchSize = 1,
        int batchIndex = 0)
    {
        var payload = new List<byte>();
        foreach (var account in accounts) WriteBytesField(payload, 1, account);
        WriteVarintField(payload, 2, (ulong)version);
        WriteVarintField(payload, 3, (ulong)batchSize);
        WriteVarintField(payload, 4, (ulong)batchIndex);
        WriteVarintField(payload, 5, 42);
        return payload;
    }

    private static string ToUri(IReadOnlyCollection<byte> protobuf, bool urlSafeWithoutPadding = false)
    {
        var encoded = Convert.ToBase64String(protobuf.ToArray());
        if (urlSafeWithoutPadding)
            encoded = encoded.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"otpauth-migration://offline?data={Uri.EscapeDataString(encoded)}";
    }

    private static void WriteStringField(List<byte> output, int field, string value) =>
        WriteBytesField(output, field, Encoding.UTF8.GetBytes(value));

    private static void WriteBytesField(List<byte> output, int field, IReadOnlyCollection<byte> value)
    {
        WriteVarint(output, (ulong)((field << 3) | 2));
        WriteVarint(output, (ulong)value.Count);
        output.AddRange(value);
    }

    private static void WriteVarintField(List<byte> output, int field, ulong value)
    {
        WriteVarint(output, (ulong)(field << 3));
        WriteVarint(output, value);
    }

    private static void WriteFixedField(List<byte> output, int field, int byteCount)
    {
        var wireType = byteCount == 8 ? 1 : 5;
        WriteVarint(output, (ulong)((field << 3) | wireType));
        output.AddRange(new byte[byteCount]);
    }

    private static void WriteVarint(List<byte> output, ulong value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }

        output.Add((byte)value);
    }
}
