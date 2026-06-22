using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AIBridge.Dialogs
{
    public partial class OAuthLoginDialog : Window
    {
        public string? Code { get; private set; }
        public string? State { get; private set; }

        private readonly string _authUrl;

        public OAuthLoginDialog(string authUrl)
        {
            InitializeComponent();
            _authUrl = authUrl;
            Loaded += OAuthLoginDialog_Loaded;
        }

        private async void OAuthLoginDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                
                // Pulisce i cookie per consentire il login di un utente diverso se necessario
                webView.CoreWebView2.CookieManager.DeleteAllCookies();

                webView.Source = new Uri(_authUrl);
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Error initializing WebView2: {0}", ex.Message), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // Intercetta il redirect verso il callback
            if (e.Uri.StartsWith("https://aibridge.org/callback", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                
                var uri = new Uri(e.Uri);
                var query = uri.Query.TrimStart('?').Split('&');
                
                foreach (var param in query)
                {
                    var parts = param.Split('=');
                    if (parts.Length == 2)
                    {
                        if (parts[0] == "code") Code = Uri.UnescapeDataString(parts[1]);
                        if (parts[0] == "state") State = Uri.UnescapeDataString(parts[1]);
                    }
                }
                
                DialogResult = true;
                Close();
            }
        }
    }
}
