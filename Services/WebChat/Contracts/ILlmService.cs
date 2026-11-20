namespace AIBridge.Services.WebChat;

/// <summary>
/// Superficie pubblica usata dal MainViewModel.
/// I nomi dei membri sono generici (non contengono "Gemini"); il parity test U1 verifica la
/// corrispondenza strutturale (tipi, firme, valori default) con GeminiBrowserService.
/// I nomi Gemini-specifici nel VM (IsGeminiBusy, _isGeminiBusy XAML) sono debito D4, rimandato a Fase 2.
/// </summary>
public interface ILlmService
{
    // ── Eventi ──
    event EventHandler<string>? OnMessageReceived;
    event EventHandler<string>? OnMessageReceivedPartial;
    event EventHandler<string>? OnMessageCompleted;
    event EventHandler<string>? OnUserPromptDetected;
    event EventHandler<string>? OnError;
    event EventHandler<string>? OnChatNotFound;
    event EventHandler<bool>? OnBusyStateChanged;
    event EventHandler<string>? OnChatBlocked;
    event EventHandler<EventArgs>? OnChatUnblocked;
    event EventHandler<EventArgs>? OnInstallingBrowser;

    // ── Proprietà ──
    bool IsChatBusy { get; }
    bool IsInitialized { get; }
    int ProcessedMessageCount { get; }

    // ── Metodi ──
    Task InitializeAsync(bool headless = false);
    Task ManualBrowserLoginAsync();
    void StartWatcher();
    void StopWatcher();
    Task ResetWatcherIndexAsync();
    Task SendPromptAsync(string message, string[]? filePaths = null);
    Task<string> SendAndAwaitResponseAsync(string message, string[]? filePaths = null);
    Task<string> GetCurrentChatIdAsync();
    Task NavigateToChatAsync(string chatId);
    Task NewChatAsync();
    Task<string?> GetChatTitleAsync(string chatId);
    void ForceResetBusyState();
    Task CloseAsync();
}
