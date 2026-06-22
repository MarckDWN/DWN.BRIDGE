using AIBridge.Models;

namespace AIBridge.Services.Extensions;

/// <summary>
/// Interfaccia per adattatori di sessione.
/// Un ISessionAdapter fornisce accesso a sessioni e messaggi della cronologia.
/// Può essere implementato da: adattatori cloud, connettori aziendali,
/// integrazioni con sistemi di ticketing/CRM, o qualsiasi estensione esterna.
/// Le implementazioni vengono scoperte tramite ExtensionHost o risolte da CloudServiceLocator.
/// </summary>
public interface ISessionAdapter
{
    /// <summary>True se l'adapter è operativo e pronto a servire dati.</summary>
    bool IsAvailable { get; }

    /// <summary>Restituisce tutte le sessioni disponibili, ordinate per data decrescente.</summary>
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync();

    /// <summary>Crea e registra una nuova sessione. Restituisce il suo ID univoco.</summary>
    Task<Guid> BeginSessionAsync(string agentType, string geminiChatId = "",
                                  string description = "", string workspacePath = "");

    /// <summary>Aggiorna la descrizione (primo messaggio) della sessione.</summary>
    Task UpdateDescriptionAsync(Guid sessionId, string description);

    /// <summary>Aggiunge un messaggio alla sessione.</summary>
    Task AppendAsync(Guid sessionId, string role, string content);

    /// <summary>Elimina una sessione e tutti i suoi messaggi.</summary>
    Task RemoveAsync(Guid sessionId);

    /// <summary>Recupera i messaggi di una sessione in ordine cronologico.</summary>
    Task<IReadOnlyList<SerializableChatMessage>> GetMessagesAsync(Guid sessionId);

    /// <summary>Aggiorna il GeminiChatId di una sessione dopo la navigazione.</summary>
    Task SyncGeminiIdAsync(Guid sessionId, string geminiChatId);

    /// <summary>
    /// Salva lo stato completo della sessione (inclusi campi UI come WorkspacePath).
    /// Utile per gli adapter locali. Gli adapter cloud potrebbero salvare solo
    /// sottoinsiemi supportati dal backend REST (es. la Description).
    /// </summary>
    Task SaveSessionAsync(ChatSession session);
}
