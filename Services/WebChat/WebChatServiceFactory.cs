namespace AIBridge.Services.WebChat;

/// <summary>
/// Factory centrale per istanziare il servizio <see cref="ILlmService"/> configurato
/// con l'engine richiesto tramite <see cref="EngineRegistry"/>, garantendo la condivisione
/// di un'unica istanza di <see cref="ISelectorsService"/> tra factory, engine e service (Unit 7/8).
/// </summary>
public static class WebChatServiceFactory
{
    public static ILlmService Create(
        string engineId = "gemini",
        ISelectorsService? selectors = null,
        IUiBridge? ui = null,
        IBrowserInstaller? installer = null)
    {
        var sharedSelectors = selectors ?? new SelectorsService();
        var uiBridge        = ui        ?? new WpfUiBridge();
        var engine          = EngineRegistry.Default.CreateEngine(engineId, sharedSelectors, uiBridge);

        return new WebChatService(sharedSelectors, engine, uiBridge, installer);
    }
}

