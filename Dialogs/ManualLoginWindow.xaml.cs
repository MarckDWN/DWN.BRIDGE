using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AIBridge.Dialogs
{
    public partial class ManualLoginWindow : Window
    {
        private readonly string _userDataDir;

        public ManualLoginWindow(string userDataDir)
        {
            InitializeComponent();
            _userDataDir = userDataDir;
            InitializeWebViewAsync();
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                // Creiamo l'environment WebView2 puntando esattamente alla stessa cartella profilo di Playwright
                var env = await CoreWebView2Environment.CreateAsync(null, _userDataDir);
                await LoginWebView.EnsureCoreWebView2Async(env);

                // Quando l'URL cambia, controlliamo se l'utente e' atterrato sull'app di Gemini
                LoginWebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;

                // Navigazione iniziale
                LoginWebView.CoreWebView2.Navigate("https://gemini.google.com");
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Error initializing WebView2: {0}", ex.Message), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool _hasReachedLoginPage = false;

        public System.Collections.Generic.List<CoreWebView2Cookie>? ExtractedCookies { get; private set; }

        private async void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (LoginWebView.CoreWebView2 != null)
            {
                string url = LoginWebView.CoreWebView2.Source;
                
                // Se passiamo dalla pagina degli account Google, significa che stiamo facendo o dobbiamo fare il login
                if (url.Contains("accounts.google.com"))
                {
                    _hasReachedLoginPage = true;
                }

                // Se siamo su Gemini E abbiamo già superato la fase di login (o eravamo già loggati)
                if (url.Contains("gemini.google.com/app") && _hasReachedLoginPage)
                {
                    await ExtractCookiesAndCloseAsync();
                }
            }
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            await ExtractCookiesAndCloseAsync();
        }

        private async System.Threading.Tasks.Task ExtractCookiesAndCloseAsync()
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
            
            this.DialogResult = true;
            this.Close();
        }
    }
}
