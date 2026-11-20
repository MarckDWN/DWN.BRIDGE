using Microsoft.Playwright;
using System.Diagnostics;

namespace AIBridge.Services.WebChat;

/// <summary>
/// Gestisce il ciclo di vita del browser Playwright: installazione Chromium,
/// profilo persistente, stealth, eventi Response/DOMContentLoaded, login manuale.
///
/// GeminiBrowserService non contiene più nulla di Playwright: delega tutto qui (U4).
/// </summary>
public sealed class BrowserHost : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowserContext? _browserContext;
    private IPage? _page;

    private readonly IChatWebEngine _engine;
    private readonly IUiBridge _ui;
    private readonly IBrowserInstaller _installer;

    // ── Profilo ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Cartella profilo persistente. Usa il nome dall'engine (R6: non cambiare mai
    /// "GeminiProfile" sui PC esistenti per non perdere la sessione).
    /// </summary>
    public string UserDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBridge",
            _engine.Descriptor.ProfileFolderName);

    // ── Stato ─────────────────────────────────────────────────────────────────

    /// <summary>True se il browser è avviato e la pagina non è chiusa.</summary>
    public bool IsInitialized => _page != null && !_page.IsClosed;

    /// <summary>Pagina corrente; null se non ancora avviato.</summary>
    public IPage? Page => _page;

    /// <summary>Contesto corrente; null se non ancora avviato.</summary>
    public IBrowserContext? Context => _browserContext;

    // ── Eventi ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Emesso se l'installazione di Chromium supera 500 ms (primo avvio su PC nuovo).
    /// </summary>
    public event EventHandler<EventArgs>? InstallingBrowser;

    /// <summary>Emesso quando si rileva un crash SPA (batchexecute status 0).</summary>
    public event EventHandler<EventArgs>? SpaCrashDetected;

    // ── Costruttore ───────────────────────────────────────────────────────────

    public BrowserHost(IChatWebEngine engine, IUiBridge ui, IBrowserInstaller? installer = null)
    {
        _engine    = engine;
        _ui        = ui;
        _installer = installer ?? new DefaultBrowserInstaller();
    }

    // ── Avvio ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Avvia il browser. Se già aperto porta la finestra in primo piano.
    /// </summary>
    public async Task EnsureStartedAsync(bool headless = false)
    {
        if (IsInitialized)
        {
            try { await _page!.BringToFrontAsync(); }
            catch { }
            return;
        }

        // 1. Installa Chromium (no-op se già presente); emette evento se impiega > 500 ms
        var installTask = Task.Run(() => _installer.InstallAsync());
        if (await Task.WhenAny(installTask, Task.Delay(500)) != installTask)
            InstallingBrowser?.Invoke(this, EventArgs.Empty);
        await installTask;

        // 2. Crea playwright e contesto persistente
        _playwright = await Playwright.CreateAsync();

        var args = new List<string>(GetStealthArgs());
        if (!headless)
        {
            args.Add("--window-size=1024,768");
            args.Add("--window-position=50,50");
        }

        var options = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless        = headless,
            Args            = args,
            IgnoreDefaultArgs = new[] { "--enable-automation", "--no-sandbox" },
            ViewportSize    = new ViewportSize { Width = 1024, Height = 768 },
            UserAgent       = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36",
            Locale          = System.Globalization.CultureInfo.CurrentUICulture.Name
        };

        PatchBrowserPreferences();

        _browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(UserDataDir, options);

        // 3. Stealth init script (pre-navigazione)
        await _browserContext.AddInitScriptAsync(GetStealthInitScript());

        // 4. Pagina principale
        _page = _browserContext.Pages.Count > 0
            ? _browserContext.Pages[0]
            : await _browserContext.NewPageAsync();

        // 5. Hook Response → SPA crash detection (delegato all'engine)
        _page.Response += async (_, response) =>
        {
            if (_engine.IsSpaCrash(response))
            {
                Debug.WriteLine("[BrowserHost] SPA crash detected, reloading...");
                SpaCrashDetected?.Invoke(this, EventArgs.Empty);
                try { await _page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle }); }
                catch { }
            }
        };

        // 6. Hook DOMContentLoaded → PreparePageAsync (inietta CSS anti-popup)
        _page.DOMContentLoaded += async (_, _) =>
        {
            try { await _engine.PreparePageAsync(_page); }
            catch { }
        };

        // 7. Porta la finestra principale WPF in primo piano (Chromium va in secondo)
        _ui.BringMainWindowToFront();
    }

    // ── Chiusura ──────────────────────────────────────────────────────────────

    /// <summary>Chiude browser e rilascia Playwright. Idempotente.</summary>
    public async Task CloseAsync()
    {
        if (_browserContext != null)
        {
            try { await _browserContext.CloseAsync(); } catch { }
            _browserContext = null;
        }
        if (_playwright != null)
        {
            try { _playwright.Dispose(); } catch { }
            _playwright = null;
        }
        _page = null;
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    // ── Login manuale ─────────────────────────────────────────────────────────

    /// <summary>
    /// Esegue il flusso di login manuale dell'engine (es. WebView2 per Google).
    /// Chiude il browser prima per sbloccare il profilo, poi lo riavvia con i cookie.
    /// </summary>
    public async Task<IReadOnlyList<Cookie>?> RunLoginFlowAsync()
    {
        var loginFlow = _engine.LoginFlow;
        if (loginFlow == null) return null;

        await CloseAsync(); // Sblocca la cartella profilo

        try
        {
            return await loginFlow.RunInteractiveLoginAsync(UserDataDir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BrowserHost] Login flow error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Inietta i cookie restituiti dal login flow nel contesto corrente.
    /// Chiamare dopo EnsureStartedAsync.
    /// </summary>
    public async Task InjectCookiesAsync(IReadOnlyList<Cookie> cookies)
    {
        if (_browserContext == null || cookies.Count == 0) return;
        try { await _browserContext.AddCookiesAsync(cookies); }
        catch { }
    }

    // ── Stealth ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Args Chromium che rimuovono le tracce di automazione più comuni.
    /// Snapshot-testati: cambiano solo volontariamente.
    /// </summary>
    public string[] GetStealthArgs()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return new[]
        {
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-infobars",
            "--disable-notifications",
            $"--lang={culture.Name},{culture.TwoLetterISOLanguageName},en-US,en",
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding",
        };
    }

    /// <summary>
    /// Script JS iniettato su ogni pagina prima del codice della pagina.
    /// Snapshot-testato: cambia solo volontariamente.
    /// </summary>
    public string GetStealthInitScript()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return $@"
// 1. Rimuovi navigator.webdriver (la traccia più ovvia di Playwright/Selenium)
Object.defineProperty(navigator, 'webdriver', {{ get: () => undefined }});

// 2. Ripristina navigator.plugins (browser reali ne hanno molti, Playwright zero)
Object.defineProperty(navigator, 'plugins', {{
    get: () => [1, 2, 3, 4, 5],
}});

// 3. Ripristina navigator.languages (sincronizzate con la lingua dell'app WPF)
Object.defineProperty(navigator, 'languages', {{
    get: () => ['{culture.Name}', '{culture.TwoLetterISOLanguageName}', 'en-US', 'en'],
}});

// 4. Nascondi la proprietà chrome usata da alcuni rilevatori
window.chrome = {{ runtime: {{}} }};

// 5. Evita il rilevamento tramite permission API
const originalQuery = window.navigator.permissions.query;
window.navigator.permissions.query = (parameters) =>
    parameters.name === 'notifications'
        ? Promise.resolve({{ state: Notification.permission }})
        : originalQuery(parameters);
";
    }

    // ── Preferenze Chromium ───────────────────────────────────────────────────

    /// <summary>
    /// Patcha il file Preferences di Chromium per forzare la lingua corretta
    /// (evita che il browser mostri la UI in inglese la prima volta).
    /// </summary>
    public void PatchBrowserPreferences()
    {
        try
        {
            var prefsPath = Path.Combine(UserDataDir, "Default", "Preferences");
            if (!File.Exists(prefsPath)) return;

            var json = File.ReadAllText(prefsPath);
            var jObj = System.Text.Json.Nodes.JsonNode.Parse(json)
                       as System.Text.Json.Nodes.JsonObject;
            if (jObj == null) return;

            var intl = jObj["intl"] as System.Text.Json.Nodes.JsonObject
                       ?? new System.Text.Json.Nodes.JsonObject();
            jObj["intl"] = intl;

            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            var langStr = $"{culture.Name},{culture.TwoLetterISOLanguageName},en-US,en";
            intl["accept_languages"] = langStr;
            intl["selected_languages"] = langStr;

            File.WriteAllText(prefsPath, jObj.ToJsonString());
        }
        catch { }
    }
}

// ── IBrowserInstaller default ────────────────────────────────────────────────

/// <summary>
/// Implementazione default: chiama playwright install chromium.
/// Sostituibile con un fake nei test.
/// </summary>
internal sealed class DefaultBrowserInstaller : IBrowserInstaller
{
    public Task InstallAsync()
    {
        Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        return Task.CompletedTask;
    }
}

