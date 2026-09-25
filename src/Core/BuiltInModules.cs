namespace UrlCleaner;

/// <summary>
/// What a built-in module sees besides the text: the config, and the clipboard history as it stood when the run started.
/// </summary>
public sealed record ModuleContext(AppConfig Config, IReadOnlyList<string> History);

/// <summary>
/// An in-process clipboard feature. <see cref="TryRewrite"/> returns the rewritten text, or <c>null</c> when the text
/// isn't this module's (not matched).
/// </summary>
public sealed record BuiltInModule(
    string Id,
    Func<AppConfig, bool> IsEnabled,
    Func<string, ModuleContext, string?> TryRewrite);

public static class BuiltInModules
{
    /// <summary>
    /// Every built-in, in fallback order. Each converter's id is also the config key its tray toggle writes.
    /// </summary>
    public static readonly IReadOnlyList<BuiltInModule> All =
    [
        new("urlCleaner", _ => true, (text, ctx) => UrlSanitizer.TryClean(text, ctx.Config)),
        new("convertPaths", c => c.ConvertPaths, (text, _) => PathConverter.TryConvert(text)),
        new("convertNumbers", c => c.ConvertNumbers, (text, _) => NumberConverter.TryConvert(text)),
        new(PlaceholdersId, c => c.ConvertPlaceholders, (text, ctx) => PlaceholderConverter.TryConvert(text, ctx.History)),
    ];

    public const string PlaceholdersId = "convertPlaceholders";

    public static BuiltInModule? Find(string id) => All.FirstOrDefault(m => m.Id == id);
}
