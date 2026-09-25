namespace UrlCleaner;

public static class PathConverter
{
    // Characters forbidden in Windows filename segments (excluding backslash, which is the separator).
    // Colon is only allowed as the 2nd char of a drive-letter prefix, handled separately.
    private static readonly char[] InvalidSegmentChars = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>
    /// If <paramref name="text"/> looks like a single Windows-style path,
    /// returns the same path with backslashes replaced by forward slashes.
    /// Returns <c>null</c> if the text is not a recognised Windows path.
    /// </summary>
    public static string? TryConvert(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var trimmed = text.Trim();

        // Must be a single line.
        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
            return null;

        // Must contain at least one backslash.
        if (!trimmed.Contains('\\'))
            return null;

        // Skip UNC paths (\\server\share).
        if (trimmed.StartsWith(@"\\"))
            return null;

        // Not a URL.
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        // Check for drive-letter path or valid relative path.
        if (IsDriveLetterPath(trimmed) || IsValidRelativePath(trimmed))
            return trimmed.Replace('\\', '/');

        return null;
    }

    private static bool IsDriveLetterPath(string path)
    {
        // Must start with letter, colon, backslash (e.g. C:\)
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            return false;

        // Validate segments after the drive prefix (skip "C:" portion).
        var afterDrive = path[3..];
        if (afterDrive.Length == 0)
            return true; // Just "C:\"

        var segments = afterDrive.Split('\\');
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            // Allow trailing backslash (last segment empty).
            if (segment.Length == 0 && i == segments.Length - 1)
                continue;
            if (segment.Length == 0)
                return false;
            if (segment.AsSpan().IndexOfAny(InvalidSegmentChars) >= 0)
                return false;
        }
        return true;
    }

    private static bool IsValidRelativePath(string path)
    {
        var segments = path.Split('\\');
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            // Allow trailing backslash (last segment empty), consistent with drive-letter paths.
            if (segment.Length == 0 && i == segments.Length - 1)
                continue;
            if (segment.Length == 0)
                return false;

            if (segment.AsSpan().IndexOfAny(InvalidSegmentChars) >= 0)
                return false;
        }
        return true;
    }
}
