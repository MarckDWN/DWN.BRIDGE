namespace AIBridge.Services;

/// <summary>
/// Modalità operativa dell'applicazione.
/// Determina quali funzionalità cloud sono disponibili per la sessione corrente.
/// </summary>
public enum AppMode
{
    /// <summary>Nessun server disponibile o nessun JWT. L'app opera esclusivamente in locale.</summary>
    Local,

    /// <summary>Connesso al Cloud Brain con tier community/free.</summary>
    CloudFree,

    /// <summary>Connesso al Cloud Brain con tier pro/business/enterprise.</summary>
    CloudPro
}

public interface ICloudModeDetector
{
    AppMode CurrentMode { get; }
    Task<AppMode> RefreshModeAsync();
}

public sealed class CloudModeDetector : ICloudModeDetector
{
    private readonly ICloudBrainClient _client;

    public AppMode CurrentMode { get; private set; } = AppMode.Local;

    public CloudModeDetector(ICloudBrainClient client)
    {
        _client = client;
    }

    public async Task<AppMode> RefreshModeAsync()
    {
        if (!_client.IsConnected)
        {
            CurrentMode = AppMode.Local;
            return CurrentMode;
        }

        var heartbeatOk = await _client.HeartbeatAsync();
        if (!heartbeatOk)
        {
            CurrentMode = AppMode.Local;
            return CurrentMode;
        }

        CurrentMode = _client.CurrentTier is "pro" or "business" or "enterprise"
            ? AppMode.CloudPro
            : AppMode.CloudFree;

        return CurrentMode;
    }
}
