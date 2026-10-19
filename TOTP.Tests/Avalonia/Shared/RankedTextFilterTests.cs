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

        var result = RankedTextFilter.Apply(items, "gIt", static item => [item]);

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
            static item => [item.DisplayName, item.Id, item.FileName]);

        Assert.Equal([items[2], items[1], items[0]], result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_EmptyQueryPreservesOriginalOrder(string query)
    {
        string[] items = ["Contains Git", "GitHub", "Git"];

        var result = RankedTextFilter.Apply(items, query, static item => [item]);

        Assert.Equal(items, result);
    }

    private sealed record SearchItem(string DisplayName, string Id, string FileName);
}
