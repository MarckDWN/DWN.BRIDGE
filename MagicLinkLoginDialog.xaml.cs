using System;
using System.Threading.Tasks;
using System.Windows;
using AIBridge.Services;

namespace AIBridge
{
    public partial class MagicLinkLoginDialog : Window
    {
        private readonly ICloudBrainClient _client;
        private bool _isPolling;

        public string? Token { get; private set; }

        public MagicLinkLoginDialog(ICloudBrainClient client)
        {
            InitializeComponent();
            _client = client;
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailTextBox.Text.Trim();
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Please enter a valid email address."), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SendButton.IsEnabled = false;
            EmailTextBox.IsEnabled = false;

            var response = await _client.StartMagicLinkAsync(email);
            if (response == null || string.IsNullOrEmpty(response.SessionId))
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Error sending Magic Link."), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                SendButton.IsEnabled = true;
                EmailTextBox.IsEnabled = true;
                return;
            }

            // Inizia il polling
            EmailTextBox.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Visible;
            SendButton.Visibility = Visibility.Collapsed;

            _isPolling = true;
            _ = PollStatusAsync(response.SessionId);
        }

        private async Task PollStatusAsync(string sessionId)
        {
            while (_isPolling)
            {
                await Task.Delay(2000); // Polling ogni 2 secondi
                
                var status = await _client.PollMagicLinkStatusAsync(sessionId);
                if (status != null)
                {
                    if (status.Status == "verified")
                    {
                        _isPolling = false;
                        Token = status.Token;
                        DialogResult = true;
                        Close();
                        return;
                    }
                    else if (status.Status == "expired")
                    {
                        _isPolling = false;
                        AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("The Magic Link has expired. Please try again."), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        DialogResult = false;
                        Close();
                        return;
                    }
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _isPolling = false;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _isPolling = false;
            base.OnClosed(e);
        }
    }
}
