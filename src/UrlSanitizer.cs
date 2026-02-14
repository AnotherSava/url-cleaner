namespace UrlCleaner;

public static class UrlSanitizer
{
    /// <summary>
    /// Attempts to clean tracking parameters from a URL string.
    /// Returns the cleaned URL, or null if the text isn't a URL or nothing was removed.
    /// </summary>
    public static string? TryClean(string text, AppConfig config)
    {
        if (config.TrimUrl)
            text = text.Trim();

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        // Find the first matching site rule by domain suffix
        SiteRule? rule = null;
        foreach (var r in config.SiteRules)
        {
            if (r.Suffix.Any(s => uri.Host.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                rule = r;
                break;
            }
        }

        // Site rule says "don't touch URLs from this domain"
        if (rule is { Enabled: false })
            return null;

        var pathChanged = false;
        var queryChanged = false;

        // --- Path cleaning ---
        var path = uri.AbsolutePath;
        if (rule is { StripPathSegments.Count: > 0 })
        {
            var segments = path.Split('/');
            var keptSegments = new List<string>(segments.Length);
            foreach (var seg in segments)
            {
                if (seg.Length > 0 && rule.StripPathSegments.Any(prefix =>
                        seg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    pathChanged = true;
                else
                    keptSegments.Add(seg);
            }

            if (pathChanged)
                path = string.Join("/", keptSegments);
        }

        // --- Query cleaning ---
        var query = uri.Query;
        var hasQuery = !string.IsNullOrEmpty(query) && query != "?";
        string? cleanedQuery = null;

        if (hasQuery)
        {
            var queryPart = query[1..]; // skip leading '?'
            var pairs = queryPart.Split('&');
            var kept = new List<string>();

            if (rule is { StripAllParams: true })
            {
                // Keep only params in ExcludedParams
                var excluded = new HashSet<string>(rule.ExcludedParams, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in pairs)
                {
                    var eqIndex = pair.IndexOf('=');
                    var rawKey = eqIndex >= 0 ? pair[..eqIndex] : pair;
                    var key = Uri.UnescapeDataString(rawKey);

                    if (excluded.Contains(key))
                        kept.Add(pair);
                    else
                        queryChanged = true;
                }
            }
            else
            {
                // Strip only known tracking params
                var paramsToStrip = config.GetAllTrackingParams();
                if (rule != null)
                {
                    foreach (var p in rule.AdditionalParams)
                        paramsToStrip.Add(p);
                    foreach (var p in rule.ExcludedParams)
                        paramsToStrip.Remove(p);
                }

                foreach (var pair in pairs)
                {
                    var eqIndex = pair.IndexOf('=');
                    var rawKey = eqIndex >= 0 ? pair[..eqIndex] : pair;
                    var key = Uri.UnescapeDataString(rawKey);

                    if (paramsToStrip.Contains(key))
                        queryChanged = true;
                    else
                        kept.Add(pair);
                }
            }

            cleanedQuery = kept.Count > 0 ? "?" + string.Join("&", kept) : "";
        }

        if (!pathChanged && !queryChanged)
            return null;

        // Rebuild URL: scheme + authority + cleaned path + cleaned query + fragment
        // Use original text up to the path to preserve exact formatting
        var afterScheme = uri.Scheme.Length + "://".Length;
        var authorityEnd = text.IndexOfAny(['/', '?', '#'], afterScheme);
        if (authorityEnd < 0) authorityEnd = text.Length;
        var baseUrl = text[..authorityEnd];
        var fragment = uri.Fragment; // "" or "#..."

        // If the original URL had no explicit path slash, don't inject one
        if (authorityEnd >= text.Length || text[authorityEnd] != '/')
            path = "";

        return baseUrl + path + (cleanedQuery ?? query) + fragment;
    }
}
