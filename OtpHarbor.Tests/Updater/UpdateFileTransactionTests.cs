using TOTP.Updater;

namespace TOTP.Tests.Updater;

public sealed class UpdateFileTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"totp-update-transaction-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task ApplyAsync_WhenAllCopiesSucceed_ReplacesAndAddsFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        await File.WriteAllTextAsync(Path.Combine(stage, "existing.txt"), "new", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(stage, "added.txt"), "added", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(target, "existing.txt"), "old", cancellationToken);

        await ApplyAsync(stage, target, cancellationToken);

        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target, "existing.txt"), cancellationToken));
        Assert.Equal("added", await File.ReadAllTextAsync(Path.Combine(target, "added.txt"), cancellationToken));
    }

    [Fact]
    public async Task ApplyAsync_WhenLaterDestinationIsBlocked_RestoresPreviousInstallation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        await File.WriteAllTextAsync(Path.Combine(stage, "a-existing.txt"), "new", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(stage, "b-added.txt"), "temporary", cancellationToken);
        var blockedStage = Directory.CreateDirectory(Path.Combine(stage, "z-blocked")).FullName;
        await File.WriteAllTextAsync(Path.Combine(blockedStage, "child.txt"), "never-installed", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(target, "a-existing.txt"), "old", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(target, "z-blocked"), "blocking-file", cancellationToken);

        await Assert.ThrowsAsync<IOException>(() => ApplyAsync(stage, target, cancellationToken));

        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(target, "a-existing.txt"), cancellationToken));
        Assert.False(File.Exists(Path.Combine(target, "b-added.txt")));
        Assert.Equal("blocking-file", await File.ReadAllTextAsync(Path.Combine(target, "z-blocked"), cancellationToken));
    }

    [Fact]
    public async Task ApplyAsync_WhenCancelledAfterFirstReplacement_RestoresEveryFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        await File.WriteAllTextAsync(Path.Combine(stage, "a-first.txt"), "new-first", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(stage, "b-second.txt"), "new-second", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(target, "a-first.txt"), "old-first", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(target, "b-second.txt"), "old-second", cancellationToken);
        var progress = new InlineProgress<InstallerProgressState>(state =>
        {
            if (state.Detail.StartsWith("2/", StringComparison.Ordinal))
                cancellation.Cancel();
        });
        var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateFileTransaction.ApplyAsync(
            files,
            stage,
            target,
            progress,
            _ => { },
            cancellation.Token));

        Assert.Equal("old-first", await File.ReadAllTextAsync(Path.Combine(target, "a-first.txt"), cancellationToken));
        Assert.Equal("old-second", await File.ReadAllTextAsync(Path.Combine(target, "b-second.txt"), cancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Task ApplyAsync(string stage, string target, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToArray();
        return UpdateFileTransaction.ApplyAsync(
            files,
            stage,
            target,
            new Progress<InstallerProgressState>(),
            _ => { },
            cancellationToken);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
