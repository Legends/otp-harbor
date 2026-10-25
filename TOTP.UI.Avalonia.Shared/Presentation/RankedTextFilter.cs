namespace TOTP.Avalonia.Shared.Presentation;

public static class RankedTextFilter
{
    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T>? items,
        string? query,
        Func<T, IEnumerable<string?>> searchableTextSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchableTextSelector);
        var source = AsReadOnlyList(items);
        var searchText = query?.Trim() ?? string.Empty;
        return searchText.Length == 0
            ? source
            : RankEnumerableFields(source, [searchText], searchableTextSelector, cancellationToken);
    }

    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T>? items,
        string? query,
        Func<T, int> fieldCountSelector,
        Func<T, int, string?> fieldSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fieldCountSelector);
        ArgumentNullException.ThrowIfNull(fieldSelector);
        var source = AsReadOnlyList(items);
        var searchText = query?.Trim() ?? string.Empty;
        return searchText.Length == 0
            ? source
            : RankIndexedFields(source, [searchText], fieldCountSelector, fieldSelector, cancellationToken);
    }

    public static IReadOnlyList<T> ApplyTerms<T>(
        IEnumerable<T>? items,
        string? query,
        Func<T, IEnumerable<string?>> searchableTextSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchableTextSelector);
        var source = AsReadOnlyList(items);
        var terms = SplitTerms(query);
        return terms.Length == 0
            ? source
            : RankEnumerableFields(source, terms, searchableTextSelector, cancellationToken);
    }

    public static IReadOnlyList<T> ApplyTerms<T>(
        IEnumerable<T>? items,
        string? query,
        Func<T, int> fieldCountSelector,
        Func<T, int, string?> fieldSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fieldCountSelector);
        ArgumentNullException.ThrowIfNull(fieldSelector);
        var source = AsReadOnlyList(items);
        var terms = SplitTerms(query);
        return terms.Length == 0
            ? source
            : RankIndexedFields(source, terms, fieldCountSelector, fieldSelector, cancellationToken);
    }

    private static IReadOnlyList<T> RankEnumerableFields<T>(
        IReadOnlyList<T> source,
        IReadOnlyList<string> terms,
        Func<T, IEnumerable<string?>> searchableTextSelector,
        CancellationToken cancellationToken)
    {
        var itemRanks = new MatchRank[source.Count];
        var termRanks = new MatchRank[terms.Count];
        var counts = new int[3];
        for (var itemIndex = 0; itemIndex < source.Count; itemIndex++)
        {
            if ((itemIndex & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(termRanks);
            foreach (var value in searchableTextSelector(source[itemIndex]))
                RankValueForTerms(value, terms, termRanks);
            StoreItemRank(itemRanks, itemIndex, termRanks, counts);
        }

        return MaterializeRanked(source, itemRanks, counts);
    }

    private static IReadOnlyList<T> RankIndexedFields<T>(
        IReadOnlyList<T> source,
        IReadOnlyList<string> terms,
        Func<T, int> fieldCountSelector,
        Func<T, int, string?> fieldSelector,
        CancellationToken cancellationToken)
    {
        var itemRanks = new MatchRank[source.Count];
        var termRanks = new MatchRank[terms.Count];
        var counts = new int[3];
        for (var itemIndex = 0; itemIndex < source.Count; itemIndex++)
        {
            if ((itemIndex & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(termRanks);
            var item = source[itemIndex];
            var fieldCount = Math.Max(0, fieldCountSelector(item));
            for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                RankValueForTerms(fieldSelector(item, fieldIndex), terms, termRanks);
            StoreItemRank(itemRanks, itemIndex, termRanks, counts);
        }

        return MaterializeRanked(source, itemRanks, counts);
    }

    private static void RankValueForTerms(
        string? value,
        IReadOnlyList<string> terms,
        MatchRank[] termRanks)
    {
        if (string.IsNullOrEmpty(value)) return;
        for (var index = 0; index < terms.Count; index++)
        {
            if (termRanks[index] == MatchRank.Exact) continue;
            var rank = RankValue(value, terms[index]);
            if (rank > termRanks[index]) termRanks[index] = rank;
        }
    }

    private static void StoreItemRank(
        MatchRank[] itemRanks,
        int itemIndex,
        IReadOnlyList<MatchRank> termRanks,
        int[] counts)
    {
        var itemRank = MatchRank.Exact;
        for (var termIndex = 0; termIndex < termRanks.Count; termIndex++)
        {
            if (termRanks[termIndex] == MatchRank.None)
            {
                itemRank = MatchRank.None;
                break;
            }

            if (termRanks[termIndex] < itemRank) itemRank = termRanks[termIndex];
        }

        itemRanks[itemIndex] = itemRank;
        if (itemRank != MatchRank.None) counts[(int)itemRank - 1]++;
    }

    private static IReadOnlyList<T> MaterializeRanked<T>(
        IReadOnlyList<T> source,
        IReadOnlyList<MatchRank> itemRanks,
        IReadOnlyList<int> counts)
    {
        var result = new T[counts[0] + counts[1] + counts[2]];
        var partialIndex = counts[2] + counts[1];
        var prefixIndex = counts[2];
        var exactIndex = 0;
        for (var index = 0; index < source.Count; index++)
        {
            switch (itemRanks[index])
            {
                case MatchRank.Exact:
                    result[exactIndex++] = source[index];
                    break;
                case MatchRank.Prefix:
                    result[prefixIndex++] = source[index];
                    break;
                case MatchRank.Partial:
                    result[partialIndex++] = source[index];
                    break;
            }
        }

        return result;
    }

    private static MatchRank RankValue(string value, string query)
    {
        if (string.Equals(value, query, StringComparison.CurrentCultureIgnoreCase))
            return MatchRank.Exact;
        if (value.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            return MatchRank.Prefix;
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            ? MatchRank.Partial
            : MatchRank.None;
    }

    private static IReadOnlyList<T> AsReadOnlyList<T>(IEnumerable<T>? items) => items switch
    {
        null => Array.Empty<T>(),
        IReadOnlyList<T> list => list,
        _ => items.ToArray()
    };

    private static string[] SplitTerms(string? query) => (query ?? string.Empty).Split(
        (char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private enum MatchRank : byte
    {
        None,
        Partial,
        Prefix,
        Exact
    }
}
