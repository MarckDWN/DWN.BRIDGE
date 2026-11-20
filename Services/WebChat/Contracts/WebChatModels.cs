namespace AIBridge.Services.WebChat;

/// <summary>Ruolo di un elemento messaggio nel DOM di una webchat.</summary>
public enum MessageRole
{
    Unknown = 0,
    User,
    Assistant
}

/// <summary>Motivo per cui la pagina della webchat non è utilizzabile (login richiesto, captcha, ...).</summary>
public enum BlockReason
{
    /// <summary>Pagina di blocco esplicita del provider (es. captcha/"sorry" page, redirect al login).</summary>
    ExplicitBlockPage,
    /// <summary>La SPA è raggiungibile ma l'utente risulta non loggato.</summary>
    SignedOut,
    /// <summary>Link di login visibile nella pagina.</summary>
    LoginLink,
    /// <summary>Captcha visibile nella pagina.</summary>
    Captcha
}

/// <summary>Esito della navigazione verso una chat esistente.</summary>
public enum NavigationResult
{
    Ok,
    LoginRequired,
    ChatNotFound
}

/// <summary>Contesto passato all'engine per le operazioni di navigazione.</summary>
/// <param name="Language">Codice lingua a due lettere (es. "it") della UI corrente.</param>
public sealed record NavigationContext(string Language);

/// <summary>Identità statica di un engine webchat.</summary>
/// <param name="Id">Identificatore tecnico stabile (es. "gemini"); usato per file di override e factory.</param>
/// <param name="DisplayName">Nome mostrabile all'utente.</param>
/// <param name="ProfileFolderName">
/// Cartella del profilo browser sotto %LocalAppData%\AIBridge. Cambiarla equivale a un logout
/// per tutti gli utenti: ogni engine deve preservare il nome già in uso sui PC esistenti.
/// </param>
/// <param name="BaseUrl">URL base del servizio.</param>
public sealed record EngineDescriptor(string Id, string DisplayName, string ProfileFolderName, string BaseUrl);

/// <summary>Esito della verifica di un singolo selettore CSS durante <see cref="IChatWebEngine.SelfTestAsync"/>.</summary>
public sealed record SelectorCheckResult(string Name, string Selector, bool Found, int MatchCount, string? Note = null);

/// <summary>Report diagnostico prodotto da <see cref="IChatWebEngine.SelfTestAsync"/>.</summary>
public sealed record EngineSelfTestReport(
    string EngineId,
    DateTimeOffset Timestamp,
    IReadOnlyList<SelectorCheckResult> Checks)
{
    public bool AllCriticalFound => Checks.Where(c => c.Note != "optional").All(c => c.Found);
}
