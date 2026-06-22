namespace AIBridge.Models;

/// <summary>
/// Persistent application settings saved to %LocalAppData%\AIBridge\settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>BCP-47 culture code of the last chosen UI language (e.g. "en-US", "it-IT").</summary>
    public string Language { get; set; } = "en-US";
}
