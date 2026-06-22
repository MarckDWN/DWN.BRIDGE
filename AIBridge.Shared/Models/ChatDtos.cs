namespace AIBridge.Shared.Models;

/// <summary>
/// Rappresentazione di una sessione chat restituita dagli endpoint server-side.
/// NON contiene stringhe di connessione, password o dati sensibili.
/// </summary>
public record ChatSessionDto
{
    public Guid Id { get; init; }
    public string AgentType { get; init; } = string.Empty;

    /// <summary>Nome/titolo della sessione impostato dall'utente o dedotto dal primo messaggio.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Percorso workspace locale. Opzionale — non contiene dati sensibili.</summary>
    public string WorkspacePath { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Data di scadenza. NULL = sessione permanente (Pro+).
    /// Per utenti Guest: CreatedAt + 24 ore.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public string GeminiChatId { get; init; } = string.Empty;
}

/// <summary>Rappresentazione di un singolo messaggio della sessione chat.</summary>
public record ChatMessageDto
{
    public long Id { get; init; }

    /// <summary>'user' | 'assistant' | 'system'</summary>
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Request per il salvataggio di un nuovo messaggio (POST /api/v1/chat/messages).</summary>
public record SaveMessageRequest
{
    public Guid SessionId { get; init; }

    /// <summary>'user' | 'assistant' | 'system'</summary>
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;
}

/// <summary>Request per la creazione di una nuova sessione (POST /api/v1/chat/sessions).</summary>
public record CreateSessionRequest
{
    public string AgentType { get; init; } = "Default";
    public string GeminiChatId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
}

/// <summary>Request per aggiornare metadati di una sessione (PATCH /api/v1/chat/sessions/{id}).</summary>
public record UpdateSessionRequest
{
    public string? Description { get; init; }
    public string? WorkspacePath { get; init; }
    public string? GeminiChatId { get; init; }
    public string? AgentType { get; init; }
}
