using AIBridge.Shared.Models;

namespace AIBridge.Services;

/// <summary>
/// Interfaccia del client HTTP per il Cloud Brain server.
/// Tutte le chiamate usano il JWT in-memory (mai su disco).
/// In caso di server non disponibile, IsConnected = false e l'app opera in modalità locale.
/// </summary>
public interface ICloudBrainClient
{
    /// <summary>True se il server è raggiungibile e il JWT è valido.</summary>
    bool IsConnected { get; }

    /// <summary>Tier dell'utente corrente (da JWT claim).</summary>
    string CurrentTier { get; }

    /// <summary>Modalità auth corrente: "guest" | "oauth" | "email".</summary>
    string AuthMode { get; }

    /// <summary>Nome o Email per la UI.</summary>
    string DisplayName { get; }

    bool HasValidSavedToken();
    bool AutoLogin();

    void ApplyToken(string token);

    /// <summary>Login Guest via HWID. Salva il JWT in-memory.</summary>
    Task<bool> LoginGuestAsync(string hwid);

    /// <summary>Esegue il logout (elimina cache e token in-memory), poi avvia un nuovo login Guest.</summary>
    Task<bool> LogoutAsync(string hwid);

    /// <summary>Inizia il flusso OAuth, restituisce URL e State.</summary>
    Task<OAuthStartResponse?> StartOAuthAsync(string provider);

    /// <summary>Completa il flusso OAuth con code e state.</summary>
    Task<(string? Token, string? Error)> OAuthCallbackAsync(string provider, string code, string state);

    /// <summary>Avvia il login con Magic Link via Email.</summary>
    Task<MagicLinkStartResponse?> StartMagicLinkAsync(string email);

    /// <summary>Controlla lo stato del Magic Link.</summary>
    Task<MagicLinkStatusResponse?> PollMagicLinkStatusAsync(string sessionId);

    /// <summary>Esegue l'Account Linking tra l'account Guest corrente e il nuovo OAuth.</summary>
    Task<bool> LinkAccountAsync(string oauthToken);

    /// <summary>
    /// Invia il contesto al server e riceve il DecryptedCommand (prompt assemblato).
    /// Restituisce null in caso di errore o server non raggiungibile (graceful degradation).
    /// </summary>
    Task<DecryptedCommand?> GetCommandAsync(ContextRequest request);

    /// <summary>Rinnova il JWT tramite heartbeat. Restituisce false se il server non è disponibile.</summary>
    Task<bool> HeartbeatAsync();

    /// <summary>Recupera i CSS selectors DOM aggiornati dal server (OTA).</summary>
    Task<DomSelectors?> GetSelectorsAsync();

    // ── Chat History ──────────────────────────────────────────────────────────
    Task<List<ChatSessionDto>> GetSessionsAsync();
    Task<ChatSessionDto?> CreateSessionAsync(string agentType, string geminiChatId = "",
                                             string description = "", string workspacePath = "");
    Task UpdateSessionAsync(Guid sessionId, string? description = null,
                            string? workspacePath = null, string? geminiChatId = null, string? agentType = null);
    Task<List<ChatMessageDto>> GetMessagesAsync(Guid sessionId);
    Task SaveMessageAsync(Guid sessionId, string role, string content);
    Task DeleteSessionAsync(Guid sessionId);

    // ── Settings ─────────────────────────────────────────────────────────────
    Task<Dictionary<string, string>> GetSettingsAsync();
    Task UpdateSettingsAsync(Dictionary<string, string> settings);

    // ── Community Agents ─────────────────────────────────────────────────────
    Task<List<CommunityAgentDto>> GetCommunityAgentsAsync();
    Task<(bool Success, string CompositeKey)> SyncCommunityAgentAsync(SyncAgentRequest request);
    Task<bool> SubmitCommunityAgentsAsync(List<string> agentKeys);
}
