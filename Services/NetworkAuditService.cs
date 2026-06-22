using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace AIBridge.Services;

public record AuditEntry
{
    public DateTime Timestamp { get; init; }
    public string Direction { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public bool IsEncrypted { get; init; }
}

public interface INetworkAuditService
{
    ObservableCollection<AuditEntry> Entries { get; }
    void LogRequest(string endpoint, int bytes);
    void LogResponse(string endpoint, int bytes, bool isEncrypted);
    void Clear();
}

public class NetworkAuditService : INetworkAuditService
{
    public ObservableCollection<AuditEntry> Entries { get; } = new ObservableCollection<AuditEntry>();

    public void LogRequest(string endpoint, int bytes)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Insert(0, new AuditEntry
            {
                Timestamp = DateTime.Now,
                Direction = "-> Server",
                Endpoint = endpoint,
                Summary = $"Inviati {bytes} bytes (Metadati/Schema/Richiesta)",
                IsEncrypted = false
            });
        });
    }

    public void LogResponse(string endpoint, int bytes, bool isEncrypted)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Insert(0, new AuditEntry
            {
                Timestamp = DateTime.Now,
                Direction = "<- Server",
                Endpoint = endpoint,
                Summary = $"Ricevuti {bytes} bytes (Payload Orchestratore)",
                IsEncrypted = isEncrypted
            });
        });
    }

    public void Clear()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Clear();
        });
    }
}
