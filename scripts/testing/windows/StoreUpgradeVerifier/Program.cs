using System.Security.Cryptography;
using System.Text;
using OtpHarbor.Testing.StoreUpgrade;

if (args.Length == 1 && args[0] == "self-check")
{
    var bytes = RandomNumberGenerator.GetBytes(32);
    try
    {
        using var key = NSec.Cryptography.Key.Import(NSec.Cryptography.AeadAlgorithm.Aes256Gcm,
            bytes, NSec.Cryptography.KeyBlobFormat.RawSymmetricKey);
        var nonce = new byte[12];
        var encrypted = NSec.Cryptography.AeadAlgorithm.Aes256Gcm.Encrypt(key, nonce, ReadOnlySpan<byte>.Empty, "check"u8);
        var decrypted = NSec.Cryptography.AeadAlgorithm.Aes256Gcm.Decrypt(key, nonce, ReadOnlySpan<byte>.Empty, encrypted);
        if (decrypted is null || !decrypted.AsSpan().SequenceEqual("check"u8)) return 1;
        CryptographicOperations.ZeroMemory(decrypted);
        Console.WriteLine("Verifier runtime and native cryptography ready.");
        return 0;
    }
    catch (Exception) { return 1; }
    finally { CryptographicOperations.ZeroMemory(bytes); }
}

if ((args.Length == 2 && args[0] == "seed-v2") || (args.Length == 5 && args[0] == "synthetic-v3"))
{
    try
    {
        if (args[0] == "seed-v2") await SyntheticV2Vault.CreateAsync(args[1]);
        else await UpgradeVerifier.VerifyV3Async(args[1], args[2], args[3], args[4], SyntheticV2Vault.Password);
        Console.WriteLine(args[0] == "seed-v2"
            ? "PASS: encrypted synthetic v2 vault and password envelope created and authenticated."
            : "PASS: all four original accounts, IDs and fields preserved in v3; original password and rollback verified.");
        return 0;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("FAIL: synthetic vault creation or verification failed. Existing files were not overwritten.");
        return 1;
    }
}

if (args.Length == 2 && args[0] == "fixture")
{
    SyntheticV2Vault.WriteImportFixture(args[1]);
    return 0;
}

if (!((args.Length == 3 && args[0] == "v2") || (args.Length == 5 && args[0] == "v3")))
{
    Console.Error.WriteLine("Use the kit's Invoke-StoreUpgradeTest.ps1 script.");
    return 1;
}

try
{
    Console.Write("Original synthetic-vault password (not recorded): ");
    var password = ReadPassword();
    try
    {
        if (args[0] == "v2") await UpgradeVerifier.VerifyV2Async(args[1], args[2], password);
        else await UpgradeVerifier.VerifyV3Async(args[1], args[2], args[3], args[4], password);
    }
    finally { password = string.Empty; }
    Console.WriteLine("PASS: four synthetic accounts verified; no application files were changed.");
    return 0;
}
catch (Exception)
{
    // Never expose exceptions from a vault/crypto reader or print account contents.
    Console.Error.WriteLine("FAIL: upgrade verification failed. Check the password, test phase, file integrity and fixture. No application files were changed.");
    return 1;
}

static string ReadPassword()
{
    if (Console.IsInputRedirected) throw new InvalidOperationException();
    var buffer = new char[1024];
    var length = 0;
    try
    {
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (length > 0) buffer[--length] = '\0'; continue; }
            if (!char.IsControl(key.KeyChar))
            {
                if (length == buffer.Length) throw new InvalidOperationException();
                buffer[length++] = key.KeyChar;
            }
        }
        Console.WriteLine();
        return new string(buffer, 0, length);
    }
    finally { CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer.AsSpan())); }
}
