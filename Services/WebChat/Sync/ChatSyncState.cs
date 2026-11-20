using AIBridge.Services.WebChat;

namespace AIBridge.Services.WebChat.Sync;

/// <summary>
/// Stato condiviso della macchina di sincronizzazione (<see cref="ChatSyncEngine"/>).
/// Incapsula in modo thread-safe (R1) i campi che in precedenza vivevano direttamente
/// dentro <c>GeminiBrowserService</c>.
/// </summary>
public sealed class ChatSyncState
{
    private readonly object _lock = new();
    private readonly List<string> _processedMessagesText = new();

    private volatile int _lastProcessedIndex = -1;
    private volatile string? _programmaticPromptText;
    private volatile bool _waitingForResponse;
    private volatile bool _isNavigating;
    private volatile bool _wasBlocked;
    private volatile bool _isChatBusy;
    private volatile string _lastActiveChatId = string.Empty;

    public StreamingStabilityTracker Tracker { get; }

    public event EventHandler<bool>? BusyStateChanged;

    public ChatSyncState(EngineTimings? timings = null)
    {
        var t = timings ?? new EngineTimings();
        Tracker = new StreamingStabilityTracker(t.StopVisibleStallCycles, t.NoStopStabilityCycles);
    }

    /// <summary>
    /// Numero di messaggi (prompt utente + risposte AI) registrati come elaborati
    /// nella sessione corrente.
    /// </summary>
    public int ProcessedMessageCount
    {
        get
        {
            lock (_lock)
            {
                return _processedMessagesText.Count;
            }
        }
    }

    /// <summary>
    /// Restituisce uno snapshot immutabile dei messaggi elaborati (thread-safe per letture concorrenti).
    /// </summary>
    public IReadOnlyList<string> GetProcessedSnapshot()
    {
        lock (_lock)
        {
            return _processedMessagesText.ToArray();
        }
    }

    public void AddProcessed(string text)
    {
        lock (_lock)
        {
            _processedMessagesText.Add(text ?? string.Empty);
        }
    }

    public void ClearProcessed()
    {
        lock (_lock)
        {
            _processedMessagesText.Clear();
        }
    }

    public int LastProcessedIndex
    {
        get => _lastProcessedIndex;
        set => _lastProcessedIndex = value;
    }

    public string? ProgrammaticPromptText
    {
        get => _programmaticPromptText;
        set => _programmaticPromptText = value;
    }

    public bool WaitingForResponse
    {
        get => _waitingForResponse;
        set => _waitingForResponse = value;
    }

    public bool IsNavigating
    {
        get => _isNavigating;
        set => _isNavigating = value;
    }

    public bool WasBlocked
    {
        get => _wasBlocked;
        set => _wasBlocked = value;
    }

    public string LastActiveChatId
    {
        get => _lastActiveChatId;
        set => _lastActiveChatId = value ?? string.Empty;
    }

    public bool IsChatBusy
    {
        get => _isChatBusy;
        set
        {
            bool changed = false;
            lock (_lock)
            {
                if (_isChatBusy != value)
                {
                    _isChatBusy = value;
                    changed = true;
                }
            }
            if (changed)
            {
                BusyStateChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Prepara lo stato prima dell'invio di un prompt programmatico da AIBridge.
    /// </summary>
    public void BeginProgrammaticPrompt(string message)
    {
        ProgrammaticPromptText = message.Trim();
        WaitingForResponse = true;
        IsChatBusy = true;
        Tracker.Reset();
    }
}
