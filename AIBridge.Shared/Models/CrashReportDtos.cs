namespace AIBridge.Shared.Models;

/// <summary>
/// Payload inviato automaticamente dal client quando si verifica un'eccezione non gestita.
/// POST /api/v1/telemetry/crash — endpoint pubblico (anonimo + autenticato).
/// </summary>
public record CrashReportRequest
{
    /// <summary>HWID della macchina (se disponibile, anche senza login).</summary>
    public string? Hwid { get; init; }

    /// <summary>Versione dell'app client (es. "1.4.2.0").</summary>
    public string? ClientVersion { get; init; }

    /// <summary>Sistema operativo e architettura (es. "Windows 11 x64 22H2").</summary>
    public string? OsInfo { get; init; }

    /// <summary>
    /// Fase dell'app in cui si è verificato il crash.
    /// Valori validi: "Startup", "Runtime", "Shutdown".
    /// </summary>
    public string? CrashPhase { get; init; }

    /// <summary>Secondi trascorsi dall'avvio dell'app al momento del crash.</summary>
    public int? AppUptimeSeconds { get; init; }

    /// <summary>Tipo completo dell'eccezione (es. "System.NullReferenceException").</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Messaggio dell'eccezione (troncato a 1000 caratteri).</summary>
    public string? ExceptionMessage { get; init; }

    /// <summary>Stack trace completo dell'eccezione.</summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// SHA-256 hex dei primi 500 caratteri dello stack trace.
    /// Usato per deduplicazione lato server.
    /// </summary>
    public string? StackTraceHash { get; init; }
}

/// <summary>Risposta dell'endpoint crash — minima, non rivela informazioni interne.</summary>
public record CrashReportResponse
{
    public bool Accepted { get; init; }
}
