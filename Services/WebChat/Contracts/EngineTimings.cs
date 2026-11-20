namespace AIBridge.Services.WebChat;

/// <summary>
/// Soglie e timeout del ciclo di sincronizzazione. I default replicano ESATTAMENTE i valori
/// attualmente hardcoded in GeminiBrowserService (Fase 1: zero cambi di comportamento).
/// Ogni engine può sovrascriverli perché webchat diverse streammano con ritmi diversi.
/// </summary>
public sealed record EngineTimings
{
    /// <summary>Intervallo del watcher loop (Task.Delay in WatcherLoopAsync).</summary>
    public int PollIntervalMs { get; init; } = 500;

    /// <summary>
    /// Cicli di poll con testo invariato e stop button visibile prima del failsafe
    /// "generazione bloccata" (80 cicli × 500 ms = 40 s).
    /// </summary>
    public int StopVisibleStallCycles { get; init; } = 80;

    /// <summary>
    /// Cicli di poll con testo invariato e stop button NON visibile prima di considerare
    /// la risposta completata (6 cicli × 500 ms = 3 s).
    /// </summary>
    public int NoStopStabilityCycles { get; init; } = 6;

    /// <summary>Timeout di SendAndAwaitResponseAsync.</summary>
    public int SendResponseTimeoutMs { get; init; } = 120_000;

    /// <summary>Attesa massima della textbox prima dell'invio del prompt.</summary>
    public int TextAreaWaitTimeoutMs { get; init; } = 15_000;

    /// <summary>Timeout delle navigazioni (GotoAsync).</summary>
    public int NavigationTimeoutMs { get; init; } = 8_000;

    /// <summary>Oltre questa lunghezza il prompt è inserito via iniezione JS invece che FillAsync.</summary>
    public int LongMessageThresholdChars { get; init; } = 1_000;

    /// <summary>Pausa dopo il caricamento di un allegato.</summary>
    public int FileUploadSettleMs { get; init; } = 5_000;

    /// <summary>Se l'installazione di Chromium supera questa soglia si notifica "download in corso".</summary>
    public int BrowserInstallNoticeDelayMs { get; init; } = 500;
}
