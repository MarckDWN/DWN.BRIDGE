using AIBridge.Models;
using AIBridge.Shared.Models;

namespace AIBridge.Services.Extensions;

/// <summary>
/// Adapter che delega al server REST (CloudBrainClient).
/// Usato quando il server cloud è raggiungibile e l'utente è autenticato.
/// </summary>
public sealed class ServerSessionAdapter : ISessionAdapter
{
    private readonly ICloudBrainClient _client;

    public ServerSessionAdapter(ICloudBrainClient client)
    {
        _client = client;
    }

    public bool IsAvailable => _client.IsConnected;

    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync()
    {
        var dtos = await _client.GetSessionsAsync();
        return dtos.Select(dto => new ChatSession
        {
            ServerId        = dto.Id,
            GeminiId        = dto.GeminiChatId   ?? string.Empty,
            Description     = dto.Description    ?? string.Empty,
            AgentRoleType   = dto.AgentType,
            AgentName       = dto.AgentType,
            WorkspacePath   = dto.WorkspacePath  ?? string.Empty,
            CreatedAt       = dto.CreatedAt.LocalDateTime
        }).ToList();
    }

    public async Task<Guid> BeginSessionAsync(string agentType, string geminiChatId = "",
                                               string description = "", string workspacePath = "")
    {
        var dto = await _client.CreateSessionAsync(agentType, geminiChatId, description, workspacePath);
        return dto?.Id ?? Guid.NewGuid();
    }

    public async Task UpdateDescriptionAsync(Guid sessionId, string description)
    {
        await _client.UpdateSessionAsync(sessionId, description: description);
    }

    public async Task AppendAsync(Guid sessionId, string role, string content)
    {
        await _client.SaveMessageAsync(sessionId, role, content);
    }

    public async Task RemoveAsync(Guid sessionId)
    {
        await _client.DeleteSessionAsync(sessionId);
    }

    public async Task<IReadOnlyList<SerializableChatMessage>> GetMessagesAsync(Guid sessionId)
    {
        var dtos = await _client.GetMessagesAsync(sessionId);
        return dtos.Select(dto => new SerializableChatMessage
        {
            Sender = dto.Role == "user" ? "You" : "Assistant",
            Text   = dto.Content,
            IsUser = dto.Role == "user"
        }).ToList();
    }

    public async Task SyncGeminiIdAsync(Guid sessionId, string geminiChatId)
    {
        await _client.UpdateSessionAsync(sessionId, geminiChatId: geminiChatId);
    }

    public async Task SaveSessionAsync(ChatSession session)
    {
        // Sincronizza Description e WorkspacePath sul server tramite PATCH
        await _client.UpdateSessionAsync(
            session.ServerId,
            description:   string.IsNullOrWhiteSpace(session.Description)   ? null : session.Description,
            workspacePath: string.IsNullOrWhiteSpace(session.WorkspacePath) ? null : session.WorkspacePath,
            agentType:     string.IsNullOrWhiteSpace(session.AgentRoleType) ? null : session.AgentRoleType);
    }
}
