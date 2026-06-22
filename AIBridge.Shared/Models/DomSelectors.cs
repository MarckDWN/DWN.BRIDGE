namespace AIBridge.Shared.Models;

/// <summary>
/// CSS selectors usati da GeminiBrowserService per interagire col DOM di Gemini.
/// I valori default corrispondono ai selettori attualmente hardcoded nel client.
/// Vengono aggiornati OTA tramite GET /api/selectors al login e ad ogni heartbeat.
/// </summary>
public class DomSelectors
{
    public string TextArea { get; set; } =
        "div[role='textbox'][contenteditable='true']";

    public string StopButton { get; set; } =
        "button[aria-label*='Interrompi'], button[aria-label*='Stop generating'], " +
        "button[aria-label*='Stop'], button[aria-label*='Cancel generating'], " +
        "button[aria-label*='Annulla']";

    public string MessageElements { get; set; } =
        "query-content, message-content";

    public string UploadButton { get; set; } =
        "button[aria-label='Carica immagine'], button[aria-label='Upload image'], " +
        "button[aria-label='Carica file'], button[aria-label='Upload file']";

    public string MenuItems { get; set; } =
        "[role='menuitem']";
}
