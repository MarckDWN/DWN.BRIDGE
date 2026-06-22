using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using AIBridge.Services.Extensions;

namespace AIBridge.Services;

/// <summary>
/// ServiceLocator statico per i servizi cloud.
/// Inizializzato in <see cref="App.OnStartup"/> prima della creazione di <c>MainViewModel</c>.
/// Usato da <c>MainViewModel</c> che non ha un DI container.
/// </summary>
public static class CloudServiceLocator
{
    private static ICloudBrainClient? _client;
    private static ICloudModeDetector? _modeDetector;
    private static INetworkAuditService _audit = new NetworkAuditService();
    private static ISessionAdapter? _sessionAdapter;
    private static AIBridge.Shared.Interfaces.IPromptOrchestrator? _orchestrator;

    public static ICloudBrainClient? Client       => _client;
    public static ICloudModeDetector? ModeDetector => _modeDetector;
    public static INetworkAuditService Audit => _audit;
    public static ISessionAdapter Session => _sessionAdapter ??= InitSessionAdapter();
    
    // Ritorna l'Orchestratore Cloud se connesso, altrimenti fa fallback a quello Locale
    public static AIBridge.Shared.Interfaces.IPromptOrchestrator Orchestrator 
    {
        get
        {
            if (_orchestrator != null) return _orchestrator;
            
            try
            {
                string extensionsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "extensions");
                string dllPath = System.IO.Path.Combine(extensionsPath, "AIBridge.Core.dll");
                
                if (System.IO.File.Exists(dllPath))
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
                    var type = assembly.GetType("AIBridge.Core.LocalPromptOrchestrator");
                    if (type != null)
                    {
                        return (AIBridge.Shared.Interfaces.IPromptOrchestrator)Activator.CreateInstance(type)!;
                    }
                }
            }
            catch { }
            
            throw new InvalidOperationException("Servizio Cloud non inizializzato e componente Standalone (AIBridge.Core) mancante nella cartella extensions.");
        }
    }

    /// <summary>
    /// Inizializza il client con la configurazione locale.
    /// Chiamato una sola volta all'avvio dell'app (App.xaml.cs).
    /// </summary>
    public static async Task<bool> InitializeAsync(string baseUrl, string hwid)
    {
        try
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout     = TimeSpan.FromSeconds(30)
            };

            // In sviluppo: accetta il certificato self-signed
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            var secureClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout     = TimeSpan.FromSeconds(30)
            };

            _client = new CloudBrainClient(
                secureClient,
                NullLogger<CloudBrainClient>.Instance);

            // Inizializza il CloudPromptOrchestrator usando lo stesso httpClient
            _orchestrator = new CloudPromptOrchestrator(secureClient);

            // Togliamo il login automatico da qui. Sarà gestito dalla Splash Screen.
            _modeDetector = new CloudModeDetector(_client);
            await _modeDetector.RefreshModeAsync();
            return true;
        }
        catch
        {
            _client      = null;
            _modeDetector = null;
            _orchestrator = null;
            return false;
        }
    }

    /// <summary>Reimposta allo stato non-connesso (utile per i test).</summary>
    public static void Reset()
    {
        _client      = null;
        _modeDetector = null;
        _sessionAdapter = null;
    }

    private static ISessionAdapter InitSessionAdapter()
    {
        // 1. Priorità massima: Estensione esterna caricata dinamicamente
        var extAdapter = ExtensionHost.TryLoadSessionAdapter();
        if (extAdapter != null)
            return extAdapter;

        // 2. Priorità media: Server Cloud (se connesso)
        if (_client?.IsConnected == true)
            return new ServerSessionAdapter(_client);

        // 3. Fallback: In-memory volatile
        return new NullSessionAdapter();
    }
}
