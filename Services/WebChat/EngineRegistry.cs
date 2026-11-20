using AIBridge.Services.WebChat.Engines.Gemini;

namespace AIBridge.Services.WebChat;

/// <summary>
/// Registro degli engine WebChat disponibili per <c>engineId</c> (Unit 8).
/// Di default è registrato solo <c>"gemini"</c>.
/// </summary>
public sealed class EngineRegistry
{
    private readonly Dictionary<string, Func<ISelectorsService, IUiBridge, IChatWebEngine>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    public static EngineRegistry Default { get; } = CreateDefault();

    private static EngineRegistry CreateDefault()
    {
        var registry = new EngineRegistry();
        registry.Register("gemini", (selectors, ui) => new GeminiWebEngine(selectors, new GeminiLoginFlow(ui)));
        return registry;
    }

    public void Register(string engineId, Func<ISelectorsService, IUiBridge, IChatWebEngine> factory)
    {
        if (string.IsNullOrWhiteSpace(engineId))
            throw new ArgumentException("Engine ID is required.", nameof(engineId));
        ArgumentNullException.ThrowIfNull(factory);
        _factories[engineId.Trim()] = factory;
    }

    public bool IsRegistered(string engineId) =>
        !string.IsNullOrWhiteSpace(engineId) && _factories.ContainsKey(engineId.Trim());

    public IReadOnlyCollection<string> RegisteredIds => _factories.Keys;

    public IChatWebEngine CreateEngine(string engineId, ISelectorsService selectors, IUiBridge ui)
    {
        if (string.IsNullOrWhiteSpace(engineId))
            throw new ArgumentException("Engine ID is required.", nameof(engineId));

        if (!_factories.TryGetValue(engineId.Trim(), out var factory))
            throw new ArgumentException($"Unknown WebChat engine '{engineId}'.", nameof(engineId));

        return factory(selectors, ui);
    }
}

