using AIBridge.Shared.Models;

namespace AIBridge.Services.WebChat;

/// <summary>
/// Vista risolta dei selettori di un engine: superset di <see cref="DomSelectors"/> (contratto col server,
/// invariato) più i selettori oggi hardcoded nel service (Docs: Phase1 §2.2).
///
/// NOTA: questa classe NON contiene default specifici di una webchat. I valori di Gemini vivono in
/// Engines/Gemini (Unit 3), così nessun literal di sito resta fuori dall'engine.
/// </summary>
public sealed class EngineSelectors
{
    // ── Campi condivisi con DomSelectors (stessi nomi/tipi: garantisce compatibilità col server) ──
    public string TextArea { get; set; } = string.Empty;
    public string StopButton { get; set; } = string.Empty;
    public string MessageElements { get; set; } = string.Empty;
    public string UploadButton { get; set; } = string.Empty;
    public string MenuItems { get; set; } = string.Empty;
    public string SendButton { get; set; } = string.Empty;

    // ── Campi aggiuntivi oggi hardcoded in GeminiBrowserService ──
    /// <summary>Lista pulsanti "allega" (oggi inline in SendPromptAsync, diversa da UploadButton).</summary>
    public string AttachButton { get; set; } = string.Empty;
    /// <summary>Regex (case-insensitive) per scegliere la voce di menu "carica da computer".</summary>
    public string UploadMenuTextRegex { get; set; } = string.Empty;
    /// <summary>Selettori che indicano utente non loggato (SPA raggiungibile ma signed-out).</summary>
    public string SignedOutIndicators { get; set; } = string.Empty;
    public string SignInLink { get; set; } = string.Empty;
    public string SignInButton { get; set; } = string.Empty;
    public string CaptchaFrame { get; set; } = string.Empty;
    /// <summary>Selettore dei blocchi di codice dentro un messaggio (per rilevare i blocchi tool).</summary>
    public string CodeBlock { get; set; } = string.Empty;
    /// <summary>CSS iniettato nella pagina per nascondere popup/upsell.</summary>
    public string HidePopupsCss { get; set; } = string.Empty;
    /// <summary>Template di selettori per trovare il link di una chat in sidebar; "{id}" = id chat.</summary>
    public string[] ChatLinkSelectorTemplates { get; set; } = Array.Empty<string>();

    /// <summary>Copia i campi condivisi da un <see cref="DomSelectors"/> (es. ricevuto dal server).</summary>
    public static EngineSelectors FromDomSelectors(DomSelectors dom)
    {
        ArgumentNullException.ThrowIfNull(dom);
        return new EngineSelectors
        {
            TextArea = dom.TextArea,
            StopButton = dom.StopButton,
            MessageElements = dom.MessageElements,
            UploadButton = dom.UploadButton,
            MenuItems = dom.MenuItems,
            SendButton = dom.SendButton
        };
    }
}
