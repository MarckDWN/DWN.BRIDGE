using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Windows;

namespace AIBridge
{
    public partial class CaptchaWebViewDialog : Window
    {
        private string _targetUrl;
        private readonly string _userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBridge", "GeminiProfile");

        public CaptchaWebViewDialog(string targetUrl)
        {
            InitializeComponent();
            _targetUrl = targetUrl;
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, _userDataDir);
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Navigate(_targetUrl);
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Error initializing WebView2: {0}", ex.Message), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
