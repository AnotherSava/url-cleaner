using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UrlCleaner;

public class AppConfig
{
    // A bool that defaults to true is always written: the writer's WhenWritingDefault would drop a false, and the
    // value would read back as true.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool TrimUrl { get; init; } = true;
    public bool ConvertPaths { get; init; }
    public bool ConvertNumbers { get; init; }
    public bool ConvertPlaceholders { get; init; }
    public List<TrackingParamGroup> TrackingParams { get; init; } = [];
    public List<SiteRule> SiteRules { get; init; } = [];

    /// <summary>
    /// Each plugin's settings, by id. A plugin missing from it is enabled.
    /// </summary>
    public Dictionary<string, PluginSettings>? Plugins { get; init; }

    /// <summary>
    /// The order of the pipeline's entries. Absent means the fallback order.
    /// </summary>
    public PipelineConfig? Pipeline { get; init; }

    public bool IsPluginEnabled(string id) => Plugins?.GetValueOrDefault(id)?.Enabled ?? true;

    /// <summary>
    /// Flattens all groups into a single set of param names for fast lookup.
    /// </summary>
    public HashSet<string> GetAllTrackingParams() =>
        new(TrackingParams.SelectMany(g => g.Params), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets one value in the config file (read-modify-write) and leaves everything else as it was. <paramref name="keys"/>
    /// is the path to it, such as <c>["convertPaths"]</c> or <c>["plugins", "acme-notes", "enabled"]</c>; missing objects
    /// along the path are created.
    /// </summary>
    public static bool UpdateConfigValue(string path, IReadOnlyList<string> keys, object value)
    {
        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                root = JsonNode.Parse(json) as JsonObject
                    ?? JsonNode.Parse(JsonSerializer.Serialize(Default(), JsonOptions)) as JsonObject
                    ?? new JsonObject();
            }
            else
            {
                // Start from the full default config so we never write a file
                // that is missing trackingParams / siteRules / etc.
                var defaultJson = JsonSerializer.Serialize(Default(), JsonOptions);
                root = JsonNode.Parse(defaultJson) as JsonObject ?? new JsonObject();
            }

            var parent = root;
            foreach (var key in keys.Take(keys.Count - 1))
            {
                if (parent[key] is not JsonObject child)
                {
                    if (parent[key] != null)
                        throw new JsonException($"\"{key}\" isn't an object");

                    child = new JsonObject();
                    parent[key] = child;
                }

                parent = child;
            }

            parent[keys[^1]] = JsonValue.Create(value);
            File.WriteAllText(path, root.ToJsonString(WriteJsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            // File may be locked or corrupted (a property named twice makes JsonObject throw ArgumentException) — log and
            // continue rather than crash the UI thread.
            Logger.Error($"Can't update {string.Join('.', keys)} in {path}", ex);
            return false;
        }
    }

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    /// <summary>
    /// The full path of the given config file, or of config.json next to the exe.
    /// </summary>
    public static string ResolvePath(string? configPath) =>
        Path.GetFullPath(configPath ?? Path.Combine(AppContext.BaseDirectory, "config.json"));

    /// <summary>
    /// Loads config from the given path, or from config.json next to the exe.
    /// If the file doesn't exist, writes a default config and returns it.
    /// </summary>
    public static AppConfig Load(string? configPath = null)
    {
        var path = ResolvePath(configPath);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? Default();
            loaded.Validate();
            return loaded;
        }

        var config = Default();
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        return config;
    }

    /// <summary>
    /// Refuses a config the features would trip over: JSON accepts a null list or a null in one, and the code reading
    /// the config then fails on every copy, or at startup for the pipeline order. Throws <see cref="JsonException"/>, which
    /// the loader reports like any other broken file.
    /// </summary>
    private void Validate()
    {
        RequireEntries(TrackingParams, "trackingParams");
        foreach (var group in TrackingParams)
            RequireEntries(group.Params, "trackingParams[].params");

        RequireEntries(SiteRules, "siteRules");
        foreach (var rule in SiteRules)
        {
            RequireEntries(rule.Suffix, "siteRules[].suffix");
            RequireEntries(rule.AdditionalParams, "siteRules[].additionalParams");
            RequireEntries(rule.ExcludedParams, "siteRules[].excludedParams");
            RequireEntries(rule.KeepPathFrom, "siteRules[].keepPathFrom");
            RequireEntries(rule.StripPathSegments, "siteRules[].stripPathSegments");
            if (rule.StripPathIndex == null)
                throw new JsonException("siteRules[].stripPathIndex can't be null");
        }

        if (Pipeline?.Order is { } order && order.Any(entry => entry == null || string.IsNullOrEmpty(entry.Id)))
            throw new JsonException("pipeline.order has an entry without an id");
    }

