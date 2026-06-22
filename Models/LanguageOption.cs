namespace AIBridge.Models;

/// <summary>
/// Represents a selectable UI language in the language picker.
/// </summary>
public class LanguageOption
{

    /// <summary>BCP-47 culture code, e.g. "en-US" or "it-IT".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Flag emoji + short label, e.g. "🇬🇧 English".</summary>
    public string Display { get; set; } = string.Empty;

    public override string ToString() => Display;
}
