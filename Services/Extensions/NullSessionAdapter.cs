using AIBridge.Models;

namespace AIBridge.Services.Extensions;

/// <summary>
/// Adapter in-memory senza persistenza.
/// Usato dalla Community Edition quando nessun server è disponibile
/// e nessuna estensione è installata nella cartella extensions/.
/// Le sessioni esistono solo per la durata della sessione applicativa corrente.
/// </summary>
public sealed class NullSessionAdapter : ISessionAdapter
{
    private readonly List<(ChatSession Session, Guid Id)> _sessions = new();
    private readonly Dictionary<Guid, List<SerializableChatMessage>> _messages = new();

    public bool IsAvailable => true;

    public Task<IReadOnlyList<ChatSession>> GetSessionsAsync()
        => Task.FromResult<IReadOnlyList<ChatSession>>(
            _sessions.Select(s => s.Session).ToList());

    public Task<Guid> BeginSessionAsync(string agentType, string geminiChatId = "",
                                         string description = "", string workspacePath = "")
    {
        var id = Guid.NewGuid();
        var session = new ChatSession
        {
            GeminiId      = geminiChatId,
            Description   = description,
            WorkspacePath = workspacePath,
            AgentRoleType = agentType,
            AgentName     = agentType,
            CreatedAt     = DateTime.Now
        };
        _sessions.Insert(0, (session, id));
        _messages[id] = new List<SerializableChatMessage>();
        return Task.FromResult(id);
    }

    public Task UpdateDescriptionAsync(Guid sessionId, string description)
    {
        var entry = _sessions.FirstOrDefault(s => s.Id == sessionId);
        if (entry != default) entry.Session.Description = description;
        return Task.CompletedTask;
    }

    public Task AppendAsync(Guid sessionId, string role, string content)
    {
        if (!_messages.ContainsKey(sessionId))
            _messages[sessionId] = new List<SerializableChatMessage>();

        _messages[sessionId].Add(new SerializableChatMessage
        {
            Sender = role == "user" ? "You" : "Assistant",
            Text   = content,
            IsUser = role == "user"
        });
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid sessionId)
    {
        _sessions.RemoveAll(s => s.Id == sessionId);
        _messages.Remove(sessionId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SerializableChatMessage>> GetMessagesAsync(Guid sessionId)
    {
        _messages.TryGetValue(sessionId, out var msgs);
        return Task.FromResult<IReadOnlyList<SerializableChatMessage>>(msgs ?? new List<SerializableChatMessage>());
    }

    public Task SyncGeminiIdAsync(Guid sessionId, string geminiChatId)
    {
        var entry = _sessions.FirstOrDefault(s => s.Id == sessionId);
        if (entry != default) entry.Session.GeminiId = geminiChatId;
        return Task.CompletedTask;
    }

    public Task SaveSessionAsync(ChatSession session)
    {
        // In-memory: l'oggetto session è già aggiornato per reference.
        return Task.CompletedTask;
    }
}
