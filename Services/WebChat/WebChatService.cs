using System.Runtime.CompilerServices;
using AIBridge.Services.WebChat.Engines.Gemini;
using AIBridge.Services.WebChat.Sync;
using Microsoft.Playwright;
using Unclassified.TxLib;

[assembly: InternalsVisibleTo("AIBridge.WebChat.Tests")]

namespace AIBridge.Services.WebChat;

/// <summary>
/// Facade principale del sottosistema WebChat (Unit 6).
/// Compone <see cref="BrowserHost"/>, <see cref="ChatSyncEngine"/>,
/// <see cref="HumanInputDriver"/> e <see cref="IChatWebEngine"/>,
/// esponendo il contratto <see cref="ILlmService"/>.
/// </summary>
public sealed class WebChatService : ILlmService
{
    private readonly BrowserHost? _browserHost;
    private readonly Func<IPage?> _pageProvider;
    private readonly IChatWebEngine _engine;
    private readonly ChatSyncState _syncState;
    private readonly ChatSyncEngine _syncEngine;
    private readonly HumanInputDriver _inputDriver;

    private bool _isLoggingIn;

    private IPage? Page => _pageProvider();

    // ── Costruttori ───────────────────────────────────────────────────────────

    /// <summary>
    /// Costruttore di produzione: compone <see cref="BrowserHost"/>, <see cref="ChatSyncEngine"/>
    /// e <see cref="HumanInputDriver"/>.
    /// </summary>
    public WebChatService(
        ISelectorsService? selectors = null,
        IChatWebEngine? engine = null,
        IUiBridge? ui = null,
        IBrowserInstaller? installer = null)
    {
        var selectorsSvc = selectors ?? new SelectorsService();
        var uiBridge     = ui        ?? new WpfUiBridge();
        _engine          = engine    ?? new GeminiWebEngine(selectorsSvc, new GeminiLoginFlow(uiBridge));
        _browserHost     = new BrowserHost(_engine, uiBridge, installer);
        _pageProvider    = () => _browserHost.Page;
        _syncState       = new ChatSyncState(_engine.Timings);
        _syncEngine      = new ChatSyncEngine(_browserHost, _engine, _syncState);
        _inputDriver     = new HumanInputDriver(_browserHost, _engine, fastMode: false);

        WireSyncEvents();
    }

    /// <summary>
    /// Costruttore per test L2 su fixture statiche/dinamiche: riceve un provider di <see cref="IPage"/>
    /// senza avviare un <see cref="BrowserHost"/> persistente.
    /// </summary>
    public WebChatService(
        Func<IPage?> pageProvider,
        IChatWebEngine engine,
        ChatSyncState? state = null,
        bool fastInputMode = true)
    {
        _pageProvider = pageProvider ?? throw new ArgumentNullException(nameof(pageProvider));
        _engine       = engine       ?? throw new ArgumentNullException(nameof(engine));
        _syncState    = state        ?? new ChatSyncState(_engine.Timings);
        _syncEngine   = new ChatSyncEngine(_pageProvider, _engine, _syncState);
        _inputDriver  = new HumanInputDriver(_pageProvider, _engine, fastInputMode);

        WireSyncEvents();
    }

    private void WireSyncEvents()
    {
        _syncEngine.MessageReceivedPartial += (s, text) => OnMessageReceivedPartial?.Invoke(this, text);
        _syncEngine.MessageCompleted       += (s, text) => OnMessageCompleted?.Invoke(this, text);
        _syncEngine.UserPromptDetected     += (s, text) => OnUserPromptDetected?.Invoke(this, text);
        _syncEngine.ChatBlocked            += (s, url)  => OnChatBlocked?.Invoke(this, url);
        _syncEngine.ChatUnblocked          += (s, e)    => OnChatUnblocked?.Invoke(this, e);
        _syncEngine.BusyStateChanged       += (s, busy) => OnBusyStateChanged?.Invoke(this, busy);
        _syncEngine.BrowserClosed          += (s, chatId) => _ = RecoverFromBrowserClosedAsync(chatId);
    }

    /// <summary>Esposto internamente per i test L2 che devono avanzare il watcher di un tick deterministico.</summary>
    internal ChatSyncEngine SyncEngine => _syncEngine;

    // ── Eventi (ILlmService) ──────────────────────────────────────────────────

