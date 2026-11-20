using System.Diagnostics;
using Microsoft.Playwright;

namespace AIBridge.Services.WebChat.Sync;

/// <summary>
/// Motore di sincronizzazione DOM ↔ AIBridge (Unit 5).
/// Gestisce il watcher loop, il rilevamento di stato busy/blocco, l'allineamento
/// dei messaggi (con <see cref="MessageAligner"/>) e lo streaming (con <see cref="StreamingStabilityTracker"/>).
/// Non contiene literal specifici di Gemini: interroga esclusivamente <see cref="IChatWebEngine"/>.
/// </summary>
public sealed class ChatSyncEngine
{
    private readonly Func<IPage?> _pageProvider;
    private readonly IChatWebEngine _engine;
    private volatile bool _watcherActive;
    private Task? _watcherTask;

    public ChatSyncState State { get; }

    public bool IsWatcherActive => _watcherActive;

    // ── Eventi ────────────────────────────────────────────────────────────────

    public event EventHandler<string>? MessageReceivedPartial;
    public event EventHandler<string>? MessageCompleted;
    public event EventHandler<string>? UserPromptDetected;
    public event EventHandler<string>? ChatBlocked;
    public event EventHandler<EventArgs>? ChatUnblocked;

    /// <summary>
    /// Emesso quando il watcher rileva che la pagina del browser è stata chiusa dall'utente.
    /// L'argomento è <see cref="ChatSyncState.LastActiveChatId"/> per il ripristino automatico.
    /// </summary>
    public event EventHandler<string>? BrowserClosed;

    public event EventHandler<bool>? BusyStateChanged
    {
        add    => State.BusyStateChanged += value;
        remove => State.BusyStateChanged -= value;
    }

    // ── Costruttori ───────────────────────────────────────────────────────────

    public ChatSyncEngine(BrowserHost browserHost, IChatWebEngine engine, ChatSyncState? state = null)
        : this(() => browserHost.Page, engine, state)
    {
    }

    public ChatSyncEngine(Func<IPage?> pageProvider, IChatWebEngine engine, ChatSyncState? state = null)
    {
        _pageProvider = pageProvider ?? throw new ArgumentNullException(nameof(pageProvider));
        _engine       = engine       ?? throw new ArgumentNullException(nameof(engine));
        State         = state        ?? new ChatSyncState(engine.Timings);
    }

    // ── Controllo Watcher ─────────────────────────────────────────────────────

    public void StartWatcher()
    {
        if (_watcherActive) return;
        _watcherActive = true;
        _watcherTask = Task.Run(WatcherLoopAsync);
    }

    public void StopWatcher()
    {
        _watcherActive = false;
    }

    private async Task WatcherLoopAsync()
    {
        int pollMs = _engine.Timings.PollIntervalMs > 0 ? _engine.Timings.PollIntervalMs : 500;

        while (_watcherActive)
        {
            var page = _pageProvider();

            try
            {
                if (page != null && !page.IsClosed && !State.IsNavigating)
                {
                    await TickOnceAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watcher] Loop error: {ex.Message}");
            }

            // Verifica esplicita se la pagina è stata chiusa dall'utente
            page = _pageProvider();
            if (_watcherActive && page != null && page.IsClosed)
            {
                _watcherActive = false;
                State.IsChatBusy = true;
                BrowserClosed?.Invoke(this, State.LastActiveChatId);
                break;
            }

            await Task.Delay(pollMs);
        }
    }

    /// <summary>
    /// Esegue un singolo ciclo di controllo del watcher (blocco, busy state, polling messaggi).
    /// Esposto pubblicamente per consentire test deterministici L2 senza dipendere da timer.
    /// </summary>
    public async Task TickOnceAsync()
    {
        var page = _pageProvider();
        if (page == null) return;

        if (page.IsClosed)
        {
            if (_watcherActive)
            {
                _watcherActive = false;
                State.IsChatBusy = true;
                BrowserClosed?.Invoke(this, State.LastActiveChatId);
            }
            return;
        }

        if (State.IsNavigating) return;

        if (await _engine.DetectBlockAsync(page) != null)
        {
            if (!State.WasBlocked)
            {
                State.WasBlocked = true;
                ChatBlocked?.Invoke(this, page.Url);
            }
            State.IsChatBusy = true;
        }
        else
        {
            if (State.WasBlocked)
            {
                State.WasBlocked = false;
                ChatUnblocked?.Invoke(this, EventArgs.Empty);
            }
            await UpdateBusyStateAsync();
            await PollAndSyncAsync();
        }
    }

