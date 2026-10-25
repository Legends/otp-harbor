using TOTP.Avalonia.Shared.Presentation;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class RankedTextFilterTests
{
    [Fact]
    public void Apply_OrdersExactThenPrefixThenPartialMatches()
    {
        string[] items =
        [
            "DigitalOcean",
            "GitLab",
            "Legit Security",
            "git",
            "GitHub",
            "Git"
        ];

        var result = RankedTextFilter.Apply(
            items,
            "gIt",
            static item => [item],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["git", "Git", "GitLab", "GitHub", "DigitalOcean", "Legit Security"],
            result);
    }

    [Fact]
    public void Apply_UsesBestMatchAcrossAllSearchableFields()
    {
        SearchItem[] items =
        [
            new("First", "service-target", "first.svg"),
            new("Target Cloud", "cloud", "cloud.svg"),
            new("Exact by identifier", "target", "exact.svg")
        ];

        var result = RankedTextFilter.Apply(
            items,
            "target",
            static item => [item.DisplayName, item.Id, item.FileName],
            TestContext.Current.CancellationToken);

        Assert.Equal([items[2], items[1], items[0]], result);
    }

    [Fact]
    public void ApplyTerms_RequiresEveryTermAndRanksExactPrefixThenPartialMatches()
    {
        SearchItem[] items =
        [
            new("DigitalOcean", "alice", "work"),
            new("GitLab", "alice", "work"),
            new("Git", "alice", "work"),
            new("GitHub", "bob", "work"),
            new("GitHub", "alice", "private")
        ];

        var result = RankedTextFilter.ApplyTerms(
            items,
            "git alice",
            static item => [item.DisplayName, item.Id, item.FileName],
            TestContext.Current.CancellationToken);

        Assert.Equal([items[2], items[1], items[4], items[0]], result);
    }

    [Fact]
    public void ApplyTerms_IndexedFieldsVisitsEachFieldOncePerItem()
    {
        var items = Enumerable.Range(0, 600)
            .Select(index => new SearchItem($"Issuer {index:D3}", $"user-{index:D3}", "Work"))
            .ToArray();
        var fieldReads = 0;

        var result = RankedTextFilter.ApplyTerms(
            items,
            "Issuer 599",
            static _ => 3,
            (item, index) =>
            {
                fieldReads++;
                return index switch
                {
                    0 => item.DisplayName,
                    1 => item.Id,
                    _ => item.FileName
                };
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(items.Length * 3, fieldReads);
        Assert.Same(items[599], Assert.Single(result));
    }

    [Fact]
    public void ApplyTerms_CancelledSearchStopsBeforeReadingFields()
    {
        SearchItem[] items = [new("GitHub", "alice", "Work")];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var fieldReads = 0;

        Assert.Throws<OperationCanceledException>(() => RankedTextFilter.ApplyTerms(
            items,
            "git",
            static _ => 3,
            (_, _) =>
            {
                fieldReads++;
                return "GitHub";
            },
            cancellation.Token));

        Assert.Equal(0, fieldReads);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_EmptyQueryPreservesOriginalOrder(string query)
    {
        string[] items = ["Contains Git", "GitHub", "Git"];

        var result = RankedTextFilter.Apply(
            items,
            query,
            static item => [item],
            TestContext.Current.CancellationToken);

        Assert.Equal(items, result);
    }

    private sealed record SearchItem(string DisplayName, string Id, string FileName);
}
