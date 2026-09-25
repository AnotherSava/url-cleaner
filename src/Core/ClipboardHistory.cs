namespace UrlCleaner;

/// <summary>
/// Recent distinct clipboard values, most recent first, used to fill placeholders.
/// </summary>
public sealed class ClipboardHistory
{
    private const int Limit = 10;

    private readonly List<string> _values = [];

    /// <summary>
    /// A copy of the current values, so a run's view can't change while it is in progress.
    /// </summary>
    public IReadOnlyList<string> Snapshot() => _values.ToArray();

    /// <summary>
    /// Pushes <paramref name="value"/> to the front, de-duplicating so a repeated copy doesn't consume several slots.
    /// A placeholder template and a value produced by filling one are never stored: either would let a template fill
    /// itself, yielding output that still contains the placeholder.
    /// </summary>
    public void Remember(string value, bool producedByFilling)
    {
        if (producedByFilling || string.IsNullOrEmpty(value) || PlaceholderConverter.ContainsPlaceholder(value))
            return;

        _values.Remove(value);
        _values.Insert(0, value);
        if (_values.Count > Limit)
            _values.RemoveRange(Limit, _values.Count - Limit);
    }
}
