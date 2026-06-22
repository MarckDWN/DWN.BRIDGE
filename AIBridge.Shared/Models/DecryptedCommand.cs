namespace AIBridge.Shared.Models;

/// <summary>
/// Comando atomico decodificato dal client dopo la decifratura AES-GCM.
/// QUESTO OGGETTO NON DEVE MAI ESSERE LOGGATO IN CHIARO.
/// </summary>
public record DecryptedCommand
{
    /// <summary>
    /// Tipo comando: 
    ///   "SEND_PROMPT"  – invia il prompt completo via Playwright (Phase 1 e 2)
    ///   "CLICK"        – click su un selettore CSS
    ///   "NAVIGATE"     – naviga a un URL
    /// </summary>
    public string CommandType { get; init; } = string.Empty;

    /// <summary>CSS selector target (per CLICK). Aggiornabile OTA tramite /api/selectors.</summary>
    public string? Selector { get; init; }

    /// <summary>Valore testuale o URL (per CLICK e NAVIGATE).</summary>
    public string? Value { get; init; }

    /// <summary>
    /// Prompt completo costruito dal server iniettando i template proprietari.
    /// Valorizzato solo per CommandType == "SEND_PROMPT".
    /// </summary>
    public string? BuiltPrompt { get; init; }
}
