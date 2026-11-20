namespace AIBridge.Services.WebChat.Sync;

/// <summary>
/// Risultato di un singolo tick della macchina a stati di streaming.
/// </summary>
public enum StreamingTickResult
{
    /// <summary>Nessun evento da emettere in questo ciclo.</summary>
    None,
    /// <summary>Il testo è cambiato: emettere <c>OnMessageReceivedPartial</c>.</summary>
    Partial,
    /// <summary>
    /// Il testo si è stabilizzato (via soglia o failsafe 80 cicli): emettere
    /// <c>OnMessageCompleted</c> e marcare l'elemento come processato.
    /// Il tracker si auto-azzera.
    /// </summary>
    Completed,
}

/// <summary>
/// Macchina a stati che determina quando un messaggio in streaming è completo o parziale,
/// in base alla visibilità del pulsante Stop e alla stabilità del testo.
///
/// Estratta da <c>GeminiBrowserService.PollAndSyncAsync</c> (righe 787–857).
/// Incapsula <c>_lastStreamedText</c> e <c>_stabilityCount</c> del service originale.
/// Soglie: StopVisibleStallCycles (80 cicli = 40 s) e NoStopStabilityCycles (6 cicli = 3 s).
/// </summary>
public sealed class StreamingStabilityTracker
{
    private string _lastStreamedText = string.Empty;
    private int _stabilityCount;

    private readonly int _stallCycles;       // default 80 — failsafe con Stop visibile
    private readonly int _stabilityCycles;   // default 6  — completamento senza Stop

    public StreamingStabilityTracker(int stallCycles = 80, int stabilityCycles = 6)
    {
        _stallCycles = stallCycles;
        _stabilityCycles = stabilityCycles;
    }

    /// <summary>Azzera la macchina a stati (chiamato dopo ogni Completed o NavigateTo*).</summary>
    public void Reset()
    {
        _lastStreamedText = string.Empty;
        _stabilityCount = 0;
    }

    /// <summary>
    /// Valuta il testo corrente del messaggio in streaming e restituisce il risultato.<br/>
    /// <paramref name="clearWaiting"/>: se true, il chiamante deve impostare
    /// <c>_waitingForResponse = false</c> (il tracker non possiede quel campo).
    /// </summary>
    public StreamingTickResult Tick(
        string currentText,
        bool stopVisible,
        bool waitingForResponse,
        out bool clearWaiting)
    {
        if (stopVisible)
        {
            // Stop visibile → siamo in generazione attiva; _waitingForResponse va sempre azzerato
            clearWaiting = waitingForResponse; // segnala al chiamante di azzerarlo

            if (currentText != _lastStreamedText)
            {
                _lastStreamedText = currentText;
                _stabilityCount = 0;
                return StreamingTickResult.Partial;
            }

            _stabilityCount++;
            if (_stabilityCount >= _stallCycles)
            {
                Reset();
                return StreamingTickResult.Completed;
            }
            return StreamingTickResult.None;
        }
        else
        {
            // Stop non visibile: potrebbe essere risposta molto rapida o testo ancora stabile
            clearWaiting = false;
            if (waitingForResponse && currentText.Length > 0)
            {
                clearWaiting = true;
                waitingForResponse = false; // variabile locale per il resto del tick
            }

            if (!waitingForResponse)
            {
                if (currentText != _lastStreamedText)
                {
                    _lastStreamedText = currentText;
                    _stabilityCount = 0;
                    return StreamingTickResult.Partial;
                }

                _stabilityCount++;
                if (_stabilityCount >= _stabilityCycles)
                {
                    Reset();
                    return StreamingTickResult.Completed;
                }
                return StreamingTickResult.None;
            }
            else
            {
                // Ancora in attesa che il server inizi: se il testo cambia emettiamo Partial
                // (protegge da risposte istantanee senza Stop visibile)
                if (currentText != _lastStreamedText)
                {
                    _lastStreamedText = currentText;
                    _stabilityCount = 0;
                    return StreamingTickResult.Partial;
                }
                return StreamingTickResult.None;
            }
        }
    }
}
