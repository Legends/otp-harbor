namespace TOTP.Avalonia.Shared.Presentation;

public static class RankedTextFilter
{
    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T>? items,
        string? query,
        Func<T, IEnumerable<string?>> searchableTextSelector)
    {
        ArgumentNullException.ThrowIfNull(searchableTextSelector);

        var source = items?.ToArray() ?? [];
        var searchText = query?.Trim() ?? string.Empty;
        if (searchText.Length == 0) return source;

        var exactMatches = new List<T>();
        var prefixMatches = new List<T>();
        var partialMatches = new List<T>();

        foreach (var item in source)
        {
            switch (GetBestMatch(item, searchText, searchableTextSelector))
            {
                case MatchRank.Exact:
                    exactMatches.Add(item);
                    break;
                case MatchRank.Prefix:
                    prefixMatches.Add(item);
                    break;
                case MatchRank.Partial:
                    partialMatches.Add(item);
                    break;
            }
        }

        return [.. exactMatches, .. prefixMatches, .. partialMatches];
    }

    private static MatchRank GetBestMatch<T>(
        T item,
        string query,
        Func<T, IEnumerable<string?>> searchableTextSelector)
    {
        var bestMatch = MatchRank.None;
        foreach (var value in searchableTextSelector(item))
        {
            if (string.IsNullOrEmpty(value)) continue;

            if (string.Equals(value, query, StringComparison.CurrentCultureIgnoreCase))
                return MatchRank.Exact;

            if (value.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            {
                bestMatch = MatchRank.Prefix;
                continue;
            }

            if (bestMatch == MatchRank.None
                && value.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                bestMatch = MatchRank.Partial;
            }
        }

        return bestMatch;
    }

    private enum MatchRank
    {
        None,
        Partial,
        Prefix,
        Exact
    }
}
