using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace AIBridge.Dialogs
{
    public partial class ManualLoginWindow : Window
    {
        private readonly string _userDataDir;
        // Guard: MakeVisible() deve essere chiamata una sola volta.
        // NavigationCompleted scatta su ogni URL di accounts.google.com durante il flusso
        // multi-step di Google (selezione account, 2FA, ecc.) — senza questo flag il popup
        // riapparirebbe ad ogni redirect interno.
        private bool _hasBeenMadeVisible = false;

        // Se true, la finestra si è chiusa autonomamente (utente già loggato) e NON deve essere mostrata.
        // GeminiLoginFlow controlla questa proprietà dopo che ShowDialog() ritorna.
        public bool WasAutoClosedAsLoggedIn { get; private set; } = false;

        public ManualLoginWindow(string userDataDir)
        {
            InitializeComponent();
            _userDataDir = userDataDir;

            // Posiziona la finestra fuori schermo invece di usare Opacity=0.
            // Con Opacity=0 il compositor Chromium di WebView2 sospende il rendering
            // (la finestra è "invisibile" per DWM) e impiega 4+ secondi a riprendere.
            // Fuori schermo (-32000) DWM continua a renderizzare normalmente:
            // la pagina viene caricata e dipinta prima che la finestra sia spostata al centro.
            this.Left = -32000;
            this.Top = 0;

            InitializeWebViewAsync();
        }

        // ── Inizializzazione WebView2 ─────────────────────────────────────────

        private async void InitializeWebViewAsync()
        {
            try
            {
                // Stessa logica usata da Playwright: imposta Locale = CurrentUICulture
                // → WebView2 invia l'header Accept-Language corretto a Google
                // → la pagina di login appare nella lingua dell'utente (es. it-IT → italiano)
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    Language = System.Globalization.CultureInfo.CurrentUICulture.Name
                };
                var env = await CoreWebView2Environment.CreateAsync(null, _userDataDir, envOptions);
                await LoginWebView.EnsureCoreWebView2Async(env);

                // SourceChanged → solo aggiornamento UI (barra URL, pulsanti nav)
                LoginWebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                LoginWebView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
                LoginWebView.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;

                // NavigationCompleted → logica di login/chiusura.
                // A differenza di SourceChanged, scatta SOLO quando la navigazione è terminata
                // (inclusi tutti i redirect intermedi). Evita false detection su URL di passaggio.
                LoginWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                // Naviga verso Gemini: se già loggato arriva definitivamente su /app → chiude.
                // Se non loggato, Google reindirizza su accounts.google.com → rendiamo visibile.
                LoginWebView.CoreWebView2.Navigate("https://gemini.google.com");

                // Sicurezza: se dopo 5s NavigationCompleted non ha ancora deciso, mostriamo la finestra
                StartFallbackVisibilityTimer();
            }
            catch (Exception ex)
            {
                MakeVisible();
                AIBridge.Dialogs.CustomMessageBox.Show(
                    Unclassified.TxLib.Tx.T("Error initializing WebView2: {0}", ex.Message),
                    Unclassified.TxLib.Tx.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Fallback timer: mostra la finestra dopo 3s se la navigazione è troppo lenta ──

        private void StartFallbackVisibilityTimer()
        {
            System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Se la finestra è ancora fuori schermo (Left molto negativo) significa che
                    // NavigationCompleted non ha ancora chiamato MakeVisible() — forziamo la visualizzazione.
                    if (this.Left < -1000 && this.IsVisible)
                        MakeVisible();
                });
            });
        }

        // ── Drag (tab strip) e pulsanti finestra ─────────────────────────────

        private void TabStrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Permette di trascinare la finestra cliccando sulla tab strip
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // ── Rende la finestra visibile all'utente ─────────────────────────────

        private void MakeVisible()
        {
            if (_hasBeenMadeVisible) return;
            _hasBeenMadeVisible = true;
            // Mostra popup PRIMA di spostare la finestra al centro:
            // la pagina Google è già caricata in background (finestra era fuori schermo),
            // quindi mentre l'utente legge le istruzioni la pagina finisce di renderizzare.
            // Quando clicca OK, la finestra appare già pronta — niente nero, niente attesa.
            AIBridge.Dialogs.CustomMessageBox.Show(
                Unclassified.TxLib.Tx.T("A browser window will open for you to sign in to Google. Complete the login and the window will close automatically."),
                Unclassified.TxLib.Tx.T("Google Login Required"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            this.ShowInTaskbar = true;

            // Centra la finestra rispetto all'Owner (o allo schermo primario se non c'è Owner)
            double w = this.Width;
            double h = this.Height;
            if (this.Owner != null)
            {
                this.Left = this.Owner.Left + (this.Owner.ActualWidth - w) / 2;
                this.Top = this.Owner.Top + (this.Owner.ActualHeight - h) / 2;
            }
            else
            {
                this.Left = (SystemParameters.PrimaryScreenWidth - w) / 2;
                this.Top = (SystemParameters.PrimaryScreenHeight - h) / 2;
            }

            this.Activate();
            this.Focus();

            // Dissolvi l'overlay: la pagina è già renderizzata (WebView2 non ha mai smesso
            // di renderizzare perché la finestra era fuori schermo, non invisibile).
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
                fromValue: 1,
                toValue: 0,
                duration: new System.Windows.Duration(TimeSpan.FromMilliseconds(300)));
            fadeOut.Completed += (_, _) =>
            {
                LoadingOverlay.Visibility = System.Windows.Visibility.Collapsed;
            };
            LoadingOverlay.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOut);
        }

        // ── Aggiornamento toolbar in base alla navigazione ────────────────────

        // SourceChanged → solo aggiornamento UI della toolbar.
        // NON contiene logica di login: scatta anche su URL intermedi durante i redirect.
        private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (LoginWebView.CoreWebView2 == null) return;
            string url = LoginWebView.CoreWebView2.Source;
            TxtUrl.Text = url;
            BtnBack.IsEnabled = LoginWebView.CoreWebView2.CanGoBack;
            BtnForward.IsEnabled = LoginWebView.CoreWebView2.CanGoForward;
        }

        // NavigationCompleted → logica di login/chiusura.
        // Scatta SOLO quando la navigazione finale e' completa (tutti i redirect inclusi).
        // Google serve /app brevemente anche per sessioni invalide prima che JS faccia redirect
        // a accounts.google.com: usando NavigationCompleted evitiamo quella false detection.
        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (LoginWebView.CoreWebView2 == null) return;
            string url = LoginWebView.CoreWebView2.Source;

            if (url.Contains("accounts.google.com"))
            {
                MakeVisible();
            }
            else if (url.Contains("gemini.google.com/app"))
            {
                // FIX (ott-2026): Gemini ora serve /app anche agli utenti NON loggati (Flash-Lite).
                // Prima di chiuderci automaticamente, verifichiamo che l'utente sia effettivamente
                // autenticato controllando i data-test-id che la SPA inietta solo per utenti anonimi.
                bool isLoggedIn = true;
                try
                {
                    var result = await LoginWebView.CoreWebView2.ExecuteScriptAsync(
                        "JSON.stringify(" +
                        "!document.querySelector('[data-test-id=\"sidenav-error-action-link\"]') && " +
                        "!document.querySelector('[data-test-id=\"sidenav-error-message\"]')" +
                        ")"
                    );
                    // ExecuteScriptAsync ritorna una stringa JSON: "true" o "false"
                    isLoggedIn = result?.Trim('"') == "true";
                }
                catch { /* se il check fallisce, assumiamo loggato per non bloccare il flusso */ }

                if (isLoggedIn)
                {
                    _ = ExtractCookiesAndCloseAsync(autoLoggedIn: this.Left < -1000);
                }
                else
                {
                    // Non loggato: mostriamo la finestra di login
                    MakeVisible();
                }
            }
        }

        private void CoreWebView2_HistoryChanged(object? sender, object e)
        {
            BtnBack.IsEnabled = LoginWebView.CoreWebView2?.CanGoBack ?? false;
            BtnForward.IsEnabled = LoginWebView.CoreWebView2?.CanGoForward ?? false;
        }

        private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
        {
            string pageTitle = LoginWebView.CoreWebView2?.DocumentTitle ?? string.Empty;
            string title = string.IsNullOrWhiteSpace(pageTitle) ? "Accedi – Google" : pageTitle;
            this.Title = title;
            TxtTabTitle.Text = title;
        }

        // ── Pulsanti di navigazione ───────────────────────────────────────────

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (LoginWebView.CoreWebView2?.CanGoBack == true)
                LoginWebView.CoreWebView2.GoBack();
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (LoginWebView.CoreWebView2?.CanGoForward == true)
                LoginWebView.CoreWebView2.GoForward();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoginWebView.CoreWebView2?.Reload();
        }

        // ── Pulsante Fatto ────────────────────────────────────────────────────

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            await ExtractCookiesAndCloseAsync(autoLoggedIn: false);
        }

        // ── Estrazione cookie e chiusura ──────────────────────────────────────

        public System.Collections.Generic.List<CoreWebView2Cookie>? ExtractedCookies { get; private set; }

        private async System.Threading.Tasks.Task ExtractCookiesAndCloseAsync(bool autoLoggedIn)
        {
            try
            {
                if (LoginWebView.CoreWebView2 != null)
                {
                    ExtractedCookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://google.com");
                    var geminiCookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://gemini.google.com");
                    if (geminiCookies != null) ExtractedCookies.AddRange(geminiCookies);
                }
            }
            catch { }

            WasAutoClosedAsLoggedIn = autoLoggedIn;

            try
            {
                this.DialogResult = true;
                this.Close();
            }
            catch { }
        }
    }
}
