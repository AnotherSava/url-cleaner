using System.Text.Json;
using System.Text.RegularExpressions;

namespace UrlCleaner;

/// <summary>
/// A manifest that breaks a rule. The message names the field.
/// </summary>
public sealed class InvalidManifestException(string message) : Exception(message);

/// <summary>
/// A process plugin, read from <c>plugins/&lt;id&gt;/plugin.json</c>. The host checks <see cref="Matches"/> itself, so
/// nothing is started for a copy the plugin doesn't want.
/// </summary>
public sealed partial class PluginManifest
{
    /// <summary>
    /// The newest protocol version this host speaks. A manifest may name any version from 1 up to it.
    /// </summary>
    public const int HostProtocol = 1;

    public const int DefaultTimeoutSeconds = 10;
    private const int MaxTimeoutSeconds = 300;

    // A pattern gets this long per match, so a pathological one can't freeze the UI thread.
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<Regex> Patterns { get; }
    public IReadOnlyList<string> Run { get; }
    public int TimeoutSeconds { get; }

    /// <summary>
    /// The plugin's folder, which is also its working directory.
    /// </summary>
    public string Folder { get; }

    private PluginManifest(string id, string name, IReadOnlyList<Regex> patterns, IReadOnlyList<string> run, int timeoutSeconds, string folder)
    {
        Id = id;
        Name = name;
        Patterns = patterns;
        Run = run;
        TimeoutSeconds = timeoutSeconds;
        Folder = folder;
    }

    /// <summary>
    /// Reads and validates a manifest found in <paramref name="folder"/>. Throws <see cref="InvalidManifestException"/>
    /// for a manifest that breaks a rule, and <see cref="JsonException"/> for one that isn't valid JSON.
    /// </summary>
    public static PluginManifest Parse(string json, string folder)
    {
        var raw = JsonSerializer.Deserialize<RawManifest>(json, JsonOptions) ?? throw new InvalidManifestException("the manifest is empty");

        var protocol = raw.Protocol ?? throw new InvalidManifestException("protocol is missing");
        if (protocol < 1)
            throw new InvalidManifestException($"protocol must be 1 or higher, not {protocol}");
        if (protocol > HostProtocol)
            throw new InvalidManifestException($"the plugin needs protocol {protocol}, and this app speaks up to {HostProtocol}");

        var id = raw.Id ?? throw new InvalidManifestException("id is missing");
        if (!KebabCase().IsMatch(id))
            throw new InvalidManifestException($"id \"{id}\" must be lowercase kebab-case, like \"acme-notes\"");
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        if (id != folderName)
            throw new InvalidManifestException($"id \"{id}\" must equal the plugin's folder name, \"{folderName}\"");

        var name = raw.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new InvalidManifestException("name is missing");

        var patterns = raw.Match?.Patterns;
        if (patterns == null || patterns.Count == 0)
            throw new InvalidManifestException("match.patterns needs at least one pattern");

        var run = raw.Run;
        if (run == null || run.Count == 0 || string.IsNullOrWhiteSpace(run[0]))
            throw new InvalidManifestException("run needs the program to start as its first element");
        if (run.Any(argument => argument == null || argument.Contains('\0')))
            throw new InvalidManifestException("run can't hold a null element or a NUL character");
        if (IsBatchFile(run[0]))
            throw new InvalidManifestException($"run can't start a batch file (\"{run[0]}\"): Windows hands it to cmd.exe, whose quoting the arguments can't be made safe for");

        var timeout = raw.TimeoutSeconds ?? DefaultTimeoutSeconds;
        if (timeout is < 1 or > MaxTimeoutSeconds)
            throw new InvalidManifestException($"timeoutSeconds must be a whole number from 1 to {MaxTimeoutSeconds}, not {timeout}");

        return new PluginManifest(id, name, patterns.Select(Compile).ToList(), run, timeout, folder);
    }

    /// <summary>
    /// Whether Windows would hand <paramref name="program"/> to cmd.exe. Trailing spaces and dots count for nothing, as
    /// Windows drops them when it resolves the name.
    /// </summary>
    public static bool IsBatchFile(string program) =>
        OperatingSystem.IsWindows() && Path.GetExtension(program.TrimEnd(' ', '.')).ToLowerInvariant() is ".cmd" or ".bat";

    private static Regex Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, PatternTimeout);
        }
        catch (ArgumentException e)
        {
            throw new InvalidManifestException($"match.patterns has a pattern that doesn't compile, \"{pattern}\": {e.Message}");
        }
    }

    /// <summary>
    /// Whether any pattern matches <paramref name="text"/> with surrounding whitespace trimmed. A line copied from an
    /// editor or a terminal often ends in CRLF, and <c>$</c> matches before a final <c>\n</c> but not before <c>\r\n</c>.
    /// A pattern that runs out of time counts as no match.
    /// </summary>
    public bool Matches(string text)
    {
        var trimmed = text.Trim();
        foreach (var pattern in Patterns)
        {
            try
            {
                if (pattern.IsMatch(trimmed))
                    return true;
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn($"Plugin {Id}: pattern \"{pattern}\" ran out of time on a {trimmed.Length}-character text; counted as no match");
            }
        }

        return false;
    }

    /// <summary>
    /// The absolute path of the program <see cref="Run"/> starts, or <c>null</c> when it can't be found. A name with a
    /// path separator is relative to the plugin folder; a bare name is searched on <paramref name="pathVariable"/>, the
    /// value of PATH. On Windows a name with no extension gets <c>.exe</c>.
    /// </summary>
    public string? FindExecutable(string? pathVariable)
    {
        var command = Run[0];
        if (OperatingSystem.IsWindows() && !Path.HasExtension(command))
            command += ".exe";

        if (command.Contains('/') || command.Contains('\\'))
        {
            var candidate = Path.GetFullPath(Path.Combine(Folder, command));
            return File.Exists(candidate) ? candidate : null;
        }

        // An entry may be quoted, which Windows' own lookup accepts.
        foreach (var entry in (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private sealed class RawManifest
    {
        public int? Protocol { get; init; }
        public string? Id { get; init; }
        public string? Name { get; init; }
        public RawMatch? Match { get; init; }
        public List<string>? Run { get; init; }
        public int? TimeoutSeconds { get; init; }
    }

    private sealed class RawMatch
    {
        public List<string>? Patterns { get; init; }
    }
}
