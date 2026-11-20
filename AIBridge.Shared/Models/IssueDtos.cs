namespace AIBridge.Shared.Models;

/// <summary>
/// Payload inviato dal client per segnalare un problema o un feedback.
/// POST /api/v1/issues/submit — endpoint pubblico (anonimo + autenticato).
/// </summary>
public record SubmitIssueRequest
{
    /// <summary>Categorie selezionate dall'utente (es. ["Workspace", "Syntax"]).</summary>
    public List<string> SelectedIssueTypes { get; init; } = new();

    /// <summary>Descrizione testuale libera del problema (min 10 caratteri raccomandato).</summary>
    public string UserDescription { get; init; } = string.Empty;

    /// <summary>
    /// Se true, l'utente ha dato consenso esplicito a collegare la sessione corrente.
    /// ActiveSessionId sarà valorizzato solo in questo caso.
    /// </summary>
    public bool ShareConversationConsent { get; init; }

    /// <summary>
    /// GUID della sessione ChatSessions attiva al momento della segnalazione.
    /// Inviato SOLO se ShareConversationConsent == true.
    /// </summary>
    public Guid? ActiveSessionId { get; init; }

    /// <summary>Versione dell'app client (es. "1.0.2.0").</summary>
    public string? ClientVersion { get; init; }

    /// <summary>Informazioni sul sistema operativo (es. "Windows 11 x64 22H2").</summary>
    public string? OsInfo { get; init; }

    /// <summary>Nome dell'agente selezionato al momento della segnalazione.</summary>
    public string? SelectedAgentName { get; init; }
}

/// <summary>
/// Risposta restituita dal server dopo l'invio di una segnalazione.
/// </summary>
public record SubmitIssueResponse
{
    public bool Success { get; init; }
    public Guid? IssueId { get; init; }
    public string? ErrorMessage { get; init; }
}

public record UserIssueDto
{
    public Guid Id { get; init; }
    public DateTimeOffset SubmitDate { get; init; }
    public string Status { get; init; } = "Open";
    public string IssueTypes { get; init; } = string.Empty;
    public string UserDescription { get; init; } = string.Empty;
    public string? AdminNote { get; init; }
    public string? FeedbackRequest { get; init; }
}

public record RespondToIssueRequest(string ResponseText);
