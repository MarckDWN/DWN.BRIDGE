using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIBridge.Shared.Models;
using Microsoft.Extensions.Logging;

namespace AIBridge.Services;

/// <summary>
/// Implementazione di <see cref="ICloudBrainClient"/> basata su HttpClient.
/// Il JWT è mantenuto SOLO in memoria (_jwtToken). Non viene mai scritto su disco.
/// In caso di errore di rete, l'app opera in modalità locale (IsConnected = false).
/// </summary>
public sealed class CloudBrainClient : ICloudBrainClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CloudBrainClient> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // JWT in-memory — il token grezzo NON viene mai loggato o inviato a terzi
    private string? _jwtToken;

    // Percorso file cifrato DPAPI (solo sul disco locale dell'utente Windows corrente)
    private static readonly string _tokenCachePath = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "AIBridge", ".auth_cache");

    public bool IsConnected => _jwtToken is not null;

    /// <summary>Salva il JWT cifrato con DPAPI sul disco locale.</summary>
    private static void SaveToken(string token)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_tokenCachePath)!);
            var encrypted = DpapiHelper.Protect(token);
            System.IO.File.WriteAllText(_tokenCachePath, encrypted, System.Text.Encoding.UTF8);
        }
        catch { /* silenzioso */ }
    }

    /// <summary>Carica e decifra il JWT dal disco. Restituisce null se assente o corrotto.</summary>
    private static string? LoadToken()
    {
        try
        {
            if (!System.IO.File.Exists(_tokenCachePath)) return null;
            var encrypted = System.IO.File.ReadAllText(_tokenCachePath, System.Text.Encoding.UTF8);
            var plain = DpapiHelper.Unprotect(encrypted);
            return string.IsNullOrWhiteSpace(plain) ? null : plain;
        }
        catch { return null; }
    }

    /// <summary>Elimina il token salvato (logout).</summary>
    public static void ClearSavedToken()
    {
        try { if (System.IO.File.Exists(_tokenCachePath)) System.IO.File.Delete(_tokenCachePath); }
        catch { }
    }

    public string CurrentTier
    {
        get
        {
            if (_jwtToken is null) return "none";
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_jwtToken);
                return jwt.Claims.FirstOrDefault(c => c.Type == "tier")?.Value ?? "community";
            }
            catch { return "community"; }
        }
    }

    public string AuthMode
    {
        get
        {
            if (_jwtToken is null) return "local";
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_jwtToken);
                return jwt.Claims.FirstOrDefault(c => c.Type == "auth_mode")?.Value ?? "guest";
            }
            catch { return "guest"; }
        }
    }

    public string DisplayName
    {
        get
        {
            if (_jwtToken is null) return "";
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_jwtToken);
                return jwt.Claims.FirstOrDefault(c => c.Type == "display_name")?.Value ?? "";
            }
            catch { return ""; }
        }
    }

    public CloudBrainClient(HttpClient http, ILogger<CloudBrainClient> logger)
    {
        _http   = http;
        _logger = logger;

        string origin = "LocalBuild";
        if (Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed")?.ToLower() == "true")
        {
            origin = $"ClickOnce: {Environment.GetEnvironmentVariable("ClickOnce_ActivationUri") ?? "Unknown"}";
        }
        else if (AppDomain.CurrentDomain.BaseDirectory.Contains(@"\Apps\2.0\"))
        {
            origin = "ClickOnce: UnknownURI";
        }
        _http.DefaultRequestHeaders.Add("X-App-Origin", origin);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public bool HasValidSavedToken()
    {
        var stored = LoadToken();
        if (stored == null) return false;
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(stored);
            
            // Non consideriamo valido un token salvato se è solo un Guest (vogliamo forzare l'utente a scegliere di nuovo)
            var authMode = jwt.Claims.FirstOrDefault(c => c.Type == "auth_mode")?.Value;
            if (authMode == "guest") return false;

            return jwt.ValidTo > DateTime.UtcNow.AddMinutes(1);
        }
        catch { return false; }
    }

    public bool AutoLogin()
    {
        var stored = LoadToken();
        if (stored != null)
        {
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(stored);
                if (jwt.ValidTo > DateTime.UtcNow.AddMinutes(1))
                {
                    _jwtToken = stored;
                    SetAuthHeader();
                    _logger.LogInformation("Token ripristinato dal cache locale. Auth: {Mode}", AuthMode);
                    
                    // Notifica il server per aggiornare le statistiche (fire-and-forget)
                    _ = _http.PostAsync("api/auth/ping", null);

                    return true;
                }
                else
                {
                    ClearSavedToken();
                }
            }
            catch { ClearSavedToken(); }
        }
        return false;
    }

    // Applica e salva un token JWT fornito dall'esterno (es. MagicLink o OAuth dal login)
    public void ApplyToken(string token)
    {
        _jwtToken = token;
        SetAuthHeader();
        SaveToken(_jwtToken);
        _logger.LogInformation("Token applicato manualmente (Auth: {Mode})", AuthMode);
    }

    public async Task<bool> LoginGuestAsync(string hwid)

    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/auth/anonymous",
                new GuestLoginRequest { Hwid = hwid },
                _json);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Login Guest fallito: {Status}", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_json);
            if (result?.Token is null) return false;

            _jwtToken = result.Token;
            SetAuthHeader();
            SaveToken(_jwtToken);  // ← persiste il token Guest cifrato
            _logger.LogInformation("Login Guest OK. Tier: {Tier}, IsNew: {IsNew}", result.Tier, result.IsNewUser);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Server non raggiungibile. Modalità locale attivata.");
            return false;
        }
    }

    public async Task<bool> LogoutAsync(string hwid)
    {
        _jwtToken = null;
        _http.DefaultRequestHeaders.Authorization = null;
        ClearSavedToken();
        _logger.LogInformation("Logout effettuato, ritorno a Guest.");
        return await LoginGuestAsync(hwid);
    }

    public async Task<OAuthStartResponse?> StartOAuthAsync(string provider)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/oauth/start", new OAuthStartRequest { Provider = provider }, _json);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<OAuthStartResponse>(_json);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(string? Token, string? Error)> OAuthCallbackAsync(string provider, string code, string state)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/oauth/callback", new OAuthCallbackRequest 
            { 
                Provider = provider, 
                Code = code, 
                State = state 
            }, _json);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_json);
                return (result?.Token, null);
            }
            
            var errStr = await response.Content.ReadAsStringAsync();
            return (null, $"HTTP {response.StatusCode}: {errStr}");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<MagicLinkStartResponse?> StartMagicLinkAsync(string email)
    {
        try
        {
            var lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
            var response = await _http.PostAsJsonAsync("api/auth/magiclink/start", new MagicLinkStartRequest { Email = email, Language = lang }, _json);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<MagicLinkStartResponse>(_json);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<MagicLinkStatusResponse?> PollMagicLinkStatusAsync(string sessionId)
    {
        try
        {
            return await _http.GetFromJsonAsync<MagicLinkStatusResponse>($"api/auth/magiclink/status?sid={sessionId}", _json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> LinkAccountAsync(string oauthToken)
    {
        if (!IsConnected) return false;
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/link", new AccountLinkRequest { OAuthToken = oauthToken }, _json);
            if (response.IsSuccessStatusCode)
            {
                // Switcha al token OAuth e lo persiste sul disco (sovrascrive il Guest)
                _jwtToken = oauthToken;
                SetAuthHeader();
                SaveToken(_jwtToken);  // ← persiste il token OAuth cifrato
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HeartbeatAsync()
    {
        if (!IsConnected) return false;
        try
        {
            var response = await _http.PostAsync("api/heartbeat", null);
            if (!response.IsSuccessStatusCode) return false;

            var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(_json);
            if (result?.Token is null) return false;

            _jwtToken = result.Token;
            SetAuthHeader();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat fallito.");
            _jwtToken = null; // Invalida la connessione
            return false;
        }
    }

    public async Task<DomSelectors?> GetSelectorsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<DomSelectors>("api/selectors", _json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetSelectors fallito — uso selectors locali.");
            return null;
        }
    }

    public async Task<DecryptedCommand?> GetCommandAsync(ContextRequest request)
    {
        if (!IsConnected) return null;
        try
        {
            var reqBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, _json);
            CloudServiceLocator.Audit.LogRequest("/api/orchestrate", reqBytes.Length);

            var response = await _http.PostAsJsonAsync("api/orchestrate", request, _json);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetCommandAsync fallito: {Status}", response.StatusCode);
                return null;
            }

            var resBytes = await response.Content.ReadAsByteArrayAsync();
            var encrypted = System.Text.Json.JsonSerializer.Deserialize<EncryptedCommandResponse>(resBytes, _json);
            
            CloudServiceLocator.Audit.LogResponse("/api/orchestrate", resBytes.Length, true);

            if (encrypted is null) return null;

            // Deriva la session key dal JWT corrente (stessa logica del server)
            var crypto = new CryptoClientService();
            var sessionKey = crypto.DeriveSessionKey(_jwtToken!);
            return crypto.Decrypt<DecryptedCommand>(encrypted.Iv, encrypted.Tag, encrypted.Pld, sessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetCommandAsync fallito — fallback locale.");
            return null;
        }
    }



    public async Task<List<ChatSessionDto>> GetSessionsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ChatSessionDto>>(
                "api/v1/chat/sessions", _json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetSessions fallito.");
            return [];
        }
    }

    public async Task<ChatSessionDto?> CreateSessionAsync(string agentType, string geminiChatId = "",
                                                          string description = "", string workspacePath = "")
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/v1/chat/sessions",
                new CreateSessionRequest
                {
                    AgentType     = agentType,
                    GeminiChatId  = geminiChatId,
                    Description   = description,
                    WorkspacePath = workspacePath
                },
                _json);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ChatSessionDto>(_json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CreateSession fallito.");
            return null;
        }
    }

    public async Task UpdateSessionAsync(Guid sessionId, string? description = null,
                                         string? workspacePath = null, string? geminiChatId = null, string? agentType = null)
    {
        try
        {
            var req = new UpdateSessionRequest
            {
                Description   = description,
                WorkspacePath = workspacePath,
                GeminiChatId  = geminiChatId,
                AgentType     = agentType
            };
            await _http.PatchAsJsonAsync($"api/v1/chat/sessions/{sessionId}", req, _json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UpdateSession fallito per sessione {Id}.", sessionId);
        }
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync(Guid sessionId)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ChatMessageDto>>(
                $"api/v1/chat/sessions/{sessionId}/messages", _json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetMessages fallito per sessione {Id}.", sessionId);
            return [];
        }
    }

    public async Task SaveMessageAsync(Guid sessionId, string role, string content)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/v1/chat/messages",
                new SaveMessageRequest { SessionId = sessionId, Role = role, Content = content },
                _json);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SaveMessage fallito.");
        }
    }

    public async Task DeleteSessionAsync(Guid sessionId)
    {
        try
        {
            await _http.DeleteAsync($"api/v1/chat/sessions/{sessionId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeleteSession fallito per sessione {Id}.", sessionId);
        }
    }

    public async Task<Dictionary<string, string>> GetSettingsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<Dictionary<string, string>>("api/v1/settings", _json) ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetSettingsAsync fallito.");
            return new Dictionary<string, string>();
        }
    }

    public async Task UpdateSettingsAsync(Dictionary<string, string> settings)
    {
        try
        {
            await _http.PatchAsJsonAsync("api/v1/settings", settings, _json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UpdateSettingsAsync fallito.");
        }
    }

    // ── Community Agents ─────────────────────────────────────────────────────

    public async Task<List<CommunityAgentDto>> GetCommunityAgentsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<CommunityAgentDto>>("api/v1/agents/list", _json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetCommunityAgentsAsync fallito.");
            return [];
        }
    }

    public async Task<(bool Success, string CompositeKey)> SyncCommunityAgentAsync(SyncAgentRequest request)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/v1/agents/sync", request, _json);
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
                if (content != null && content.TryGetPropertyValue("compositeKey", out var keyNode) && keyNode != null)
                {
                    return (true, keyNode.ToString());
                }
            }
            return (false, "");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SyncCommunityAgentAsync fallito.");
            return (false, "");
        }
    }

    public async Task<bool> SubmitCommunityAgentsAsync(List<string> agentKeys)
    {
        try
        {
            var req = new SubmitAgentRequest { AgentKeys = agentKeys };
            var res = await _http.PostAsJsonAsync("api/v1/agents/submit", req, _json);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SubmitCommunityAgentsAsync fallito.");
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetAuthHeader()
    {
        if (_jwtToken is not null)
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _jwtToken);
    }
}
