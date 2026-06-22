namespace AIBridge.Shared.Models;

/// <summary>
/// Payload inviato dal client WPF al Cloud Brain server.
/// NON deve mai contenere stringhe di connessione, password o chiavi API.
/// SchemaContext contiene solo metadati strutturali (nomi tabelle/colonne/tipi).
/// </summary>
public record ContextRequest
{
    /// <summary>ID sessione corrente (GUID assegnato dal server alla creazione della sessione).</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Tipo agente: "SqlAnalyst" | "Coder" | "JsonExtractor" | "Default"</summary>
    public string AgentType { get; init; } = string.Empty;

    /// <summary>Testo estratto dal DOM della pagina Gemini corrente (contesto visivo).</summary>
    public string DomContext { get; init; } = string.Empty;

    /// <summary>Messaggio grezzo dell'utente (non pre-elaborato).</summary>
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>
    /// Schema strutturale del database (solo nomi tabelle/colonne/tipi).
    /// MAI stringhe di connessione o credenziali.
    /// </summary>
    public string SchemaContext { get; init; } = string.Empty;

    /// <summary>Lingua dell'interfaccia per la selezione del template prompt. Default: "it-IT".</summary>
    public string Language { get; init; } = "it-IT";
}