    public event EventHandler<string>? OnMessageReceived;
    public event EventHandler<string>? OnMessageReceivedPartial;
    public event EventHandler<string>? OnMessageCompleted;
    public event EventHandler<string>? OnUserPromptDetected;
    public event EventHandler<string>? OnError;
    public event EventHandler<string>? OnChatNotFound;
    public event EventHandler<bool>? OnBusyStateChanged;
    public event EventHandler<string>? OnChatBlocked;
    public event EventHandler<EventArgs>? OnChatUnblocked;

    private event EventHandler<EventArgs>? _installingBrowserFallback;

    public event EventHandler<EventArgs>? OnInstallingBrowser
    {
        add
        {
            if (_browserHost != null) _browserHost.InstallingBrowser += value;
            else _installingBrowserFallback += value;
        }
        remove
        {
            if (_browserHost != null) _browserHost.InstallingBrowser -= value;
            else _installingBrowserFallback -= value;
        }
    }

    // ── Proprietà (ILlmService) ───────────────────────────────────────────────

    public bool IsChatBusy => _syncState.IsChatBusy;

    public bool IsInitialized => _browserHost != null
        ? _browserHost.IsInitialized
        : (Page != null && !Page.IsClosed);

    public int ProcessedMessageCount => _syncState.ProcessedMessageCount;

    // ── Metodi (ILlmService) ──────────────────────────────────────────────────

    public async Task InitializeAsync(bool headless = false)
    {
        try
        {
            if (_browserHost != null)
            {
                bool wasAlreadyOpen = _browserHost.IsInitialized;
                await _browserHost.EnsureStartedAsync(headless);
                if (!wasAlreadyOpen)
                {
                    StartWatcher();
                    await ResetWatcherIndexAsync();
                }
            }
            else
            {
                StartWatcher();
                await ResetWatcherIndexAsync();
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"Initialization Error: {ex.Message}");
        }
    }

    public async Task ManualBrowserLoginAsync()
    {
        if (_isLoggingIn) return;
        _isLoggingIn = true;
        _syncState.IsNavigating = true;
        try
        {
            string targetChatId = _syncState.LastActiveChatId;

            if (_browserHost != null)
            {
                var cookies = await _browserHost.RunLoginFlowAsync();
                await InitializeAsync(false);
                if (cookies != null)
                    await _browserHost.InjectCookiesAsync(cookies);
            }

            if (!string.IsNullOrEmpty(targetChatId))
            {
                await NavigateToChatAsync(targetChatId);
            }
            else
            {
                await NewChatAsync();
            }
        }
        finally
        {
            _isLoggingIn = false;
            _syncState.IsNavigating = false;
        }
    }

    public void StartWatcher() => _syncEngine.StartWatcher();

    public void StopWatcher() => _syncEngine.StopWatcher();

    public Task ResetWatcherIndexAsync() => _syncEngine.ResetWatcherIndexAsync();

    public void ForceResetBusyState() => _syncEngine.ForceResetBusyState();

