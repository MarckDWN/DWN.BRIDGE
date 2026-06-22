namespace AIBridge.Shared.Models;

/// <summary>
/// Risposta cifrata AES-GCM inviata dal server al client.
/// Il payload (Pld) contiene un <see cref="DecryptedCommand"/> serializzato in JSON e cifrato.
/// IV, Tag e Payload sono separati (mai concatenati) per sicurezza.
/// </summary>
public record EncryptedCommandResponse
{
    /// <summary>Session ID di correlazione.</summary>
    public string Sid { get; init; } = string.Empty;

    /// <summary>Initialization Vector (12 byte), Base64-encoded.</summary>
    public string Iv { get; init; } = string.Empty;

    /// <summary>GCM Authentication Tag (16 byte), Base64-encoded.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Ciphertext AES-GCM, Base64-encoded.</summary>
    public string Pld { get; init; } = string.Empty;
}
