using Microsoft.Playwright;

namespace AIBridge.Services.WebChat;

/// <summary>
/// Aiuti di input "umano" messi a disposizione degli engine (es. per allegare file).
/// Implementato da HumanInputDriver in Unit 6; definito qui per non creare dipendenze circolari.
/// </summary>
public interface IHumanInput
{
    Task DelayAsync(int minMs, int maxMs);
    Task ClickAsync(ILocator locator);
}

/// <summary>
/// Accesso alla UI WPF, isolato per rendere testabile il layer browser senza Application.Current.
/// </summary>
public interface IUiBridge
{
    /// <summary>Esegue l'azione sul thread UI e attende il completamento.</summary>
    void Invoke(Action action);

    /// <summary>Porta la finestra principale in primo piano (Chromium passa in secondo piano).</summary>
    void BringMainWindowToFront();
}

/// <summary>Login manuale interattivo specifico di un engine (es. WebView2 + cookie).</summary>
public interface ILoginFlow
{
    /// <summary>
    /// Mostra il flusso di login e restituisce i cookie da iniettare nel contesto Playwright
    /// (null se non necessario). userDataDir è la cartella profilo da sbloccare/condividere.
    /// </summary>
    Task<IReadOnlyList<Cookie>?> RunInteractiveLoginAsync(string userDataDir);
}

/// <summary>Installazione del browser Chromium per Playwright.</summary>
public interface IBrowserInstaller
{
    /// <summary>Scarica/verifica Chromium. Può richiedere molto tempo al primo avvio.</summary>
    Task InstallAsync();
}


