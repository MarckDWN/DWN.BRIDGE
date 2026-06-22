using System.IO;
using System.Text.Json;
using System.Windows;
using AIBridge.Services;
using Unclassified.TxLib;

namespace AIBridge;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static AIBridge.Shared.Interfaces.IToolDictionaryService? ToolDictionary { get; private set; }

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
        LogFatalError(e.Exception);
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
    }

    protected override void OnStartup(StartupEventArgs e)
    {
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

        var config    = ReadLocalConfig();
        var baseUrl   = config.GetValueOrDefault("CloudBrain:BaseUrl", "https://localhost:7174");
        var enabled   = config.GetValueOrDefault("CloudBrain:Enabled", "true") == "true";
        var hwid      = new HwidService().ComputeHwid();

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

    /// <summary>Legge appsettings.json piatto in un dizionario semplice.</summary>
    private static Dictionary<string, string> ReadLocalConfig()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return result;

            using var doc  = JsonDocument.Parse(File.ReadAllText(path));
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