    // ── Busy State & Stop Button ──────────────────────────────────────────────

    public void ForceResetBusyState()
    {
        State.IsChatBusy = false;
        State.WaitingForResponse = false;
        State.Tracker.Reset();

        _ = Task.Run(ForceResetBusyStateCoreAsync);
    }

    /// <summary>
    /// Versione attendibile di <see cref="ForceResetBusyState"/> per i test L2.
    /// </summary>
    public async Task ForceResetBusyStateAsync()
    {
        State.IsChatBusy = false;
        State.WaitingForResponse = false;
        State.Tracker.Reset();

        await ForceResetBusyStateCoreAsync();
    }

    private async Task ForceResetBusyStateCoreAsync()
    {
        try
        {
            var page = _pageProvider();
            if (page != null && !page.IsClosed)
            {
                var stopBtn = page.Locator(_engine.BuildStopButtonSelector()).First;
                if (await stopBtn.CountAsync() > 0 && await stopBtn.IsVisibleAsync())
                {
                    await stopBtn.ClickAsync();
                    await Task.Delay(1500);
                }

                if (await stopBtn.CountAsync() > 0 && await stopBtn.IsVisibleAsync())
                {
                    Console.WriteLine("[DWN.Bridge] Failsafe: Stop button still visible. Triggering page reload...");
                    await page.ReloadAsync(new PageReloadOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 10000
                    });
                }
            }
        }
        catch { }
    }

    public async Task UpdateBusyStateAsync()
    {
        var page = _pageProvider();
        if (page == null) return;

        try
        {
            // Aggiorna l'ID della chat corrente per il recupero in caso di crash/chiusura/login
            try
            {
                var extractedId = _engine.TryExtractChatId(page.Url);
                if (!string.IsNullOrEmpty(extractedId))
                {
                    State.LastActiveChatId = extractedId;
                }
            }
            catch { }

            bool isBusy = false;
            var stopBtn = page.Locator(_engine.BuildStopButtonSelector()).First;

            if (await stopBtn.CountAsync() > 0 && await stopBtn.IsVisibleAsync())
            {
                isBusy = true;
                State.WaitingForResponse = false;
            }
            else if (State.WaitingForResponse)
            {
                isBusy = true;
            }

            State.IsChatBusy = isBusy;
        }
        catch
        {
            State.IsChatBusy = false;
        }
    }

    // ── Allineamento & Polling DOM ────────────────────────────────────────────

    public async Task<int> GetFirstNewElementIndexAsync(ILocator elements, int count)
    {
        if (count == 0) return 0;

        var page = _pageProvider();
        if (page == null) return 0;

        // 1. Singola query JS in batch O(1) per trovare l'ultimo elemento marcato
        int lastProcessedDomIndex = -1;
        try
        {
            lastProcessedDomIndex = await page.EvaluateAsync<int>(
                @"(selector) => {
                    const all = Array.from(document.querySelectorAll(selector));
                    for (let i = all.length - 1; i >= 0; i--) {
                        if (all[i].getAttribute('data-aibridge-processed') === 'true') return i;
                    }
                    return -1;
                }",
                _engine.MessageElementsSelector
            );
        }
        catch
        {
            // Fallback testuale se EvaluateAsync fallisce durante transizioni
        }

        if (lastProcessedDomIndex != -1)
        {
            return lastProcessedDomIndex + 1;
        }

        // 2. Fallback testuale tramite MessageAligner (U2)
        var snapshots = new List<DomMessageSnapshot>(count);
        for (int i = 0; i < count; i++)
        {
            var el = elements.Nth(i);
            var tag = await el.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
            string text = string.Empty;
            var role = await _engine.ClassifyAsync(el);
            if (role == MessageRole.User)
                text = (await el.InnerTextAsync() ?? string.Empty).Trim();
            else if (role == MessageRole.Assistant)
                text = await _engine.ReadMessageAsync(el, role, page);
            snapshots.Add(new DomMessageSnapshot(text, tag));
        }

        return MessageAligner.FindFirstNewIndex(
            snapshots,
            State.GetProcessedSnapshot(),
            State.WaitingForResponse,
            State.IsChatBusy);
    }

    public async Task PollAndSyncAsync()
    {
        var page = _pageProvider();
        if (page == null) return;

        var elements = page.Locator(_engine.MessageElementsSelector);
        int count = await elements.CountAsync();

        int firstNewIndex = await GetFirstNewElementIndexAsync(elements, count);

        // Guard anti-reiniezione: se l'allineatore dice "tutto nuovo" (firstNewIndex==0)
        // ma abbiamo già messaggi processati e non stiamo aspettando una risposta,
        // ignora il ciclo.
        if (firstNewIndex == 0
            && State.ProcessedMessageCount > 0
            && count > 0
            && !State.WaitingForResponse
            && !State.IsNavigating)
        {
            Debug.WriteLine($"[Watcher] Anti-reiniezione: firstNewIndex=0, processed={State.ProcessedMessageCount}, DOM={count}. Skipping.");
            return;
        }

        if (firstNewIndex < count)
        {
            // Guard per hydration storica a riposo (post-navigazione SPA o caricamento chat esistente S5):
            // se non stiamo aspettando risposta e compaiono più di 2 elementi non marcati senza Stop visibile
            // (2 elementi possono essere un singolo turno S10 prompt+risposta scritto nel browser),
            // allinea l'indice silenziosamente senza emettere eventi.
            if (!State.WaitingForResponse
                && !State.IsChatBusy
                && State.ProgrammaticPromptText == null
                && (count - firstNewIndex) > 2)
            {
                bool stopVisible = false;
                try
                {
                    var stopBtn = page.Locator(_engine.BuildStopButtonSelector()).First;
                    stopVisible = await stopBtn.CountAsync() > 0 && await stopBtn.IsVisibleAsync();
                }
                catch { }

                if (!stopVisible)
                {
                    Debug.WriteLine($"[Watcher] Hydration storica rilevata ({count - firstNewIndex} elementi a riposo). Allineo indice senza riemettere eventi.");
                    await ResetWatcherIndexAsync(scheduleLazyCatch: false);
                    return;
                }
            }

            for (int i = firstNewIndex; i < count; i++)
            {
                var el = elements.Nth(i);
                var role = await _engine.ClassifyAsync(el);

                if (role == MessageRole.User)
                {
                    var text = (await el.InnerTextAsync() ?? string.Empty).Trim();
                    bool isProgrammatic = false;
                    var progText = State.ProgrammaticPromptText;
                    if (progText != null)
                    {
                        var cleanText = text.Replace("\r", "").Replace("\n", "").Replace(" ", "");
                        var cleanProg = progText.Replace("\r", "").Replace("\n", "").Replace(" ", "");
                        if (cleanText.Contains(cleanProg) || cleanProg.Contains(cleanText))
                        {
                            isProgrammatic = true;
                            State.ProgrammaticPromptText = null;
                        }
                    }

                    if (!isProgrammatic)
                    {
                        UserPromptDetected?.Invoke(this, text);
                    }

                    State.AddProcessed(text);
                    try { await el.EvaluateAsync("el => el.setAttribute('data-aibridge-processed', 'true')"); } catch { }
                    State.LastProcessedIndex = i;
                    State.Tracker.Reset();
                }
                else if (role == MessageRole.Assistant)
                {
                    if (i < count - 1)
                    {
                        var text = await _engine.ReadMessageAsync(el, MessageRole.Assistant, page);
                        if (State.WaitingForResponse || State.IsChatBusy)
                        {
                            State.WaitingForResponse = false;
                            MessageCompleted?.Invoke(this, text);
                        }
                        State.AddProcessed(text);
                        try { await el.EvaluateAsync("el => el.setAttribute('data-aibridge-processed', 'true')"); } catch { }
                        State.LastProcessedIndex = i;
                        State.Tracker.Reset();
                    }
                    else
                    {
                        var text = await _engine.ReadMessageAsync(el, MessageRole.Assistant, page);
                        if (string.IsNullOrEmpty(text)) continue;

                        bool stopBtnVisible = false;
                        try
                        {
                            var stopBtn = page.Locator(_engine.BuildStopButtonSelector()).First;
                            stopBtnVisible = await stopBtn.CountAsync() > 0 && await stopBtn.IsVisibleAsync();
                        }
                        catch { }

                        var tickResult = State.Tracker.Tick(text, stopBtnVisible, State.WaitingForResponse, out bool clearWaiting);
                        if (clearWaiting) State.WaitingForResponse = false;

                        switch (tickResult)
                        {
                            case StreamingTickResult.Partial:
                                MessageReceivedPartial?.Invoke(this, text);
                                break;

                            case StreamingTickResult.Completed:
                                ForceResetBusyState();
                                MessageCompleted?.Invoke(this, text);
                                State.AddProcessed(text);
                                try { await el.EvaluateAsync("el => el.setAttribute('data-aibridge-processed', 'true')"); } catch { }
                                State.LastProcessedIndex = i;
                                break;
                        }
                    }
                }
            }
        }
    }

    public async Task ResetWatcherIndexAsync(bool scheduleLazyCatch = true)
    {
        var page = _pageProvider();
        if (page == null) return;

        try
        {
            var selector = _engine.MessageElementsSelector;
            var elements = page.Locator(selector);
            int count = await elements.CountAsync();

            State.ClearProcessed();

            // Singolo EvaluateAsync bulk O(1): marca tutti gli elementi presenti come processati
            // e rimuove l'eventuale flag stale pre-navigazione.
            await page.EvaluateAsync(
                @"(selector) => {
                    document.querySelectorAll(selector).forEach(el => {
                        el.setAttribute('data-aibridge-processed', 'true');
                        el.removeAttribute('data-aibridge-stale');
                    });
                }",
                selector);

            // Popola placeholder per i messaggi storici e leggi solo gli ultimi 3 per l'allineamento
            int readFrom = Math.Max(0, count - 3);
            for (int i = 0; i < readFrom; i++)
            {
                State.AddProcessed(string.Empty);
            }
            for (int i = readFrom; i < count; i++)
            {
                var el = elements.Nth(i);
                string text = string.Empty;
                try
                {
                    var role = await _engine.ClassifyAsync(el);
                    if (role == MessageRole.User)
                        text = (await el.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3000 }) ?? string.Empty).Trim();
                    else if (role == MessageRole.Assistant)
                        text = await _engine.ReadMessageAsync(el, role, page);
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine($"[Watcher] Reset: timeout su elemento {i}, uso placeholder vuoto.");
                }
                State.AddProcessed(text);
            }

            State.LastProcessedIndex = count - 1;
            State.Tracker.Reset();
            Debug.WriteLine($"[Watcher] Reset completato: {count} elementi marcati, letti ultimi {State.ProcessedMessageCount}.");

            if (scheduleLazyCatch)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2500);
                    if (page == null || page.IsClosed || State.IsNavigating) return;
                    try
                    {
                        await page.EvaluateAsync(
                            @"(selector) => {
                                document.querySelectorAll(selector).forEach(el =>
                                    el.setAttribute('data-aibridge-processed', 'true')
                                );
                            }",
                            selector);
                        var newCount = await elements.CountAsync();
                        if (newCount != count)
                        {
                            State.LastProcessedIndex = newCount - 1;
                            Debug.WriteLine($"[Watcher] Lazy-load catch: marcati {newCount - count} nuovi elementi.");
                        }
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watcher] Reset index error: {ex.Message}");
        }
    }
}

