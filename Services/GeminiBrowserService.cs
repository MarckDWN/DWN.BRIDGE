using Microsoft.Playwright;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AIBridge.Services
{
    public class GeminiBrowserService
    {
        private IPlaywright? _playwright;
        private IBrowserContext? _browserContext;
        private IPage? _page;
        
        // Path to store session cookies/state so you don't have to login every time
        private readonly string _userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBridge", "GeminiProfile");
        
        private readonly ISelectorsService _selectors;

        public GeminiBrowserService(ISelectorsService? selectors = null)
        {
            _selectors = selectors ?? new SelectorsService();
        }

        public event EventHandler<string>? OnMessageReceived;
        public event EventHandler<string>? OnMessageReceivedPartial;
        public event EventHandler<string>? OnMessageCompleted;
        public event EventHandler<string>? OnUserPromptDetected;
        public event EventHandler<string>? OnError;
        public event EventHandler<string>? OnChatNotFound;
        public event EventHandler<bool>? OnBusyStateChanged;
        public event EventHandler<string>? OnGeminiBlocked;
        public event EventHandler<EventArgs>? OnGeminiUnblocked;
        public event EventHandler<EventArgs>? OnInstallingBrowser;

        private int _lastProcessedIndex = -1;
        private string _lastStreamedText = string.Empty;
        private int _stabilityCount = 0;
        private const int StabilityThreshold = 10; // 5 seconds at 500ms intervals

        private string? _programmaticPromptText = null;
        private bool _watcherActive = false;
        private Task? _watcherTask;
        private bool _isNavigating = false;
        private bool _isGeminiBusy = false;
        private bool _wasBlocked = false;

        // Shared Random for human-like timing
        private static readonly Random _rng = new();

        public bool IsGeminiBusy
        {
            get => _isGeminiBusy;
            private set
            {
                if (_isGeminiBusy != value)
                {
                    _isGeminiBusy = value;
                    OnBusyStateChanged?.Invoke(this, _isGeminiBusy);
                }
            }
        }

        public void ForceResetBusyState()
        {
            IsGeminiBusy = false;
            
            // Forza fisicamente il click sul pulsante Stop del browser se visibile
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_page != null && !_page.IsClosed)
                    {
                        var stopBtn = _page.Locator("button[aria-label='Stop generating']");
                        if (await stopBtn.IsVisibleAsync())
                        {
                            await stopBtn.ClickAsync();
                        }
                    }
                }
                catch { }
            });
        }

        /// <summary>True se il browser Playwright è stato avviato ed è ancora aperto.</summary>
        public bool IsInitialized => _page != null && !_page.IsClosed;

        // ─── STEALTH: Additional browser args and JS injections ──────────────────

        /// <summary>
        /// Args aggiuntivi per nascondere le tracce di automazione Chromium.
        /// Mirano ai controlli più comuni usati da Google per rilevare i bot.
        /// </summary>
        private static readonly string[] StealthArgs = new[]
        {
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-infobars",
            "--disable-notifications",
            "--lang=it-IT,it,en-US,en",
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding",
        };

        /// <summary>
        /// Script JS iniettato su ogni pagina prima che il codice della pagina giri.
        /// Sovrascrive le proprietà più facili da rilevare da parte degli anti-bot.
        /// </summary>
        private const string StealthInitScript = @"
// 1. Rimuovi navigator.webdriver (la traccia più ovvia di Playwright/Selenium)
Object.defineProperty(navigator, 'webdriver', { get: () => undefined });

// 2. Ripristina i plugin (browser reali ne hanno molti, Playwright zero)
Object.defineProperty(navigator, 'plugins', {
    get: () => [1, 2, 3, 4, 5],
});

// 3. Ripristina le lingue 
Object.defineProperty(navigator, 'languages', {
    get: () => ['it-IT', 'it', 'en-US', 'en'],
});

// 4. Nascondi la proprietà chrome usata da alcuni rilevatori
window.chrome = { runtime: {} };

// 5. Evita il rilevamento tramite permission API (i bot non chiedono permessi)
const originalQuery = window.navigator.permissions.query;
window.navigator.permissions.query = (parameters) =>
    parameters.name === 'notifications'
        ? Promise.resolve({ state: Notification.permission })
        : originalQuery(parameters);