    private static void RequireEntries<T>(List<T>? list, string name) where T : class
    {
        if (list == null || list.Any(item => item == null))
            throw new JsonException($"{name} can't be null or hold a null");
    }

    /// <summary>
    /// The embedded default config.
    /// </summary>
    public static AppConfig Default()
    {
        // default.json is compiled into the DLL as an embedded resource,
        // so it's always available even if the user deletes files next to the exe.
        using var stream = typeof(AppConfig).Assembly
            .GetManifestResourceStream("UrlCleaner.default.json")!;
        return JsonSerializer.Deserialize<AppConfig>(stream, JsonOptions)!;
    }
}

public class PluginSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Enabled { get; init; } = true;
}

public class PipelineConfig
{
    public List<PipelineEntry>? Order { get; init; }
}

public class PipelineEntry
{
    public string Id { get; init; } = "";

    /// <summary>
    /// Whether the run ends once this entry has matched. Omitted means set, as in the fallback order, so leaving it
    /// out of a reordered entry can't silently turn the reorder into fall-through.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Stop { get; init; } = true;
}

public class TrackingParamGroup
{
    public string Comment { get; init; } = "";
    public List<string> Params { get; init; } = [];
}

public class SiteRule
{
    /// <summary>
    /// Domain suffix(es) to match. Accepts a single string or an array in JSON.
    /// </summary>
    [JsonConverter(typeof(StringOrListConverter))]
    public List<string> Suffix { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Enabled { get; init; } = true;
    public List<string> AdditionalParams { get; init; } = [];
    public List<string> ExcludedParams { get; init; } = [];

    /// <summary>
    /// When true, strip ALL query params except those in <see cref="ExcludedParams"/>.
    /// </summary>
    public bool StripAllParams { get; init; }

    /// <summary>
    /// Keep the path starting from the first occurrence of any listed segment,
    /// discarding the SEO slug before it. Accepts a string or array in JSON.
    /// </summary>
    [JsonConverter(typeof(StringOrListConverter))]
    public List<string> KeepPathFrom { get; init; } = [];

    /// <summary>
    /// Path segment prefixes to remove. A segment like "ref=sr_1_8" is removed
    /// if any prefix (e.g. "ref=") matches. Accepts a string or array in JSON.
    /// </summary>
    [JsonConverter(typeof(StringOrListConverter))]
    public List<string> StripPathSegments { get; init; } = [];

    /// <summary>
    /// When true, strip SEO slug text from path segments that start with digits
    /// followed by a hyphen (e.g. "2409726-some-slug" → "2409726").
    /// </summary>
    public bool StripSlugs { get; init; }

    /// <summary>
    /// Zero-based path segment indices to remove. Accepts a single int or array in JSON.
    /// </summary>
    [JsonConverter(typeof(IntOrListConverter))]
    public List<int> StripPathIndex { get; init; } = [];

    /// <summary>
    /// When true, strip the URL fragment (#...).
    /// </summary>
    public bool StripFragment { get; init; }
}

/// <summary>
/// Reads a JSON value that is either a single int or an array of ints
/// into a List&lt;int&gt;. Always writes back as an array.
/// </summary>
public class IntOrListConverter : JsonConverter<List<int>>
{
    public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return [reader.GetInt32()];

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<int>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                list.Add(reader.GetInt32());
            return list;
        }

        throw new JsonException("Expected int or array for StripPathIndex");
    }

    public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteNumberValue(item);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Reads a JSON value that is either a single string or an array of strings
/// into a List&lt;string&gt;. Always writes back as an array.
/// </summary>
public class StringOrListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return [reader.GetString()!];

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                list.Add(reader.GetString()!);
            return list;
        }

        throw new JsonException("Expected string or array for Suffix");
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
