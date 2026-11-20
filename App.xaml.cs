using AIBridge.Services;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Unclassified.TxLib;

namespace AIBridge;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static AIBridge.Shared.Interfaces.IToolDictionaryService? ToolDictionary { get; private set; }

    // Timestamp di avvio — usato per calcolare AppUptime al momento del crash
    private static readonly DateTimeOffset _appStartedAt = DateTimeOffset.UtcNow;

    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatalError(e.Exception);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject as Exception is Exception ex)
            LogFatalError(ex);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        // Logquietly instead of showing a fatal error, because MdXaml often throws HttpRequestException here for broken image links
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "AIBridge_Background_Errors.txt");
            File.AppendAllText(logPath, $"[{DateTime.Now}] {e.Exception}\n\n");
        }
        catch { }
    }

    private void LogFatalError(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "AIBridge_Crash.txt");
            File.AppendAllText(logPath, $"[{DateTime.Now}] {ex}\n\n");
            AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Critical error on startup.\nA log file was created: {0}\n\nDetails: {1}", logPath, ex.Message), Unclassified.TxLib.Tx.T("Fatal Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }

        // Invio asincrono al server (best-effort, non blocca il crash dialog)
        _ = Task.Run(() => TrySendCrashReportAsync(ex));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args != null && Array.Exists(e.Args, a => a.Equals("--run-acceptance-test", StringComparison.OrdinalIgnoreCase)))
        {
            var (allPassed, passedCount, totalCount, log) = AIBridge.AgentCore.FileNatureClassifierTests.RunAcceptanceTest();
            Console.WriteLine("=== ACCEPTANCE TEST RESULTS ===");
            Console.WriteLine($"Passed: {passedCount} / {totalCount}");
            Console.WriteLine($"All Passed: {allPassed}");
            Console.WriteLine();
            foreach (var line in log)
            {
                Console.WriteLine(line);
            }
            Environment.Exit(allPassed ? 0 : 1);
            return;
        }
        // NOTE: base.OnStartup(e) fires the Startup event which calls Application_Startup.
        // Application_Startup immediately shows the splash screen.
        // So we MUST initialize Tx BEFORE calling base.OnStartup(e).

        // Initialize TxLib localization — use absolute path so it works from any working directory
        string txdPath = System.IO.Path.Combine(AppContext.BaseDirectory, "localization", "languages.txd");

        try
        {
            Tx.UseFileSystemWatcher = false;
            Tx.LoadFromXmlFile(txdPath);
        }
        catch (Exception ex)
        {
            AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Missing or corrupted language file:\n{0}\n\n{1}", txdPath, ex.Message), Tx.T("Initialization Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            // Proseguiamo comunque, TxLib farà fallback sulle chiavi.
        }

        // Pre-apply the saved language so Tx.T() already works when splash screen opens
        try
        {
            var settings = new Services.SettingsService().Load();
            string langCode = string.IsNullOrEmpty(settings.Language) ? "en-US" : settings.Language;
            var culture = System.Globalization.CultureInfo.GetCultureInfo(langCode);
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            Tx.SetCulture(langCode);
        }
        catch
        {
            // Fallback: leave Tx with default (key-as-fallback) behaviour
        }

        // Now fire Application_Startup → shows splash screen → Tx is already ready
        base.OnStartup(e);
    }


    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Prevents the application from closing when the splash screen (the first window) closes
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Caricamento Dinamico del Modulo Proprietario (ToolCore)
        try
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "extensions", "AIBridge.ToolCore.dll");
            if (File.Exists(assemblyPath))
            {
                var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
                var type = assembly.GetType("AIBridge.ToolCore.ToolDictionaryService");
                if (type != null)
                {
                    ToolDictionary = (AIBridge.Shared.Interfaces.IToolDictionaryService?)Activator.CreateInstance(type);
                }
                else
                {
                    System.Windows.MessageBox.Show("ToolDictionaryService type not found in assembly.");
                }
            }
            else
            {
                System.Windows.MessageBox.Show($"File not found: {assemblyPath}");
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error loading ToolCore: {ex.Message}");
        }

        var config = ReadLocalConfig();
        var baseUrl = config.GetValueOrDefault("CloudBrain:BaseUrl", "https://dwnbridge.org");
        var enabled = config.GetValueOrDefault("CloudBrain:Enabled", "true") == "true";
        var hwid = new HwidService().ComputeHwid();

        if (enabled)
        {
            try { await CloudServiceLocator.InitializeAsync(baseUrl, hwid); } catch { }
        }

        var splash = new Dialogs.SplashScreenDialog(hwid);
        if (splash.ShowDialog() == true)
        {
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            mainWindow.Topmost = true;
            mainWindow.Topmost = false;
            mainWindow.Activate();
            mainWindow.Focus();
        }
        else
        {
            this.Shutdown();
        }
    }

    /// <summary>
    /// Tenta di inviare un crash report automatico al server AIBridge.
    /// Completamente asincrono e fire-and-forget: non blocca il thread UI e non solleva eccezioni.
    /// Verificato il flag SendCrashReports prima di inviare qualsiasi dato.
    /// Funziona anche quando CloudServiceLocator.Client è null (crash prima del login).
    /// </summary>
    private static async Task TrySendCrashReportAsync(Exception ex)
    {
        try
        {
            // Verifica il consenso dell'utente
            var settings = new Services.SettingsService().Load();
            if (!settings.SendCrashReports) return;

            // Legge la URL del server dalla configurazione locale
            var config = ReadLocalConfig();
            var baseUrl = config.GetValueOrDefault("CloudBrain:BaseUrl", "https://dwnbridge.org");

            // Determina la fase del crash in base all'uptime
            var uptimeSeconds = (int)(DateTimeOffset.UtcNow - _appStartedAt).TotalSeconds;
            var crashPhase = uptimeSeconds < 10 ? "Startup" : "Runtime";

            // Calcola lo stack trace hash (SHA-256 dei primi 500 caratteri)
            var stackTrace = ex.ToString();
            var hashInput  = stackTrace[..Math.Min(500, stackTrace.Length)];
            var hashBytes  = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
            var hash       = Convert.ToHexString(hashBytes).ToLowerInvariant();

            // Raccoglie HWID e info OS
            var hwid   = new HwidService().ComputeHwid();
            var osInfo = $"{Environment.OSVersion} {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}";
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                              .GetName().Version?.ToString() ?? "unknown";

            var payload = new AIBridge.Shared.Models.CrashReportRequest
            {
                Hwid             = hwid,
                ClientVersion    = version,
                OsInfo           = osInfo,
                CrashPhase       = crashPhase,
                AppUptimeSeconds = uptimeSeconds,
                ExceptionType    = ex.GetType().FullName,
                ExceptionMessage = ex.Message[..Math.Min(1000, ex.Message.Length)],
                StackTrace       = stackTrace,
                StackTraceHash   = hash
            };

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };

            // Aggiunge JWT se disponibile — consente al server di associare UserId al crash
            var jwtToken = CloudServiceLocator.Client?.GetCurrentToken();
            if (!string.IsNullOrEmpty(jwtToken))
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);

            var json    = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await http.PostAsync("/api/v1/telemetry/crash", content);
        }
        catch
        {
            // Silenzioso: un crash report non può causare un secondo errore
        }
    }

    /// <summary>Legge appsettings.json piatto in un dizionario semplice.</summary>
    private static Dictionary<string, string> ReadLocalConfig()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return result;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            FlattenJson(doc.RootElement, "", result);
        }
        catch { }
        return result;
    }

    private static void FlattenJson(JsonElement el, string prefix, Dictionary<string, string> dict)
    {
        foreach (var prop in el.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}:{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object)
                FlattenJson(prop.Value, key, dict);
            else
                dict[key] = prop.Value.ToString();
        }
    }
}
