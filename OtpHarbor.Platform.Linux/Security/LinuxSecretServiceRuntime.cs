using System.Diagnostics;

namespace TOTP.Platform.Linux.Security;

public sealed record LinuxSecretServiceCommandResult(int ExitCode, byte[] StandardOutput);

public interface ILinuxSecretServiceRuntime
{
    bool IsPlatformSupported { get; }
    bool HasSessionBus { get; }
    string? SecretToolPath { get; }
    Task<LinuxSecretServiceCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte> standardInput,
        int maximumOutputBytes,
        CancellationToken cancellationToken);
}

public sealed class LinuxSecretServiceRuntime : ILinuxSecretServiceRuntime
{
    public bool IsPlatformSupported => OperatingSystem.IsLinux();

    public bool HasSessionBus => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));

    public string? SecretToolPath => FindSecretTool();

    public async Task<LinuxSecretServiceCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte> standardInput,
        int maximumOutputBytes,
        CancellationToken cancellationToken)
    {
        var executable = SecretToolPath
            ?? throw new PlatformNotSupportedException("secret-tool is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("secret-tool could not be started.");
        try
        {
            var outputTask = ReadBoundedAsync(
                process.StandardOutput.BaseStream,
                maximumOutputBytes,
                cancellationToken);
            var errorDrainTask = process.StandardError.BaseStream.CopyToAsync(
                Stream.Null,
                cancellationToken);
            if (!standardInput.IsEmpty)
                await process.StandardInput.BaseStream.WriteAsync(standardInput, cancellationToken);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            await errorDrainTask;
            return new LinuxSecretServiceCommandResult(process.ExitCode, output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        var exceeded = false;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (output.Length + read > maximumBytes)
                {
                    exceeded = true;
                    continue;
                }
                if (!exceeded) await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (exceeded) throw new InvalidDataException("Secret Service output exceeded its limit.");
            return output.ToArray();
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    private static string? FindSecretTool()
    {
        if (!OperatingSystem.IsLinux()) return null;
        foreach (var path in new[] { "/usr/bin/secret-tool", "/bin/secret-tool" })
        {
            if (File.Exists(path)) return path;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) return null;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, "secret-tool");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cancellation of a process owned by this adapter.
        }
    }
}
