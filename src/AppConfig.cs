using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace UrlCleaner;

public class AppConfig
{
    public bool TrimUrl { get; init; } = true;
    public List<TrackingParamGroup> TrackingParams { get; init; } = [];
    public List<SiteRule> SiteRules { get; init; } = [];

    /// <summary>
    /// Flattens all groups into a single set of param names for fast lookup.
    /// </summary>
    public HashSet<string> GetAllTrackingParams() =>
        new(TrackingParams.SelectMany(g => g.Params), StringComparer.OrdinalIgnoreCase);

    private const string AppName = "UrlCleaner";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Checks whether the app is registered to start with Windows.
    /// </summary>
    public static bool GetAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    /// <summary>
    /// Adds or removes the app from the Windows startup registry.
    /// </summary>
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    /// <summary>
    /// The resolved path of the config file used by the last <see cref="Load"/> call.
    /// </summary>
    public static string ConfigFilePath { get; private set; } = "";

    private static string DefaultConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>
    /// Loads config from the given path, or from config.json next to the exe.
    /// If the file doesn't exist, writes a default config and returns it.
    /// </summary>
    public static AppConfig Load(string? configPath = null)
    {
        var path = configPath ?? DefaultConfigPath;
        ConfigFilePath = Path.GetFullPath(path);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefault();
        }

        var config = CreateDefault();
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        return config;
    }

    private static AppConfig CreateDefault()
    {
        // default.json is compiled into the DLL as an embedded resource,
        // so it's always available even if the user deletes files next to the exe.
        using var stream = typeof(AppConfig).Assembly
            .GetManifestResourceStream("UrlCleaner.default.json")!;
        return JsonSerializer.Deserialize<AppConfig>(stream, JsonOptions)!;
    }
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
