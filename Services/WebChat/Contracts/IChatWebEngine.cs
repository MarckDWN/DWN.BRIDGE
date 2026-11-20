using Microsoft.Playwright;

namespace AIBridge.Services.WebChat;

/// <summary>
/// Tutta la "conoscenza del sito" di una webchat: ciò che cambia quando cambia la GUI del provider.
/// BrowserHost, ChatSyncEngine, HumanInputDriver e WebChatService non devono contenere literal di sito.
///
/// Scelta consapevole: il contratto espone tipi Playwright (IPage/ILocator/IResponse). Tutto è in-process,
/// quindi un'astrazione "finta" del DOM aggiungerebbe solo costo senza benefici.
/// </summary>
public interface IChatWebEngine
{
    EngineDescriptor Descriptor { get; }
    EngineTimings Timings { get; }

    /// <summary>Vista risolta dei selettori (default engine &lt; OTA &lt; override locale, vedi Unit 8).</summary>
    EngineSelectors Selectors { get; }

    // ── URL / identità chat ──
    string BuildNewChatUrl(string language);
    string BuildChatUrl(string chatId, string language, string? authUser);
    bool IsOnEngineHost(string url);
    string? TryExtractChatId(string url);

    // ── Modello dei messaggi ──
    /// <summary>Selettore CSS che individua gli elementi messaggio (utente e assistente).</summary>
    string MessageElementsSelector { get; }

    /// <summary>Determina se l'elemento è un messaggio utente o dell'assistente (tag, attributo, figlio...).</summary>
    Task<MessageRole> ClassifyAsync(ILocator element);

    /// <summary>Legge il contenuto del messaggio normalizzato (Markdown, blocchi tool inclusi).</summary>
    Task<string> ReadMessageAsync(ILocator element, MessageRole role, IPage page);

    // ── Stato pagina ──
    /// <summary>Selettore del pulsante Stop già composto (es. con :near(textarea)).</summary>
    string BuildStopButtonSelector();

    /// <summary>null = pagina utilizzabile; altrimenti il motivo del blocco.</summary>
    Task<BlockReason?> DetectBlockAsync(IPage page);

    /// <summary>True se la risposta di rete indica un crash della SPA da recuperare con un reload.</summary>
    bool IsSpaCrash(IResponse response);

    // ── Azioni sito-specifiche ──
    /// <summary>Hook su DOMContentLoaded (es. iniezione CSS anti-popup).</summary>
    Task PreparePageAsync(IPage page);

    Task AttachFilesAsync(IPage page, string[] filePaths, IHumanInput input);

    Task<NavigationResult> NavigateToChatAsync(IPage page, string chatId, NavigationContext context);

    Task NavigateToNewChatAsync(IPage page, NavigationContext context);

    /// <summary>Titolo della chat letto dalla sidebar; null se non trovato.</summary>
    Task<string?> GetChatTitleAsync(IPage page, string chatId);

    /// <summary>Flusso di login manuale dell'engine; null se non previsto.</summary>
    ILoginFlow? LoginFlow { get; }

    /// <summary>Verifica sul DOM corrente la presenza dei selettori configurati (Unit 8).</summary>
    Task<EngineSelfTestReport> SelfTestAsync(IPage page);
}
