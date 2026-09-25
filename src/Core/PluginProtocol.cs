using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UrlCleaner;

/// <summary>
/// An answer that breaks a protocol rule.
/// </summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>
/// What a plugin answered, once validated.
/// </summary>
public abstract record PluginAnswer;

/// <summary>
/// The text wasn't the plugin's after all.
/// </summary>
public sealed record NoneAnswer : PluginAnswer;

public sealed record RewriteAnswer(string Text) : PluginAnswer;

/// <summary>
/// The plugin acted; the host shows <see cref="Message"/>.
/// </summary>
public sealed record NotifyAnswer(string Message) : PluginAnswer;

/// <summary>
/// A proposal for the host to render as a confirm window. <see cref="State"/> is sent back unchanged with the action call.
/// </summary>
public sealed record ConfirmAnswer(string Title, string? Message, IReadOnlyList<ConfirmField> Fields, IReadOnlyList<ConfirmAction> Actions, JsonElement? State) : PluginAnswer;

public sealed record ErrorAnswer(string Message) : PluginAnswer;

/// <summary>
/// An editable text field. With <see cref="Options"/>, the host offers them as a drop-down on the editable box.
/// </summary>
public sealed record ConfirmField(string Id, string Label, string Value, IReadOnlyList<string>? Options);

public sealed record ConfirmAction(string Id, string Label);

/// <summary>
/// Protocol v1: the requests the host writes to a plugin's stdin, and the answers it accepts on stdout. Nothing in either
/// direction is specific to one platform.
/// </summary>
public static class PluginProtocol
{
    public const int Version = 1;

    // Non-ASCII text goes out as UTF-8 rather than \u escapes; the pipe carries UTF-8 both ways.
    private static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The call sent when a plugin's patterns match the copied text.
    /// </summary>
    public static string CopyRequest(string text) =>
        new JsonObject { ["protocol"] = Version, ["type"] = "copy", ["kind"] = "text", ["text"] = text }.ToJsonString(WriteOptions);

    /// <summary>
    /// The call sent when the user presses one of a confirm proposal's actions.
    /// </summary>
    public static string ActionRequest(string text, string action, IReadOnlyDictionary<string, string> fields, JsonElement? state)
    {
        var fieldValues = new JsonObject();
        foreach (var (id, value) in fields)
            fieldValues[id] = value;

        var request = new JsonObject
        {
            ["protocol"] = Version,
            ["type"] = "action",
            ["kind"] = "text",
            ["text"] = text,
            ["action"] = action,
            ["fields"] = fieldValues
        };
        if (state is { } stateValue)
            request["state"] = JsonNode.Parse(stateValue.GetRawText());

        return request.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Validates what a plugin wrote to stdout. It must be exactly one JSON object of a known type that keeps every rule;
    /// an action call may answer only <c>notify</c> or <c>error</c>. Throws <see cref="ProtocolException"/> otherwise.
    /// </summary>
    public static PluginAnswer ParseAnswer(string output, bool isActionCall)
    {
        if (string.IsNullOrWhiteSpace(output))
            throw new ProtocolException("it wrote nothing to stdout");

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(output);
            root = document.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new ProtocolException($"its output isn't one JSON value: {e.Message}");
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new ProtocolException("its answer isn't a JSON object");

        var type = OptionalString(root, "type") ?? throw new ProtocolException("its answer has no type");
        PluginAnswer answer = type switch
        {
            "none" => new NoneAnswer(),
            "rewrite" => new RewriteAnswer(RequiredString(root, "text")),
            "notify" => new NotifyAnswer(RequiredString(root, "message")),
            "confirm" => ParseConfirm(root),
            "error" => new ErrorAnswer(RequiredString(root, "message")),
            _ => throw new ProtocolException($"its answer has an unknown type, \"{type}\"")
        };

        if (isActionCall && answer is not (NotifyAnswer or ErrorAnswer))
            throw new ProtocolException($"it answered an action call with \"{type}\"; only notify or error is allowed");

        return answer;
    }

    private static ConfirmAnswer ParseConfirm(JsonElement root)
    {
        var title = RequiredString(root, "title");
        var message = OptionalString(root, "message");

        var fields = RequiredArray(root, "fields").Select(field => new ConfirmField(
            RequiredString(field, "id"),
            RequiredString(field, "label"),
            OptionalString(field, "value") ?? "",
            field.TryGetProperty("options", out var options) ? StringArray(options, "options") : null)).ToList();
        RequireUnique(fields.Select(f => f.Id), "field");

        var actions = RequiredArray(root, "actions").Select(action => new ConfirmAction(RequiredString(action, "id"), RequiredString(action, "label"))).ToList();
        if (actions.Count == 0)
            throw new ProtocolException("its confirm proposal has no actions");
        RequireUnique(actions.Select(a => a.Id), "action");

        JsonElement? state = root.TryGetProperty("state", out var value) ? value.Clone() : null;
        return new ConfirmAnswer(title, message, fields, actions, state);
    }

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) is { Length: > 0 } value ? value : throw new ProtocolException($"\"{name}\" must be a non-empty string");

    private static string? OptionalString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        return value.ValueKind == JsonValueKind.String ? Text(value, name) : throw new ProtocolException($"\"{name}\" must be a string");
    }

    /// <summary>
    /// A JSON string's text. A lone surrogate escape, which a plugin cutting a string between the halves of an emoji
    /// produces, isn't valid text.
    /// </summary>
    private static string Text(JsonElement value, string name)
    {
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw new ProtocolException($"\"{name}\" isn't valid text: it holds half of a surrogate pair");
        }
    }

    private static IEnumerable<JsonElement> RequiredArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : throw new ProtocolException($"\"{name}\" must be an array");

    private static IReadOnlyList<string> StringArray(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
            ? value.EnumerateArray().Select(item => Text(item, name)).ToList()
            : throw new ProtocolException($"\"{name}\" must be an array of strings");

    private static void RequireUnique(IEnumerable<string> ids, string kind)
    {
        var seen = new HashSet<string>();
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                throw new ProtocolException($"its confirm proposal has two {kind}s with the id \"{id}\"");
        }
    }
}
