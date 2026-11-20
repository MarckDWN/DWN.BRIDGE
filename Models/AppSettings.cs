namespace AIBridge.Models;

/// <summary>
/// Persistent application settings saved to %LocalAppData%\AIBridge\settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>BCP-47 culture code of the last chosen UI language (e.g. "en-US", "it-IT"). Empty means use system default.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Whether to show the agent selection dialog on startup.</summary>
    public bool ShowAgentSelectionAtStartup { get; set; } = true;

    /// <summary>
    /// If true, unhandled exceptions are automatically reported to the AIBridge server
    /// to help the team diagnose crashes. No personal data or conversation content is sent.
    /// Default: true (opt-in — user can disable in Settings).
    /// </summary>
    public bool SendCrashReports { get; set; } = true;
}
