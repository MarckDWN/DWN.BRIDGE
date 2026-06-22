using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AIBridge
{
    public partial class OAuthLoginDialog : Window
    {
        public string AuthorizationCode { get; private set; } = string.Empty;
        public string ReturnedState { get; private set; } = string.Empty;

        public OAuthLoginDialog(string authUrl, string userDataFolder)
        {
            InitializeComponent();
            InitializeWebView(authUrl, userDataFolder);
        }

        private async void InitializeWebView(string authUrl, string userDataFolder)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await OAuthWebView.EnsureCoreWebView2Async(env);
                
                OAuthWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                OAuthWebView.NavigationCompleted += (s, e) => LoadingPanel.Visibility = Visibility.Collapsed;
                
                OAuthWebView.Source = new Uri(authUrl);
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Error initializing WebView2: {0}", ex.Message), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri.StartsWith("aibridge://callback", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("https://aibridge.org/callback", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                
                var uri = new Uri(e.Uri);
                // Simple parsing to avoid HttpUtility dependency if not available, though System.Web is usually present.
                // Using basic string split to parse query string.
                string query = uri.Query.TrimStart('?');
                var parts = query.Split('&');
                foreach (var part in parts)
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        if (kv[0].Equals("code", StringComparison.OrdinalIgnoreCase)) AuthorizationCode = kv[1];
                        if (kv[0].Equals("state", StringComparison.OrdinalIgnoreCase)) ReturnedState = kv[1];
                    }
                }
                
                DialogResult = true;
                Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
