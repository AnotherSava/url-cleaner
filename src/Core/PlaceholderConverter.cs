using System.Text.RegularExpressions;

namespace UrlCleaner;

public static partial class PlaceholderConverter
{
    // A placeholder: a kebab-case label (lowercase alphanumeric groups joined by
    // single hyphens) wrapped in double braces, e.g. "{{tvdb-api-key}}".
    [GeneratedRegex(@"\{\{[a-z0-9]+(?:-[a-z0-9]+)*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>
    /// Whether <paramref name="text"/> contains at least one <c>{{kebab-case}}</c> placeholder.
    /// </summary>
    public static bool ContainsPlaceholder(string? text) =>
        !string.IsNullOrEmpty(text) && Placeholder().IsMatch(text);

    /// <summary>
    /// If <paramref name="text"/> contains one or more <c>{{kebab-case}}</c> placeholders,
    /// fills them from recent clipboard <paramref name="history"/> (most-recent first) and
    /// returns the result. Distinct placeholders draw from successively deeper history
    /// entries, aligned so the value copied <em>first</em> fills the placeholder that
    /// appears <em>first</em> (reading order). A single placeholder therefore takes the
    /// most recent entry. Returns <c>null</c> when there is no placeholder or no history
    /// value to insert.
    /// </summary>
    public static string? TryConvert(string text, IReadOnlyList<string> history)
    {
        if (string.IsNullOrEmpty(text) || history.Count == 0)
            return null;

        var matches = Placeholder().Matches(text);
        if (matches.Count == 0)
            return null;

        // Distinct placeholders in order of first appearance.
        var distinct = new List<string>();
        foreach (Match m in matches)
            if (!distinct.Contains(m.Value))
                distinct.Add(m.Value);

        // Assign each placeholder a history entry, anchored at the most-recent end:
        // the last-appearing placeholder gets history[0], and earlier placeholders get
        // progressively older entries. If history is shorter than the placeholder count,
        // the earliest placeholders are left unfilled (their literal text is kept).
        var count = distinct.Count;
        var map = new Dictionary<string, string>();
        for (var j = 0; j < count; j++)
        {
            var depth = count - 1 - j;
            if (depth < history.Count)
                map[distinct[j]] = history[depth];
        }

        if (map.Count == 0)
            return null;

        // Use a match evaluator so each value is inserted literally — a raw Replace
        // would treat "$" sequences in the value as substitution patterns.
        var result = Placeholder().Replace(text, m => map.TryGetValue(m.Value, out var value) ? value : m.Value);
        return result == text ? null : result;
    }
}
