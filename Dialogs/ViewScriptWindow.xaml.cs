using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Unclassified.TxLib;

namespace AIBridge.Dialogs
{
    public partial class ViewScriptWindow : Window
    {
        private string _localFilePath;
        private DispatcherTimer? _timer;
        private DateTime _lastWriteTime;

        public ViewScriptWindow(string agentName, string scriptContent, bool isCustom, string localFilePath = "")
        {
            InitializeComponent();
            AgentNameTextBlock.Text = $"{Tx.T("Agent Script")}: {agentName}";
            ScriptTextBox.Text = scriptContent;

            _localFilePath = localFilePath;

            if (isCustom && !string.IsNullOrEmpty(_localFilePath) && File.Exists(_localFilePath))
            {
                EditButton.Visibility = Visibility.Visible;

                // Start polling file modifications
                _lastWriteTime = File.GetLastWriteTime(_localFilePath);
                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _timer.Tick += Timer_Tick;
                _timer.Start();

                Closed += (s, e) => _timer?.Stop();
            }
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (File.Exists(_localFilePath))
                {
                    var currentWriteTime = File.GetLastWriteTime(_localFilePath);
                    if (currentWriteTime != _lastWriteTime)
                    {
                        _lastWriteTime = currentWriteTime;
                        string text = await File.ReadAllTextAsync(_localFilePath);
                        ScriptTextBox.Text = text;
                    }
                }
            }
            catch
            {
                // Suppress exceptions when file is locked or temporarily inaccessible
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_localFilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _localFilePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ScriptTextBox.Text);
                CustomMessageBox.Show(Tx.T("Script copied to clipboard!"), Tx.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.Message, Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
