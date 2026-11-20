using Microsoft.Playwright;

namespace AIBridge.Services.WebChat.Engines.Gemini;

/// <summary>
/// Flusso di login interattivo per Google Gemini basato su <see cref="Dialogs.ManualLoginWindow"/> (WebView2).
/// Aggira i blocchi anti-bot di Google su Chromium/Playwright eseguendo il login in WebView2
/// ed estraendo i cookie di sessione da iniettare nel contesto Playwright.
/// </summary>
public sealed class GeminiLoginFlow : ILoginFlow
{
    private readonly IUiBridge _ui;

    public GeminiLoginFlow(IUiBridge? ui = null)
    {
        _ui = ui ?? new WpfUiBridge();
    }

    public Task<IReadOnlyList<Cookie>?> RunInteractiveLoginAsync(string userDataDir)
    {
        List<Cookie>? pwCookies = null;

        // La ManualLoginWindow gestisce internamente la propria visibilità:
        // - Parte invisible (Opacity=0) per non mostrare nulla se l'utente è già loggato.
        // - Si rende visibile autonomamente solo se rileva accounts.google.com (login necessario).
        // ShowDialog() è necessario per creare l'HWND che WebView2 richiede per funzionare.
        _ui.Invoke(() =>
        {
            var loginWindow = new Dialogs.ManualLoginWindow(userDataDir)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            loginWindow.ShowDialog();

            if (loginWindow.ExtractedCookies != null)
            {
                pwCookies = new List<Cookie>();
                foreach (var c in loginWindow.ExtractedCookies)
                {
                    pwCookies.Add(new Cookie
                    {
                        Name     = c.Name,
                        Value    = c.Value,
                        Domain   = c.Domain,
                        Path     = c.Path,
                        Secure   = c.IsSecure,
                        HttpOnly = c.IsHttpOnly,
                        Expires  = (c.Expires > new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                    ? new DateTimeOffset(c.Expires).ToUnixTimeSeconds()
                                    : -1
                    });
                }
            }
        });

        return Task.FromResult<IReadOnlyList<Cookie>?>(pwCookies);
    }
}