    public async Task SendPromptAsync(string message, string[]? filePaths = null)
    {
        var page = Page;
        if (page == null || page.IsClosed)
        {
            OnError?.Invoke(this, "Il browser è chiuso o in riavvio. Attendi...");
            return;
        }

        try
        {
            _syncState.BeginProgrammaticPrompt(message);

            if (filePaths != null && filePaths.Length > 0)
            {
                try
                {
                    await _engine.AttachFilesAsync(page, filePaths, _inputDriver);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error Uploading File: {ex.Message}");
                }
            }

            await _inputDriver.InsertPromptAsync(message);
            await _inputDriver.SubmitAsync();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"Send Prompt Error: {ex.Message}");
            throw;
        }
    }

    public async Task<string> SendAndAwaitResponseAsync(string message, string[]? filePaths = null)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<string>? completedHandler = null;
        EventHandler<string>? errorHandler = null;

        completedHandler = (s, responseText) =>
        {
            tcs.TrySetResult(responseText);
        };

        errorHandler = (s, errorText) =>
        {
            tcs.TrySetException(new Exception(errorText));
        };

        OnMessageCompleted += completedHandler;
        OnError += errorHandler;

        try
        {
            await SendPromptAsync(message, filePaths);

            int timeoutMs = _engine.Timings.SendResponseTimeoutMs > 0
                ? _engine.Timings.SendResponseTimeoutMs
                : 120_000;

            var completedTask = tcs.Task;
            var timeoutTask = Task.Delay(timeoutMs);

            var finishedTask = await Task.WhenAny(completedTask, timeoutTask);
            if (finishedTask == timeoutTask)
            {
                throw new TimeoutException("Timeout waiting for response.");
            }

            return await completedTask;
        }
        finally
        {
            OnMessageCompleted -= completedHandler;
            OnError -= errorHandler;
        }
    }

    public Task<string> GetCurrentChatIdAsync()
    {
        var page = Page;
        if (page == null) return Task.FromResult(string.Empty);
        try
        {
            return Task.FromResult(_engine.TryExtractChatId(page.Url) ?? string.Empty);
        }
        catch
        {
            return Task.FromResult(string.Empty);
        }
    }

    public async Task NavigateToChatAsync(string chatId)
    {
        if (!string.IsNullOrEmpty(chatId))
            _syncState.LastActiveChatId = chatId;

        var page = Page;
        if (page == null) return;

        _syncState.IsNavigating = true;
        try
        {
            if (await _engine.DetectBlockAsync(page) != null)
            {
                OnChatBlocked?.Invoke(this, page.Url);
                throw new Exception(Tx.T("Google login required"));
            }

            var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var ctx = new NavigationContext(lang);

            var result = await _engine.NavigateToChatAsync(page, chatId, ctx);

            switch (result)
            {
                case NavigationResult.LoginRequired:
                    OnChatBlocked?.Invoke(this, page.Url);
                    throw new Exception(Tx.T("Google login required"));

                case NavigationResult.ChatNotFound:
                    await ResetWatcherIndexAsync();
                    OnChatNotFound?.Invoke(this, chatId);
                    return;

                case NavigationResult.Ok:
                    break;
            }

            // Attendi stabilizzazione dei messaggi NON-stale della nuova chat
            var freshSelector = string.Join(", ",
                _engine.MessageElementsSelector
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => $"{s}:not([data-aibridge-stale])"));

            int prevCount = -1;
            int stableTicks = 0;
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(250);
                int currentCount = await page.Locator(freshSelector).CountAsync();
                if (currentCount > 0 && currentCount == prevCount)
                {
                    stableTicks++;
                    if (stableTicks >= 2) break;
                }
                else
                {
                    stableTicks = 0;
                }
                prevCount = currentCount;
            }

            await ResetWatcherIndexAsync();
        }
        catch (TimeoutException)
        {
            await ResetWatcherIndexAsync();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"Navigate Error: {ex.Message}");
        }
        finally
        {
            _syncState.IsNavigating = false;
        }
    }

    public async Task NewChatAsync()
    {
        var page = Page;
        if (page == null) return;

        _syncState.IsNavigating = true;
        try
        {
            var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            await _engine.NavigateToNewChatAsync(page, new NavigationContext(lang));
            await ResetWatcherIndexAsync();
        }
        catch (TimeoutException)
        {
            await ResetWatcherIndexAsync();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"New Chat Error: {ex.Message}");
        }
        finally
        {
            _syncState.IsNavigating = false;
        }
    }

    public async Task<string?> GetChatTitleAsync(string chatId)
    {
        var page = Page;
        if (page == null || string.IsNullOrEmpty(chatId)) return null;
        return await _engine.GetChatTitleAsync(page, chatId);
    }

    public async Task CloseAsync()
    {
        StopWatcher();
        if (_browserHost != null)
        {
            await _browserHost.CloseAsync();
        }
    }

    private async Task RecoverFromBrowserClosedAsync(string targetChatId)
    {
        OnError?.Invoke(this, "Il browser è stato chiuso! Riavvio in corso...");

        try
        {
            await CloseAsync();
            await InitializeAsync(false);

            if (!string.IsNullOrEmpty(targetChatId))
            {
                await NavigateToChatAsync(targetChatId);
            }
            else
            {
                await NewChatAsync();
            }

            OnError?.Invoke(this, Tx.T("Browser restarted and ready."));
        }
        catch (Exception restartEx)
        {
            OnError?.Invoke(this, Tx.T("Error during automatic restart: {0}", restartEx.Message));
        }
        finally
        {
            _syncState.IsChatBusy = false;
        }
    }
}

