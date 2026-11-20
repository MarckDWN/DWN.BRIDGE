using System.Diagnostics;
using System.Text.Json;
using AIBridge.Shared.Models;

namespace AIBridge.Services.WebChat;

/// <summary>
/// Risolve la vista effettiva di <see cref="EngineSelectors"/> secondo il layering (Unit 8):
/// <c>default nel codice</c> &lt; <c>OTA server</c> (se abilitato) &lt; <c>override locale</c>
/// (<c>%LocalAppData%\AIBridge\selectors\{engineId}.json</c>).
/// Solo le chiavi presenti nell'override locale sovrascrivono i layer sottostanti;
/// file JSON invalidi o illeggibili vengono ignorati con log diagnostico.
/// </summary>
public static class SelectorLayerResolver
{
    private sealed record CachedOverride(DateTime LastWriteUtc, Dictionary<string, JsonElement> Entries);
    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, CachedOverride> _overrideCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetDefaultOverrideDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBridge",
            "selectors");

    public static string GetOverrideFilePath(string engineId, string? overrideDirectory = null)
    {
        var dir = string.IsNullOrWhiteSpace(overrideDirectory)
            ? GetDefaultOverrideDirectory()
            : overrideDirectory;
        return Path.Combine(dir, $"{engineId}.json");
    }

    public static EngineSelectors Resolve(
        string engineId,
        EngineSelectors codeDefaults,
        DomSelectors? otaSelectors = null,
        string? overrideDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(codeDefaults);

        // Layer 1: default nel codice
        var result = Clone(codeDefaults);

        // Layer 2: OTA server (se presente, sovrascrive solo campi non vuoti)
        if (otaSelectors != null)
        {
            ApplyNonEmpty(otaSelectors.TextArea,        v => result.TextArea = v);
            ApplyNonEmpty(otaSelectors.StopButton,      v => result.StopButton = v);
            ApplyNonEmpty(otaSelectors.MessageElements, v => result.MessageElements = v);
            ApplyNonEmpty(otaSelectors.UploadButton,    v => result.UploadButton = v);
            ApplyNonEmpty(otaSelectors.MenuItems,       v => result.MenuItems = v);
            ApplyNonEmpty(otaSelectors.SendButton,      v => result.SendButton = v);
        }

        // Layer 3: override locale JSON (%LocalAppData%\AIBridge\selectors\{engineId}.json)
        if (!string.IsNullOrWhiteSpace(engineId))
        {
            var overridePath = GetOverrideFilePath(engineId, overrideDirectory);
            var overrides = TryLoadOverrides(overridePath);
            if (overrides != null)
            {
                ApplyOverrides(result, overrides);
            }
        }

        return result;
    }

    private static EngineSelectors Clone(EngineSelectors src) => new()
    {
        TextArea                  = src.TextArea,
        StopButton                = src.StopButton,
        MessageElements           = src.MessageElements,
        UploadButton              = src.UploadButton,
        MenuItems                 = src.MenuItems,
        SendButton                = src.SendButton,
        AttachButton              = src.AttachButton,
        UploadMenuTextRegex       = src.UploadMenuTextRegex,
        SignedOutIndicators       = src.SignedOutIndicators,
        SignInLink                = src.SignInLink,
        SignInButton              = src.SignInButton,
        CaptchaFrame              = src.CaptchaFrame,
        CodeBlock                 = src.CodeBlock,
        HidePopupsCss             = src.HidePopupsCss,
        ChatLinkSelectorTemplates = src.ChatLinkSelectorTemplates.ToArray()
    };

    private static void ApplyNonEmpty(string? value, Action<string> setter)
    {
        if (!string.IsNullOrWhiteSpace(value))
            setter(value);
    }

    private static Dictionary<string, JsonElement>? TryLoadOverrides(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);
            lock (_cacheLock)
            {
                if (_overrideCache.TryGetValue(filePath, out var cached) && cached.LastWriteUtc == lastWriteUtc)
                    return cached.Entries;
            }

            var json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (parsed == null)
                return null;

            var caseInsensitive = new Dictionary<string, JsonElement>(parsed, StringComparer.OrdinalIgnoreCase);
            lock (_cacheLock)
            {
                _overrideCache[filePath] = new CachedOverride(lastWriteUtc, caseInsensitive);
            }
            return caseInsensitive;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Selectors] Override locale ignorato ({filePath}): {ex.Message}");
            Console.WriteLine($"[Selectors] Override locale ignorato ({filePath}): {ex.Message}");
            return null;
        }
    }

    private static void ApplyOverrides(EngineSelectors target, Dictionary<string, JsonElement> overrides)
    {
        TryApplyString(overrides, nameof(EngineSelectors.TextArea),            v => target.TextArea = v);
        TryApplyString(overrides, nameof(EngineSelectors.StopButton),          v => target.StopButton = v);
        TryApplyString(overrides, nameof(EngineSelectors.MessageElements),     v => target.MessageElements = v);
        TryApplyString(overrides, nameof(EngineSelectors.UploadButton),        v => target.UploadButton = v);
        TryApplyString(overrides, nameof(EngineSelectors.MenuItems),           v => target.MenuItems = v);
        TryApplyString(overrides, nameof(EngineSelectors.SendButton),          v => target.SendButton = v);
        TryApplyString(overrides, nameof(EngineSelectors.AttachButton),        v => target.AttachButton = v);
        TryApplyString(overrides, nameof(EngineSelectors.UploadMenuTextRegex), v => target.UploadMenuTextRegex = v);
        TryApplyString(overrides, nameof(EngineSelectors.SignedOutIndicators), v => target.SignedOutIndicators = v);
        TryApplyString(overrides, nameof(EngineSelectors.SignInLink),          v => target.SignInLink = v);
        TryApplyString(overrides, nameof(EngineSelectors.SignInButton),        v => target.SignInButton = v);
        TryApplyString(overrides, nameof(EngineSelectors.CaptchaFrame),        v => target.CaptchaFrame = v);
        TryApplyString(overrides, nameof(EngineSelectors.CodeBlock),           v => target.CodeBlock = v);
        TryApplyString(overrides, nameof(EngineSelectors.HidePopupsCss),       v => target.HidePopupsCss = v);

        if (overrides.TryGetValue(nameof(EngineSelectors.ChatLinkSelectorTemplates), out var arrEl)
            && arrEl.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();
            foreach (var item in arrEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    items.Add(s);
            }
            target.ChatLinkSelectorTemplates = items.ToArray();
        }
    }

    private static void TryApplyString(Dictionary<string, JsonElement> overrides, string key, Action<string> setter)
    {
        if (overrides.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (s != null)
                setter(s);
        }
    }
}

