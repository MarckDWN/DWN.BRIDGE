namespace AIBridge.Shared.Models;

/// <summary>Auth request per il login Guest (POST /api/auth/anonymous).</summary>
public record GuestLoginRequest
{
    /// <summary>
    /// Hardware ID: SHA-256(UserSID + MotherboardSerial).
    /// Non è un dato personale identificativo (nessun nome, email, IP).
    /// </summary>
    public string Hwid { get; init; } = string.Empty;
}

/// <summary>Auth response per il login Guest e OAuth.</summary>
public record AuthResponse
{
    public string Token { get; init; } = string.Empty;
    public long UserId { get; init; }
    public bool IsNewUser { get; init; }

    /// <summary>"guest" | "oauth"</summary>
    public string AuthMode { get; init; } = "guest";

    /// <summary>"community" | "pro" | "business" | "enterprise"</summary>
    public string Tier { get; init; } = "community";
}

/// <summary>Request per l'account linking Guest → OAuth (POST /api/auth/link).</summary>
public record AccountLinkRequest
{
    /// <summary>JWT OAuth ottenuto dall'IdP (via /api/auth/oauth/callback).</summary>
    public string OAuthToken { get; init; } = string.Empty;
}

/// <summary>Request per l'avvio del flusso OAuth PKCE (POST /api/auth/oauth/start).</summary>
public record OAuthStartRequest
{
    /// <summary>"google" | "github" | "microsoft"</summary>
    public string Provider { get; init; } = string.Empty;
}

/// <summary>Response per l'avvio del flusso OAuth PKCE.</summary>
public record OAuthStartResponse
{
    /// <summary>URL di autorizzazione dell'IdP a cui il client deve navigare.</summary>
    public string AuthorizationUrl { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}

/// <summary>Request per scambiare il codice OAuth con il JWT AIBridge.</summary>
public record OAuthCallbackRequest
{
    public string Code { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
}

/// <summary>Heartbeat response con JWT rinnovato.</summary>
public record HeartbeatResponse
{
    public string Token { get; init; } = string.Empty;
}

public record MagicLinkStartRequest
{
    public string Email { get; init; } = string.Empty;
    public string Language { get; init; } = "en-US";
}

public record MagicLinkStartResponse
{
    public string SessionId { get; init; } = string.Empty;
}

public record MagicLinkVerifyRequest
{
    public string Email { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

public record MagicLinkStatusResponse
{
    public string Status { get; init; } = string.Empty; // "pending" | "verified" | "expired"
    public string Token { get; init; } = string.Empty;
    public long UserId { get; init; }
}
