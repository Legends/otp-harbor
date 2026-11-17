using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;
using QRCoder;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

if (args.Length != 1) throw new ArgumentException("Pass a new, empty output directory.");
await new ImportFixtureKit(Path.GetFullPath(args[0])).BuildAsync();

sealed class ImportFixtureKit(string root)
{
    private const string BackupPassword = "OtpHarbor-Test-2026!";
    private readonly string tag = DateTime.UtcNow.ToString("HHmmss");
    private readonly ExportService reader = new(NullLogger<ExportService>.Instance);
    private readonly List<object> cases = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly (string Folder, string Label, string Extension)[] Providers =
    [
        ("Aegis", "Aegis", ".json"), ("2FAS", "2FAS", ".2fas"),
        ("Bitwarden-Authenticator", "BW Auth", ".json"), ("Bitwarden-Password-Manager", "BW Vault", ".json"),
        ("Ente-Auth", "Ente", ".txt"), ("Proton-Authenticator", "Proton", ".json"),
        ("LastPass-Authenticator", "LastPass", ".json"), ("OTP-Harbor", "OTP", ".json")
    ];

    public async Task BuildAsync()
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("The output directory must be empty; existing files were not changed.");
        Directory.CreateDirectory(root);
        foreach (var provider in Providers)
        {
            var accounts = Accounts(provider.Label);
            var changed = Changed(accounts, provider.Label);
            var invalid = accounts.ToArray();
            invalid[0] = Copy(accounts[0], secret: "INVALID-SEED!");
            await FileCase(provider.Folder, "01-clean" + provider.Extension, Encode(provider.Folder, accounts), accounts, "clean");
            await FileCase(provider.Folder, "02-exact-duplicates" + provider.Extension, Encode(provider.Folder, accounts), accounts, "duplicates", accounts);
            await FileCase(provider.Folder, "03-conflicts-and-new" + provider.Extension, Encode(provider.Folder, changed), changed, "conflicts", accounts);
            await FileCase(provider.Folder, "04-invalid-mixed" + provider.Extension, Encode(provider.Folder, invalid), [], "rejected");
            await FileCase(provider.Folder, "05-clean-renamed.data", Encode(provider.Folder, accounts), accounts, "clean");
            if (provider.Folder != "OTP-Harbor")
                await FileCase(provider.Folder, "06-encrypted-marker-rejection.json", EncryptedMarker(provider.Folder), [], "rejected");
            await ExtraCases(provider.Folder, accounts);
            Write(provider.Folder + "/README.txt", ProviderGuide(provider.Folder, accounts));
        }
        await GoogleCases();
        await MigrationGuide("Microsoft-Authenticator", "MS");
        await MigrationGuide("Authy", "Authy");
        Write("START-HERE.txt", $"""
            OTP HARBOR - SYNTHETIC AUTHENTICATOR IMPORT TEST KIT
            Generated UTC: {DateTime.UtcNow:O}
            Account prefix for this run: QA-{tag}

            All seeds, names and passwords are artificial test data. These files are
            generated from supported schemas, not exports collected from real accounts
            or every vendor/version. Do not use these published test seeds for real logins.

            Open Settings > Data & backup > Import accounts > Import from file.
            Browse Downloads > OTP-Harbor-Import-Tests (or the timestamped kit folder).
            Google and migration-guide PNG files use Import QR code > Choose image.

            EACH PROVIDER IS INDEPENDENT. No fresh vault or deletion of existing data is required.
            Its QA issuer names contain the provider and this run's prefix.
            1. Import 01-clean: six new accounts; confirm 'Import now?'.
            2. Import 02-exact-duplicates: only duplicates, nothing to import.
            3. Open 03-conflicts-and-new: seven entries; three changed matches,
               three exact duplicates, one new account. Review Replace / Keep both / Skip.
               Cancel first to prove no changes. Committing changes the QA baseline.
            4. Import 04-invalid-mixed: reject the entire file, no partial account writes.
            5. Select 05-clean-renamed.data: detect by content; with baseline imported,
               expect six duplicates. The suffix must not choose the parser.
            Read each folder's README for additional cases and full parameter tables.

            OTP Harbor encrypted fixtures use backup password: {BackupPassword}
            Foreign encrypted-marker files must be rejected without a native-backup
            password prompt. Their ciphertext is a synthetic rejection marker, not a
            decryptable vendor backup. Foreign encrypted import is intentionally unsupported.

            Microsoft/Authy folders contain standard TOTP re-enrolment examples and
            migration instructions, not native Authy exports or Microsoft cloud backups.
            Google export QR has a fixed 30-second period (the migration wire format
            has no period field). No screenshot or real account export was copied.

            This generator checked decoding/parameters, clean/duplicate/conflict previews
            and cancellation through production services with an in-memory-only store.
            It never opens, imports into or changes the app's actual vault.
            Test expectations assume you cancel 03 unless explicitly testing a commit.
            After testing you can search QA-{tag} to identify this kit's accounts.
            """);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order()
            .Select(path => new { path = Path.GetRelativePath(root, path).Replace('\\', '/'), sha256 = Hash(path), bytes = new FileInfo(path).Length }).ToArray();
        Write("manifest.json", JsonSerializer.Serialize(new { generatedUtc = DateTime.UtcNow, prefix = "QA-" + tag,
            synthetic = true, accountWrites = "in-memory only", nativeBackupPassword = BackupPassword, cases, files }, Json));
        Console.WriteLine($"Created and validated {cases.Count} cases in {Directory.GetDirectories(root).Length} provider folders: {root}");
    }

    private Account[] Accounts(string provider, bool google = false) =>
    [
        Make(provider, 0, "GitHub", "alice@example.test", "SHA1", 6, 30),
        Make(provider, 1, "Workspace", "bob@example.test", "SHA256", 8, google ? 30 : 60),
        Make(provider, 2, "Personal", null, "SHA512", 8, google ? 30 : 60),
        Make(provider, 3, "Büro", "jörg@example.test", "SHA512", 6, google ? 30 : 120),
        Make(provider, 4, "Cloud", "home@example.test", "SHA1", 6, 30),
        Make(provider, 5, "Cloud", "work@example.test", "SHA256", 8, google ? 30 : 60)
    ];

    private Account Make(string provider, int number, string issuer, string? name, string algorithm, int digits, int period)
    {
        var bytes = SHA256.HashData(Utf8.GetBytes($"OTP Harbor synthetic fixture {tag}/{provider}/{number}"));
        return new Account(new Guid(bytes.AsSpan(0, 16)), $"QA-{tag} {provider} {issuer}",
            Base32Encoding.ToString(bytes.AsSpan(0, 20).ToArray()), name, period,
            isFavorite: (provider.StartsWith("BW") || provider == "OTP") && number == 0, algorithm: algorithm, digits: digits);
    }

    private Account[] Changed(Account[] accounts, string provider, bool google = false)
    {
        var changed = accounts.ToList();
        changed[0] = Copy(accounts[0], secret: Make(provider, 100, "x", null, "SHA1", 6, 30).Secret);
        changed[1] = Copy(accounts[1], algorithm: "SHA512", digits: 6, period: google ? 30 : 90);
        changed[2] = Copy(accounts[2], secret: Make(provider, 102, "x", null, "SHA1", 6, 30).Secret);
        changed.Add(Make(provider, 6, "New service", "new@example.test", "SHA1", 6, 30));
        return changed.ToArray();
    }

    private static Account Copy(Account a, string? secret = null, string? algorithm = null, int? digits = null, int? period = null) =>
        new(a.ID, a.Issuer, secret ?? a.Secret, a.AccountName, period ?? a.PeriodSeconds,
            isFavorite: a.IsFavorite, algorithm: algorithm ?? a.Algorithm, digits: digits ?? a.Digits);
    private static string UriFor(Account a) => $"otpauth://totp/{Uri.EscapeDataString(a.Issuer)}:{Uri.EscapeDataString(a.AccountName ?? "")}?secret={Uri.EscapeDataString(a.Secret)}&issuer={Uri.EscapeDataString(a.Issuer)}&algorithm={a.Algorithm}&digits={a.Digits}&period={a.PeriodSeconds}";
    private static byte[] Encode(string provider, IReadOnlyList<Account> accounts, int version = 4) => Utf8.GetBytes(provider switch
    {
        "Aegis" => JsonSerializer.Serialize(new { version = 1, header = new { slots = (object?)null, @params = (object?)null },
            db = new { version = version == 4 ? 3 : version, entries = accounts.Select(a => new { type = "totp", uuid = a.ID,
                issuer = a.Issuer, name = a.AccountName ?? "", note = "Synthetic test data", favorite = false, icon = (string?)null,
                info = new { secret = a.Secret, algo = a.Algorithm, digits = a.Digits, period = a.PeriodSeconds }, groups = Array.Empty<string>() }), groups = Array.Empty<object>() } }, Json),
        "2FAS" => JsonSerializer.Serialize(new { schemaVersion = version, appOrigin = "ios", appVersionCode = 0, appVersionName = "synthetic",
            services = accounts.Select((a, i) => new { name = a.Issuer, secret = a.Secret, type = "custom", updatedAt = 0L,
                order = new { position = i }, otp = new { account = a.AccountName, issuer = a.Issuer, algorithm = a.Algorithm, digits = a.Digits,
                    period = a.PeriodSeconds, tokenType = "TOTP", source = "manual", link = (string?)null } }),
            servicesEncrypted = (string?)null, reference = (string?)null, groups = Array.Empty<object>() }, Json),
        "Bitwarden-Authenticator" or "Bitwarden-Password-Manager" => JsonSerializer.Serialize(new { encrypted = false,
            items = accounts.Select(a => new { id = a.ID, type = 1, name = a.Issuer, favorite = a.IsFavorite,
                login = new { username = a.AccountName, password = "synthetic-ignored", totp = UriFor(a) } }) }, Json),
        "Ente-Auth" => string.Join('\n', accounts.Select(a => EnteUri(a, false))) + "\n",
        "Proton-Authenticator" => JsonSerializer.Serialize(new { version = 1, entries = accounts.Select(a => new {
            id = a.ID.ToString(), note = "Synthetic note, not imported", content = new { entry_type = "Totp", uri = UriFor(a), name = a.Issuer } }) }, Json),
        "LastPass-Authenticator" => JsonSerializer.Serialize(new { version = 1, accounts = accounts.Select(a => new {
            issuerName = a.Issuer, userName = a.AccountName ?? "", secret = a.Secret, algorithm = a.Algorithm, digits = a.Digits, timeStep = a.PeriodSeconds }) }, Json),
        "OTP-Harbor" => JsonSerializer.Serialize(accounts, Json),
        _ => throw new ArgumentException("Unknown fixture provider.")
    });
    private static string EnteUri(Account a, bool trashed) => UriFor(a) + "&codeDisplay=" + Uri.EscapeDataString(
        JsonSerializer.Serialize(new { trashed, pinned = false, note = "Ignored note: &period=1 # Ü,雪", tags = new[] { "synthetic" } }));
    private static byte[] EncryptedMarker(string provider) => Utf8.GetBytes(provider switch
    {
        "Aegis" => "{\"version\":1,\"header\":{\"slots\":[]},\"db\":\"synthetic-rejection-marker\"}",
        "2FAS" => "{\"schemaVersion\":4,\"services\":[],\"servicesEncrypted\":\"synthetic-rejection-marker\",\"reference\":\"synthetic-rejection-marker\"}",
        "Bitwarden-Authenticator" or "Bitwarden-Password-Manager" => "{\"encrypted\":true,\"passwordProtected\":true,\"items\":[]}",
        "Ente-Auth" => "{\"version\":1,\"kdfParams\":{\"salt\":\"synthetic\",\"memLimit\":4096,\"opsLimit\":3},\"encryptedData\":\"synthetic\",\"encryptionNonce\":\"synthetic\"}",
        "Proton-Authenticator" => "{\"version\":1,\"salt\":\"synthetic\",\"content\":\"synthetic\"}",
        _ => "{\"encrypted\":true,\"accounts\":[]}"
    });

    private async Task ExtraCases(string provider, Account[] accounts)
    {
        if (provider == "2FAS")
            for (var v = 1; v <= 3; v++) await FileCase(provider, $"07-schema-{v}" + (v == 1 ? ".bak" : ".2fas"), Encode(provider, accounts, v), accounts, "clean");
        if (provider == "Aegis")
            for (var v = 1; v <= 2; v++) await FileCase(provider, $"07-database-v{v}.json", Encode(provider, accounts, v), accounts, "clean");
        if (provider.StartsWith("Bitwarden"))
        {
            var csv = "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n"
                + string.Join("\n\n", accounts.Select(a => string.Join(',', new[] { "", a.IsFavorite ? "1" : "", "login", a.Issuer, "", "", "0", "", a.AccountName ?? "", "", UriFor(a) }.Select(Csv)))) + "\n";
            await FileCase(provider, "08-ios-or-password-manager.csv", Utf8.GetBytes(csv), accounts, "clean");
            if (provider == "Bitwarden-Authenticator")
            {
                var android = "folder,favorite,type,name,login_uri,login_totp\n" + string.Join('\n', accounts.Select(a =>
                    string.Join(',', new[] { "", "", "1", a.Issuer, "", UriFor(a), a.Issuer, a.PeriodSeconds.ToString(), a.Digits.ToString() }.Select(Csv)))) + "\n";
                await FileCase(provider, "07-android-six-header-nine-values.csv", Utf8.GetBytes(android), accounts, "clean", compareFavorite: false);
            }
        }
        if (provider == "Ente-Auth")
        {
            var deleted = accounts.Take(2).Select(a => EnteUri(a, true));
            await FileCase(provider, "07-active-and-deleted.txt", Utf8.GetBytes(string.Join('\n', accounts.Select(a => EnteUri(a, false)).Concat(deleted))), accounts, "clean", trashed: 2);
            await FileCase(provider, "08-only-deleted.txt", Utf8.GetBytes(string.Join('\n', deleted)), [], "deleted-only", trashed: 2);
        }
        if (provider == "OTP-Harbor")
        {
            foreach (var format in new[] { ExportFileFormat.Csv, ExportFileFormat.Txt })
            {
                using var output = new MemoryStream();
                Require((await reader.ExportToStreamAsync(accounts, output, format)).IsSuccess, "Native plaintext export failed.");
                await FileCase(provider, "06-native." + format.ToString().ToLowerInvariant(), output.ToArray(), accounts, "clean", compareFavorite: false);
            }
            using var encrypted = new MemoryStream();
            byte[] bytes;
            for (var attempt = 0; ; attempt++)
            {
                Require(attempt < 32, "Could not generate a compatible synthetic legacy fixture.");
                encrypted.SetLength(0);
                Require((await reader.ExportToEncryptedStreamAsync(accounts, BackupPassword, encrypted, ExportFileFormat.Json)).IsSuccess, "Native encrypted export failed.");
                bytes = encrypted.ToArray();
                if (!Enum.IsDefined((ExportFileFormat)bytes[5])) break;
            }
            await FileCase(provider, "07-encrypted.otphbp", bytes, accounts, "clean", nativeBackup: true);
            var tampered = bytes.ToArray(); tampered[^1] ^= 1;
            await FileCase(provider, "08-tampered.otphbp", tampered, [], "rejected", nativeBackup: true);
            var legacy = bytes.Take(4).Concat(bytes.Skip(5)).ToArray();
            await FileCase(provider, "09-legacy-unversioned.totp", legacy, accounts, "clean", nativeBackup: true);
        }
    }

    private async Task FileCase(string provider, string name, byte[] bytes, IReadOnlyList<Account> expected, string scenario,
        IReadOnlyList<Account>? baseline = null, int trashed = 0, bool nativeBackup = false, bool compareFavorite = true)
    {
        var path = provider + "/" + name;
        Write(path, bytes);
        using var input = new MemoryStream(bytes);
        var result = await reader.ImportFromStreamWithFormatSelectionAsync(input, name,
            (_, _) => throw new InvalidOperationException("Unexpected ambiguous synthetic fixture."), async (request, token) =>
            {
                Require(nativeBackup && !request.UsesMasterPassword, "Foreign fixture unexpectedly requested a native-backup password.");
                Require((await request.TryReadAsync("incorrect-synthetic-password", token)).IsFailed, "Wrong fixture password accepted.");
                return await request.TryReadAsync(BackupPassword, token);
            });
        if (scenario == "rejected") Require(result.IsFailed, "Invalid fixture was accepted: " + path);
        else
        {
            Require(result.IsSuccess, "Valid fixture was rejected: " + path);
            CheckAccounts(result.Value, expected, compareFavorite, nativeBackup);
            var summary = result.Successes.OfType<AccountImportReadSummary>().Single();
            Require(summary.TrashedCount == trashed, "Deleted-entry diagnostic mismatch: " + path);
            if (scenario != "deleted-only")
                await CheckFilePreview(result.Value, baseline ?? [], scenario, nativeBackup);
        }
        cases.Add(new { path, scenario, validAccounts = expected.Count, trashed, nativeBackup,
            expected = Expectation(scenario, expected.Count, trashed) });
    }

    private static async Task CheckFilePreview(IReadOnlyList<Account> incoming, IReadOnlyList<Account> baseline, string scenario, bool native)
    {
        var store = new MemoryAccounts(baseline);
        var service = new AccountImportService(store);
        var called = false;
        Task<AccountImportResolution?> Inspect(AccountImportPreview p, CancellationToken _)
        {
            called = true;
            Require(p.TotalCount == incoming.Count, "Preview total mismatch.");
            Require(scenario switch { "clean" => p.NewCount == incoming.Count && p.ConflictCount == 0,
                "duplicates" => p.UnchangedCount == incoming.Count && p.ChangedConflicts.Count == 0,
                "conflicts" => p.NewCount == 1 && p.UnchangedCount == 3 && p.ChangedConflicts.Count == 3, _ => false }, "Preview classification mismatch.");
            return Task.FromResult<AccountImportResolution?>(null);
        }
        var result = native ? await service.RestoreBackupWithConflictResolutionAsync(incoming, Inspect)
            : await service.ImportWithConflictResolutionAsync(incoming, Inspect);
        Require(called && result.IsSuccess && store.Writes == 0 && store.Backups == 0, "Cancelled preview wrote fixture accounts.");
    }

    private async Task GoogleCases()
    {
        const string provider = "Google-Authenticator";
        var accounts = Accounts("Google", google: true);
        await QrCase(provider, "01-clean-v2.png", Migration(accounts, 2), accounts, "clean");
        await QrCase(provider, "02-exact-duplicates.png", Migration(accounts, 2), accounts, "duplicates", accounts);
        var changed = Changed(accounts, "Google", google: true);
        await QrCase(provider, "03-conflicts-and-new.png", Migration(changed, 2), changed, "conflicts", accounts);
        await QrCase(provider, "04-clean-v1.png", Migration(accounts, 1), accounts, "clean");
        await QrCase(provider, "05-unsupported-algorithm.png", Migration(accounts, 2, invalidAlgorithm: true), [], "rejected");
        Write(provider + "/README.txt", "Use Import QR code > Choose image for these PNGs, not Import from file.\n"
            + ProviderGuide(provider, accounts) + "\nGoogle QR preserves SHA/digits but always uses period 30 seconds.\n");
    }

    private async Task MigrationGuide(string provider, string label)
    {
        var account = Make(label, 0, "Standard TOTP setup", "test@example.test", "SHA1", 6, 30);
        await QrCase(provider, "standard-totp-setup-example.png", UriFor(account), [account], "clean");
        Write(provider + "/README.txt", $"""
            {provider}: MIGRATION GUIDANCE, NOT A NATIVE EXPORT TEST
            This folder does not claim support for a vendor backup/export format.
            Re-enrol standard TOTP at the original service and scan its new setup QR.
            The PNG here is a synthetic standard TOTP setup example for testing that route.
            Use Import QR code > Choose image. Expected: one new {account.Issuer} account,
            SHA1 / 6 digits / 30 seconds. Repeat: exact duplicate, nothing to import.
            Authy-specific tokens, Microsoft push/passwordless approvals and cloud backups
            are not interchangeable with ordinary TOTP setup codes.
            """);
    }

    private async Task QrCase(string provider, string name, string payload, IReadOnlyList<Account> expected, string scenario, IReadOnlyList<Account>? baseline = null)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        var path = provider + "/" + name;
        Write(path, png.GetGraphic(8));
        var store = new MemoryAccounts(baseline ?? []);
        var service = new QrAccountImportService(store);
        var called = false;
        var result = await service.ImportWithConflictResolutionAsync(payload, (preview, _) =>
        {
            called = true;
            Require(scenario switch { "clean" => preview.NewCount == expected.Count,
                "duplicates" => preview.UnchangedCount == expected.Count && preview.ChangedConflicts.Count == 0,
                "conflicts" => preview.NewCount == 1 && preview.UnchangedCount == 3 && preview.ChangedConflicts.Count == 3, _ => false }, "QR preview mismatch.");
            return Task.FromResult<QrAccountImportResolution?>(scenario == "clean" ? new([]) : null);
        });
        if (scenario == "rejected") Require(result.IsFailed && !called && store.Writes == 0, "Unsupported QR accepted.");
        else
        {
            Require(result.IsSuccess && called, "QR fixture validation failed.");
            if (scenario == "clean") CheckAccounts(store.Data, expected, false, false);
            else Require(store.Writes == 0, "Cancelled QR review wrote accounts.");
        }
        cases.Add(new { path, scenario, validAccounts = expected.Count, expected = Expectation(scenario, expected.Count, 0), method = "Import QR code / image" });
    }

    private static string Migration(IEnumerable<Account> accounts, int version, bool invalidAlgorithm = false)
    {
        using var payload = new MemoryStream();
        foreach (var account in accounts)
        {
            using var entry = new MemoryStream();
            Bytes(entry, 1, Base32Encoding.ToBytes(account.Secret));
            Bytes(entry, 2, Utf8.GetBytes(account.AccountName ?? ""));
            Bytes(entry, 3, Utf8.GetBytes(account.Issuer));
            Integer(entry, 4, invalidAlgorithm ? 4 : account.Algorithm switch { "SHA1" => 1, "SHA256" => 2, _ => 3 });
            Integer(entry, 5, account.Digits == 8 ? 2 : 1);
            Integer(entry, 6, 2);
            Bytes(payload, 1, entry.ToArray());
        }
        Integer(payload, 2, version); Integer(payload, 3, 1); Integer(payload, 4, 0); Integer(payload, 5, 31415);
        return "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload.ToArray()));
    }
    private static void Integer(Stream s, int field, int value) { Varint(s, field << 3); Varint(s, value); }
    private static void Bytes(Stream s, int field, byte[] value) { Varint(s, (field << 3) | 2); Varint(s, value.Length); s.Write(value); }
    private static void Varint(Stream s, int value) { var n = (uint)value; while (n >= 128) { s.WriteByte((byte)(n | 128)); n >>= 7; } s.WriteByte((byte)n); }
    private static void CheckAccounts(IReadOnlyList<Account> actual, IReadOnlyList<Account> expected, bool favorites, bool ids)
    {
        Require(actual.Count == expected.Count, "Decoded account count mismatch.");
        foreach (var account in expected)
        {
            var decoded = actual.Single(a => a.Issuer == account.Issuer && (a.AccountName ?? "") == (account.AccountName ?? ""));
            Require(decoded.Secret == account.Secret && decoded.Algorithm == account.Algorithm && decoded.Digits == account.Digits
                && decoded.PeriodSeconds == account.PeriodSeconds && (!favorites || decoded.IsFavorite == account.IsFavorite)
                && (!ids || decoded.ID == account.ID), "Decoded account fields mismatch.");
        }
    }
    private string ProviderGuide(string provider, Account[] accounts)
    {
        var files = cases.Select(value => JsonSerializer.SerializeToElement(value))
            .Where(value => value.GetProperty("path").GetString()!.StartsWith(provider + "/", StringComparison.Ordinal))
            .Select(value => Path.GetFileName(value.GetProperty("path").GetString()) + ": " + value.GetProperty("expected").GetString());
        return $"""
        {provider} - SYNTHETIC COMPATIBILITY FIXTURES - prefix QA-{tag}
        Not real account data or an app-generated export from every vendor/version.

        METHOD: {(provider == "Google-Authenticator" ? "Import QR code > Choose image (PNG files)" : "Import from file")}
        Run 01, then 02; open 03 and CANCEL initially to preserve the baseline.
        01: six new accounts, Import now? confirmation. Import to establish baseline.
        02: six exact duplicates, nothing to import. No conflict review needed.
        03: seven entries = three changed matches, three exact duplicates, one new.
            The changed matches include an unnamed account with an equal issuer.
            Try Replace / Keep both / Skip or bulk actions, then cancel or commit.

        FILE-BY-FILE EXPECTATIONS
        {string.Join('\n', files)}

        Extra valid schema/CSV/QR variants contain the same six identities. Their
        'new' counts assume an empty baseline; after 01 they are duplicates.
        Deleted exclusions remain visible even if the six active entries already exist.
        Committing 03 changes the QA baseline and therefore later comparison results.
        {(provider == "OTP-Harbor" ? $"Encrypted .otphbp / .totp password: {BackupPassword}. Try a wrong password, then retry. Native CSV/TXT do not carry favourite flags." : "Foreign encrypted-marker cases are rejection tests, not decryptable backups.")}

        EXPECTED BASELINE (issuer | account | algorithm | digits | period seconds)
        {string.Join('\n', accounts.Select(a => $"{a.Issuer} | {a.AccountName ?? "(empty)"} | {a.Algorithm} | {a.Digits} | {a.PeriodSeconds}"))}

        The two Cloud entries share an issuer but have different account names:
        both are separate valid accounts and must not create a conflict with each other.
        All personal notes, passwords and display metadata in files are synthetic.
        """;
    }
    private static string Expectation(string scenario, int count, int trashed) => scenario switch
    {
        "clean" => $"{count} eligible new accounts against an empty baseline" + (trashed > 0 ? $"; {trashed} deleted excluded with visible diagnostics" : ""),
        "duplicates" => $"{count} exact duplicates after 01; nothing to import",
        "conflicts" => "3 changed matches + 3 exact duplicates + 1 new after 01; review required",
        "deleted-only" => "No active accounts; informational acknowledgement; no vault writes",
        _ => "Reject entire input; no partial import"
    };
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private void Write(string relative, string text) => Write(relative, Utf8.GetBytes(text));
    private void Write(string relative, byte[] bytes) { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

sealed class MemoryAccounts(IReadOnlyList<Account> initial) : IAccountManager
{
    public List<Account> Data { get; private set; } = [.. initial];
    public int Writes { get; private set; }
    public int Backups { get; private set; }
    public Task<Result<IReadOnlyList<Account>>> GetAllOtpEntriesSortedAsync() => Task.FromResult(Result.Ok<IReadOnlyList<Account>>(Data.ToArray()));
    public Task<Result> BackupOtpEntriesStorageFileAsync() { Backups++; return Task.FromResult(Result.Ok()); }
    public Task<Result> CommitImportAsync(IReadOnlyCollection<Account> accounts) { Data = [.. accounts]; Writes++; return Task.FromResult(Result.Ok()); }
    public Task<Result> AddNewAsync(Account account) { Data.Add(account); Writes++; return Task.FromResult(Result.Ok()); }
    public Task<Result> UpdateAsync(Account previous, Account updated) => throw new NotSupportedException();
    public Task<Result> SaveFavoritesAsync(IReadOnlyCollection<Guid> ids) => throw new NotSupportedException();
    public Task<Result> SaveGroupAsync(AccountGroup group, IReadOnlyCollection<Guid> ids) => throw new NotSupportedException();
    public Task<Result> DeleteGroupAsync(Guid id) => throw new NotSupportedException();
    public Task<Result> DeleteAsync(Account account) => throw new NotSupportedException();
}