";

        public async Task ManualBrowserLoginAsync()
        {
            await CloseAsync(); // Sblocca la cartella profilo di Playwright
            
            System.Collections.Generic.List<Microsoft.Playwright.Cookie>? pwCookies = null;

            // Usiamo il Dispatcher per aprire la finestra WPF che contiene la WebView2
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var loginWindow = new Dialogs.ManualLoginWindow(_userDataDir)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                loginWindow.ShowDialog();

                if (loginWindow.ExtractedCookies != null)
                {
                    pwCookies = new System.Collections.Generic.List<Microsoft.Playwright.Cookie>();
                    foreach (var c in loginWindow.ExtractedCookies)
                    {
                        pwCookies.Add(new Microsoft.Playwright.Cookie
                        {
                            Name = c.Name,
                            Value = c.Value,
                            Domain = c.Domain,
                            Path = c.Path,
                            Secure = c.IsSecure,
                            HttpOnly = c.IsHttpOnly,
                            Expires = (c.Expires > new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)) ? new DateTimeOffset(c.Expires).ToUnixTimeSeconds() : -1
                        });
                    }
                }
            });
            
            // Una volta chiusa la finestra (perché l'utente si è loggato), riavviamo Playwright
            await InitializeAsync(false);

            if (pwCookies != null && _browserContext != null)
            {
                try { await _browserContext.AddCookiesAsync(pwCookies); } catch { }
            }

            await NewChatAsync();
        }

        public async Task InitializeAsync(bool headless = false)
        {
            // Se è già aperto, porta solo la tab di Gemini in primo piano
            if (IsInitialized)
            {
                try { await _page!.BringToFrontAsync(); }
                catch { }
                return;
            }

            try
            {
                // Esegue l'installer automatico di Playwright (Chromium) in background.
                // Essenziale sui nuovi PC dove il browser non è ancora scaricato.
                var installTask = Task.Run(() => 
                {
                    Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                });

                // Se l'installazione ci mette più di 500ms, significa che sta scaricando il browser.
                if (await Task.WhenAny(installTask, Task.Delay(500)) != installTask)
                {
                    OnInstallingBrowser?.Invoke(this, EventArgs.Empty);
                }
                await installTask;

                _playwright = await Playwright.CreateAsync();

                var combinedArgs = new System.Collections.Generic.List<string>(StealthArgs);
                if (!headless)
                {
                    combinedArgs.Add("--window-size=1024,768");
                    combinedArgs.Add("--window-position=50,50");
                }

                var options = new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = headless,
                    Args = combinedArgs,
                    IgnoreDefaultArgs = new[] { "--enable-automation", "--no-sandbox" }, // Rimuove banner automazione e warning sandbox
                    ViewportSize = new ViewportSize { Width = 1024, Height = 768 },
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36",
                    Locale = "it-IT",
                    TimezoneId = "Europe/Rome",
                    Geolocation = new Geolocation { Latitude = 41.9f, Longitude = 12.5f },
                    Permissions = new[] { "geolocation" }
                };

                _browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(_userDataDir, options);

                // Inietta lo script stealth prima che qualsiasi pagina giri il suo JS
                await _browserContext.AddInitScriptAsync(StealthInitScript);

                _page = _browserContext.Pages.Count > 0 ? _browserContext.Pages[0] : await _browserContext.NewPageAsync();
                
                _page.Response += async (sender, response) =>
                {
                    if (response.Url.Contains("batchexecute") && response.Status == 0)
                    {
                        Console.WriteLine("[DWN.Bridge] Detected Gemini SPA Crash. Triggering auto-refresh...");
                        try
                        {
                            await _page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
                        }
                        catch { }
                    }
                };

                _page.DOMContentLoaded += async (sender, e) =>
                {
                    try
                    {
                        await _page.AddStyleTagAsync(new PageAddStyleTagOptions 
                        { 
                            Content = "[id*='upsell'], [class*='upsell'], [id*='popover'], .experimental-mode-upsell { display: none !important; }" 
                        });
                    }
                    catch { }
                };

                StartWatcher();
                await ResetWatcherIndexAsync();
                
                // Forza l'app in primo piano (in modo che la finestra di Chromium vada in secondo piano)
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow;
                    if (mainWindow != null)
                    {
                        if (mainWindow.WindowState == System.Windows.WindowState.Minimized)
                            mainWindow.WindowState = System.Windows.WindowState.Normal;
                        
                        mainWindow.Activate();
                        mainWindow.Topmost = true;
                        mainWindow.Topmost = false;
                        mainWindow.Focus();
                    }
                });
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Initialization Error: {ex.Message}");
            }
        }

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
            while (_watcherActive)
            {
                try
                {
                    if (IsInitialized && !_isNavigating)
                    {
                        if (await IsGeminiBlockedAsync())
                        {
                            if (!_wasBlocked)
                            {
                                _wasBlocked = true;
                                OnGeminiBlocked?.Invoke(this, _page!.Url);
                            }
                            IsGeminiBusy = true; // Blocca input utente
                        }
                        else
                        {
                            if (_wasBlocked)
                            {
                                _wasBlocked = false;
                                OnGeminiUnblocked?.Invoke(this, EventArgs.Empty);
                            }
                            await UpdateBusyStateAsync();
                            await PollAndSyncAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Watcher] Loop error: {ex.Message}");
                }

                // Verifica esplicita se la pagina è stata chiusa dall'utente (invece di basarci solo sulle eccezioni)
                if (_watcherActive && _page != null && _page.IsClosed)
                {
                    _watcherActive = false;
                    IsGeminiBusy = true; // Blocca la chat nella UI
                    
                    OnError?.Invoke(this, "Il browser è stato chiuso! Riavvio in corso...");
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string targetChatId = _lastActiveChatId;
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
                            
                            OnError?.Invoke(this, Unclassified.TxLib.Tx.T("Browser restarted and ready."));
                        }
                        catch (Exception restartEx)
                        {
                            OnError?.Invoke(this, Unclassified.TxLib.Tx.T("Error during automatic restart: {0}", restartEx.Message));
                        }
                        finally
                        {
                            IsGeminiBusy = false;
                        }
                    });
                    break;
                }

                await Task.Delay(500);
            }
        }

        private string _lastActiveChatId = string.Empty;

        private async Task UpdateBusyStateAsync()
        {
            if (_page == null) return;
            try
            {
                // Aggiorniamo l'ID della chat corrente per il recupero in caso di crash
                try
                {
                    var uri = new Uri(_page.Url);
                    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0] == "app")
                    {
                        _lastActiveChatId = segments[1];
                    }
                }
                catch { }

                bool isBusy = false;

                // Strategy 1: look for the stop/interrupt button (visible only when generating)
                // Covers Italian (Interrompi), English (Stop), and other variants
                var stopBtn = _page.Locator(_selectors.Current.StopButton).First;

                if (await stopBtn.CountAsync() > 0 && await stopBtn.IsVisibleAsync())
                {
                    isBusy = true;
                }

                // Removed Strategy 2 because the send button is disabled also when the prompt textbox is empty,
                // which causes IsGeminiBusy to be stuck to true when Gemini is idle.
                
                IsGeminiBusy = isBusy;
            }
            catch 
            {
                IsGeminiBusy = false;
            }
        }

        private async Task<bool> IsGeminiBlockedAsync()
        {
            if (_page == null) return false;
            var url = _page.Url;
            if (url.Contains("google.com/sorry") || url.Contains("accounts.google.com/signin")) return true;
            try
            {
                // Controlla il selettore test-id universale di Google (indipendente dalla lingua)
                var signInIndicators = _page.Locator("[data-test-id='sign-in-button']");
                if (await signInIndicators.CountAsync() > 0 && await signInIndicators.First.IsVisibleAsync(new Microsoft.Playwright.LocatorIsVisibleOptions { Timeout = 100 }))
                {
                    return true;
                }

                var captchaFrame = _page.Locator("iframe[src*='recaptcha']");
                return await captchaFrame.CountAsync() > 0 && await captchaFrame.IsVisibleAsync();
            }
            catch
            {
                return false;
            }
        }

        private async Task PollAndSyncAsync()
        {
            if (_page == null) return;

            var elements = _page.Locator(_selectors.Current.MessageElements);
            int count = await elements.CountAsync();

            if (count > _lastProcessedIndex + 1)
            {
                for (int i = _lastProcessedIndex + 1; i < count; i++)
                {
                    var el = elements.Nth(i);
                    var tagName = await el.EvaluateAsync<string>("el => el.tagName.toLowerCase()");

                    if (tagName == "query-content")
                    {
                        var text = (await el.InnerTextAsync() ?? "").Trim();
                        bool isProgrammatic = false;
                        if (_programmaticPromptText != null)
                        {
                            var cleanText = text.Replace("\r", "").Replace("\n", "").Replace(" ", "");
                            var cleanProg = _programmaticPromptText.Replace("\r", "").Replace("\n", "").Replace(" ", "");
                            if (cleanText.Contains(cleanProg) || cleanProg.Contains(cleanText))
                            {
                                isProgrammatic = true;
                                _programmaticPromptText = null;
                            }
                        }

                        if (!isProgrammatic)
                        {
                            OnUserPromptDetected?.Invoke(this, text);
                        }

                        _lastProcessedIndex = i;
                    }
                    else if (tagName == "message-content")
                    {
                        if (i < count - 1)
                        {
                            var text = await GetFormattedMessageContentAsync(el);
                            OnMessageCompleted?.Invoke(this, text);
                            _lastProcessedIndex = i;
                            _lastStreamedText = string.Empty;
                            _stabilityCount = 0;
                        }
                        else
                        {
                            var text = await GetFormattedMessageContentAsync(el);
                            if (string.IsNullOrEmpty(text))
                            {
                                continue;
                            }

                            if (IsGeminiBusy)
                            {
                                // Still generating
                                if (text != _lastStreamedText)
                                {
                                    _lastStreamedText = text;
                                    _stabilityCount = 0;
                                    OnMessageReceivedPartial?.Invoke(this, text);
                                }
                                else
                                {
                                    _stabilityCount++;
                                    // Failsafe: if Gemini is ostensibly busy but text hasn't changed for 15 seconds (30 loops)
                                    if (_stabilityCount >= 30)
                                    {
                                        OnMessageCompleted?.Invoke(this, text);
                                        _lastProcessedIndex = i;
                                        _lastStreamedText = string.Empty;
                                        _stabilityCount = 0;
                                    }
                                }
                            }
                            else
                            {
                                // Gemini is NOT busy, meaning it has finished generating
                                if (text != _lastStreamedText)
                                {
                                    _lastStreamedText = text;
                                    _stabilityCount = 0;
                                    OnMessageReceivedPartial?.Invoke(this, text);
                                }
                                else
                                {
                                    // Give it 3 seconds of stability (6 loops) after IsGeminiBusy becomes false 
                                    // to ensure we don't accidentally complete right at the start of a response due to a race condition or network lag
                                    _stabilityCount++;
                                    if (_stabilityCount >= 6)
                                    {
                                        OnMessageCompleted?.Invoke(this, text);
                                        _lastProcessedIndex = i;
                                        _lastStreamedText = string.Empty;
                                        _stabilityCount = 0;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task<string> GetFormattedMessageContentAsync(ILocator locator)
        {
            var html = await locator.InnerHTMLAsync() ?? "";
            
            var text = "";
            try 
            {
                var config = new ReverseMarkdown.Config
                {
                    GithubFlavored = true,
                    RemoveComments = true,
                    SmartHrefHandling = true,
                    UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.Bypass
                };
                var converter = new ReverseMarkdown.Converter(config);
                text = converter.Convert(html);
                text = System.Net.WebUtility.HtmlDecode(text);
            }
            catch 
            {
                // Fallback sicuro al testo puro se il parser fallisce
                text = await locator.InnerTextAsync() ?? "";
            }

            try
            {
                var codeBlocks = await locator.Locator("pre code").AllAsync();
                foreach (var codeBlock in codeBlocks)
                {
                    var codeText = await codeBlock.InnerTextAsync();
                    var className = await codeBlock.GetAttributeAsync("class") ?? "";

                    bool isTool = className.Contains("language-tool") || 
                                  className.Contains("lang-tool") || 
                                  className.Equals("tool") ||
                                  (codeText.Contains("\"action\"") && (codeText.Trim().StartsWith("{") || codeText.Trim().StartsWith("[")));

                    if (isTool)
                    {
                        if (!text.Contains("```tool"))
                        {
                            text += $"\n\n```tool\n{codeText.Trim()}\n```";
                        }
                    }
                }
            }
            catch { }
            return text.Trim();
        }

        public async Task ResetWatcherIndexAsync()
        {
            if (_page == null) return;
            try
            {
                var elements = _page.Locator(_selectors.Current.MessageElements);
                int count = await elements.CountAsync();
                _lastProcessedIndex = count - 1;
                _lastStreamedText = string.Empty;
                _stabilityCount = 0;
                Debug.WriteLine($"[Watcher] Reset index to {_lastProcessedIndex}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watcher] Reset index error: {ex.Message}");
            }
        }

        // ─── HUMAN SIMULATION HELPERS ─────────────────────────────────────────────

        /// <summary>
        /// Delay randomico tra min e max ms. Simula i tempi di reazione umani.
        /// </summary>
        private static Task HumanDelay(int minMs, int maxMs) =>
            Task.Delay(_rng.Next(minMs, maxMs));

        /// <summary>
        /// Muove il mouse verso il centro dell'elemento con un piccolo offset casuale,
        /// poi esegue il click. Più realistico di un click diretto al centro esatto.
        /// </summary>
        private async Task HumanClickAsync(ILocator locator)
        {
            try
            {
                var box = await locator.BoundingBoxAsync();
                if (box != null && _page != null)
                {
                    // Offset casuale entro l'80% del bounding box per evitare click esatti al pixel
                    float offsetX = (float)(_rng.NextDouble() * box.Width * 0.6 + box.Width * 0.2);
                    float offsetY = (float)(_rng.NextDouble() * box.Height * 0.6 + box.Height * 0.2);
                    await _page.Mouse.MoveAsync(box.X + offsetX, box.Y + offsetY);
                    await HumanDelay(60, 180);
                    await _page.Mouse.ClickAsync(box.X + offsetX, box.Y + offsetY);
                }
                else
                {
                    await locator.ClickAsync();
                }
            }
            catch
            {
                await locator.ClickAsync();
            }
        }

        /// <summary>
        /// Digita una stringa carattere per carattere con delay casuali tra 40–120ms.
        /// Usato solo per messaggi brevi (<= 200 caratteri) per massimizzare il realismo.
        /// Per messaggi lunghi si usa il clipboard per non impattare l'UX.
        /// </summary>
        private async Task HumanTypeAsync(string text)
        {
            if (_page == null) return;
            foreach (char c in text)
            {
                await _page.Keyboard.TypeAsync(c.ToString());
                // Piccola pausa variabile tra un carattere e l'altro (40–120ms)
                await HumanDelay(40, 120);
                // Ogni tanto (5% di probabilità) una pausa più lunga, come chi rilegge
                if (_rng.NextDouble() < 0.05)
                    await HumanDelay(200, 500);
            }
        }

        // ─── SEND PROMPT (con simulazione umana) ─────────────────────────────────

        public async Task SendPromptAsync(string message, string[]? filePaths = null)
        {
            if (_page == null || _page.IsClosed)
            {
                OnError?.Invoke(this, "Il browser è chiuso o in riavvio. Attendi...");
                return;
            }

            try
            {
                _programmaticPromptText = message.Trim();

                // ── Gestione allegati ──────────────────────────────────────────────
                if (filePaths != null && filePaths.Length > 0)
                {
                    try
                    {
                        var addBtn = _page.Locator(
                            "button[aria-label='Carica immagine'], button[aria-label='Upload image'], " +
                            "button[aria-label='Carica file'], button[aria-label='Upload file'], " +
                            "button[aria-label='Carica immagine o file'], button[aria-label='Upload image or file']"
                        ).First;
                        
                        if (await addBtn.IsVisibleAsync())
                        {
                            await HumanDelay(300, 600);
                            await HumanClickAsync(addBtn);
                            await HumanDelay(600, 1000);
                        }
                        
                        try
                        {
                            var leftBtn = _page.Locator($"button:left-of({_selectors.Current.TextArea})").First;
                            if (await leftBtn.IsVisibleAsync())
                            {
                                await HumanDelay(300, 600);
                                await HumanClickAsync(leftBtn);
                                await HumanDelay(600, 1000);
                            }
                        }
                        catch { }

                        var fileChooser = await _page.RunAndWaitForFileChooserAsync(async () =>
                        {
                            var menuItems = _page.Locator(_selectors.Current.MenuItems);
                            if (await menuItems.CountAsync() > 0)
                            {
                                var computerUpload = menuItems.Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("(?i)computer|file|carica|upload") }).First;
                                if (await computerUpload.IsVisibleAsync())
                                    await computerUpload.ClickAsync();
                                else
                                    await menuItems.First.ClickAsync();
                            }
                        });

                        await fileChooser.SetFilesAsync(filePaths);
                        await Task.Delay(5000); 
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error Uploading File: {ex.Message}");
                    }
                }

                // ── Attesa pre-focus (simula il tempo di avvicinamento al mouse) ──
                await HumanDelay(150, 400);

                var textAreaSelector = _selectors.Current.TextArea; 
                await _page.WaitForSelectorAsync(textAreaSelector, new() { Timeout = 15000 });

                // Muovi il mouse verso la textbox prima di cliccarci (simula avvicinamento)
                var textArea = _page.Locator(textAreaSelector).First;
                var box = await textArea.BoundingBoxAsync();
                if (box != null)
                {
                    float ox = (float)(_rng.NextDouble() * box.Width * 0.6 + box.Width * 0.2);
                    float oy = (float)(_rng.NextDouble() * box.Height * 0.6 + box.Height * 0.2);
                    await _page.Mouse.MoveAsync(box.X + ox, box.Y + oy);
                    await HumanDelay(80, 200);
                }

                await _page.FocusAsync(textAreaSelector);
                await HumanDelay(100, 250);

                // Pulisci eventuale testo precedente
                await _page.Keyboard.PressAsync("Control+A");
                await HumanDelay(60, 120);
                await _page.Keyboard.PressAsync("Backspace");
                await HumanDelay(80, 180);

                // ── Strategia di inserimento testo ────────────────────────────────
                // Messaggi brevi (<=200 char): digitazione umana carattere per carattere
                // Messaggi lunghi: clipboard paste (nessun impatto sull'UX)
                bool useTyping = message.Length <= 200;

                if (useTyping)
                {
                    await HumanTypeAsync(message);
                }
                else
                {
                    bool clipboardSuccess = false;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(message);
                            clipboardSuccess = true;
                        }
                        catch { }
                    });

                    if (clipboardSuccess)
                    {
                        await _page.Keyboard.PressAsync("Control+V");
                        await HumanDelay(300, 600);
                    }
                    else
                    {
                        await _page.FillAsync(textAreaSelector, message);
                        await HumanDelay(300, 500);
                    }
                }

                // ── Pausa pre-invio (simula "rileggo prima di mandare") ────────────
                // Tra 400ms e 900ms — impercettibile per l'utente, cruciale per i bot detector
                await HumanDelay(400, 900);

                await _page.FocusAsync(textAreaSelector);
                await HumanDelay(60, 150);

                // Piccolo movimento del mouse verso il tasto Invio/area tastiera
                if (box != null)
                {
                    await _page.Mouse.MoveAsync(
                        box.X + box.Width + _rng.Next(20, 60),
                        box.Y + _rng.Next(-10, 10)
                    );
                    await HumanDelay(80, 200);
                }

                await _page.Keyboard.PressAsync("Enter");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Send Prompt Error: {ex.Message}");
                throw;
            }
        }

        public async Task<string> SendAndAwaitResponseAsync(string message, string[]? filePaths = null)
        {
            var tcs = new TaskCompletionSource<string>();
            
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
                
                var completedTask = tcs.Task;
                var timeoutTask = Task.Delay(120000);
                
                var finishedTask = await Task.WhenAny(completedTask, timeoutTask);
                if (finishedTask == timeoutTask)
                {
                    throw new TimeoutException("Timeout waiting for Gemini response.");
                }
                
                return await completedTask;
            }
            finally
            {
                OnMessageCompleted -= completedHandler;
                OnError -= errorHandler;
            }
        }

        /// <summary>Legge l'ID univoco della chat Gemini dall'URL corrente.</summary>
        public async Task<string> GetCurrentChatIdAsync()
        {
            if (_page == null) return string.Empty;
            try
            {
                var url = _page.Url;
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && segments[0] == "app")
                    return segments[1];
            }
            catch { }
            return string.Empty;
        }

        public async Task NavigateToChatAsync(string geminiId)
        {
            if (_page == null) return;
            _isNavigating = true;
            try
            {
                string url = $"https://gemini.google.com/app/{geminiId}";
                await _page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 8000
                });

                // Controlla l'URL prima di attendere il DOM
                if (_page.Url.Contains("google.com/sorry") || _page.Url.Contains("accounts.google.com/signin")) return;

                // Non facciamo il check sincrono qui perché potremmo non essere loggati
                // Aspetta in parallelo o i messaggi o la pagina di blocco o il redirect alla root
                var tMessages = _page.WaitForSelectorAsync(_selectors.Current.MessageElements, new PageWaitForSelectorOptions { Timeout = 5000 });
                var tBlock = _page.WaitForSelectorAsync("[data-test-id='sign-in-button'], iframe[src*='recaptcha']", new PageWaitForSelectorOptions { Timeout = 5000 });
                var tRedirect = _page.WaitForURLAsync("**/app", new PageWaitForURLOptions { Timeout = 5000 });

                // Evita eccezioni non osservate sui task orfani
                _ = tMessages.ContinueWith(t => t.Exception, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                _ = tBlock.ContinueWith(t => t.Exception, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                _ = tRedirect.ContinueWith(t => t.Exception, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

                try
                {
                    var firstCompleted = await Task.WhenAny(tMessages, tBlock, tRedirect);
                    if (firstCompleted == tBlock)
                    {
                        return; // Non loggato o bloccato
                    }
                    else if (firstCompleted == tRedirect || (!string.IsNullOrEmpty(geminiId) && !_page.Url.Contains(geminiId) && _page.Url.Contains("gemini.google.com/app")))
                    {
                        // Ha reindirizzato alla root (o era già alla root dopo il Goto). 
                        // Aspettiamo in modo affidabile che carichi la pagina target prima di trarre conclusioni (il DOM potrebbe non essere ancora pronto)
                        var secondCompleted = await Task.WhenAny(tMessages, tBlock);
                        if (secondCompleted == tBlock)
                        {
                            return; // Non loggato o bloccato
                        }

                        if (!string.IsNullOrEmpty(geminiId))
                        {
                            // Google ci ha reindirizzato alla root ed eravamo loggati: la chat è stata cancellata
                            OnChatNotFound?.Invoke(this, geminiId);
                            return;
                        }
                    }
                    
                    await firstCompleted; // Propaga eccezioni dal task vincente
                }
                catch { }

                try
                {
                    int currentCount = await _page.Locator(_selectors.Current.MessageElements).CountAsync();
                    _lastProcessedIndex = currentCount - 1;
                }
                catch { }

                // Attendi che il numero di messaggi si stabilizzi per evitare di processare la history come se fossero nuovi messaggi
                int prevCount = -1;
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(300);
                    int currentCount = await _page.Locator(_selectors.Current.MessageElements).CountAsync();
                    if (currentCount > 0 && currentCount == prevCount) break;
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
                _isNavigating = false;
            }
        }

        /// <summary>Avvia una nuova chat su Gemini (naviga alla home dell'app).</summary>
        public async Task NewChatAsync()
        {
            if (_page == null) return;
            _isNavigating = true;
            try
            {
                await _page.GotoAsync("https://gemini.google.com/app", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 8000
                });
                await Task.Delay(800);
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
                _isNavigating = false;
            }
        }

        /// <summary>
        /// Cerca nella sidebar di Gemini la chat con l'ID specificato e ne restituisce il titolo.
        /// Restituisce null se non trovata o se il browser non è aperto.
        /// </summary>
        public async Task<string?> GetChatNameFromGeminiAsync(string geminiId)
        {
            if (_page == null || string.IsNullOrEmpty(geminiId)) return null;
            try
            {
                var candidates = new[]
                {
                    $"a[href*='{geminiId}']",
                    $"[data-conversation-id='{geminiId}']",
                    $"[href='/app/{geminiId}']"
                };

                foreach (var selector in candidates)
                {
                    var el = _page.Locator(selector).First;
                    if (await el.CountAsync() > 0)
                    {
                        var text = (await el.InnerTextAsync())?.Trim();
                        if (!string.IsNullOrEmpty(text))
                            return text;
                    }
                }

                var allLinks = await _page.QuerySelectorAllAsync("a[href]");
                foreach (var link in allLinks)
                {
                    var href = await link.GetAttributeAsync("href") ?? "";
                    if (href.Contains(geminiId))
                    {
                        var text = await link.InnerTextAsync();
                        if (!string.IsNullOrEmpty(text))
                            return text.Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        public async Task CloseAsync()
        {
            StopWatcher();
            if (_browserContext != null)
                await _browserContext.CloseAsync();
            
            _playwright?.Dispose();
        }
    }
}
