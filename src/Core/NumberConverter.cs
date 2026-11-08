using System.Text.RegularExpressions;

namespace UrlCleaner;

public static partial class NumberConverter
{
    // Matches a number that uses commas as thousands separators, e.g.
    // "10,871.69", "1,000", "-1,234,567.89". Requires at least one comma group
    // (each exactly 3 digits) so there is something to strip. The first group
    // cannot start with 0, so a European decimal such as "0,500" is left alone.
    [GeneratedRegex(@"^-?[1-9]\d{0,2}(,\d{3})+(\.\d+)?$")]
    private static partial Regex GroupedNumber();

    /// <summary>
    /// If the whole text is a number written with comma thousands separators,
    /// returns the same number with the commas removed (e.g. "10,871.69" → "10871.69").
    /// Returns <c>null</c> if the text is not such a number.
    /// </summary>
    public static string? TryConvert(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var trimmed = text.Trim();

        if (!GroupedNumber().IsMatch(trimmed))
            return null;

        return trimmed.Replace(",", "");
    }
}
