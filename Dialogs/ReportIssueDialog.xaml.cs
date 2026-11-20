using AIBridge.Shared.Models;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace AIBridge.Dialogs
{
    public partial class ReportIssueDialog : Window
    {
        private readonly Guid? _activeSessionId;
        private readonly string _selectedAgentName;
        private readonly string _clientVersion;
        private readonly string _osInfo;

        // Questo DTO conterrà i dati inseriti dall'utente se il dialogo si chiude con successo (DialogResult = true)
        public SubmitIssueRequest? ResultRequest { get; private set; }

        public ReportIssueDialog(Guid? activeSessionId, string? selectedAgentName)
        {
            InitializeComponent();

            _activeSessionId = activeSessionId;
            _selectedAgentName = selectedAgentName ?? "Default";

            // Recupera versione client ed info OS
            _clientVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";
            _osInfo = $"{Environment.OSVersion.VersionString} {RuntimeInformation.OSArchitecture}";

            // Aggiorna footer di diagnostica
            DiagnosticsTextBlock.Text = $"Version: {_clientVersion} | Agent: {_selectedAgentName} | OS: {_osInfo}";

            // Configura stato iniziale del consenso sessione
            if (_activeSessionId.HasValue)
            {
                SessionInfoTextBlock.Text = $"Session GUID: {_activeSessionId.Value}";
                ConsentCheckBox.IsChecked = true;
                SessionInfoTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                // Nascondi se non c'è una sessione attiva
                ConsentCheckBox.Visibility = Visibility.Collapsed;
                SessionInfoTextBlock.Visibility = Visibility.Collapsed;
            }

            UpdateSendButtonState();
        }

        private void UpdateSendButtonState()
        {
            if (SendButton == null) return;

            bool isAnyCategoryChecked = false;
            if (CategoriesPanel != null)
            {
                foreach (var child in CategoriesPanel.Children)
                {
                    if (child is CheckBox cb && cb.IsChecked == true)
                    {
                        isAnyCategoryChecked = true;
                        break;
                    }
                }
            }

            SendButton.IsEnabled = isAnyCategoryChecked;
        }

        private void Category_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSendButtonState();
        }

        private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSendButtonState();
        }

        private void Consent_Changed(object sender, RoutedEventArgs e)
        {
            if (SessionInfoTextBlock == null) return;

            if (ConsentCheckBox.IsChecked == true && _activeSessionId.HasValue)
            {
                SessionInfoTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                SessionInfoTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var description = DescriptionTextBox.Text.Trim();
            if (description.Length < 20)
            {
                CustomMessageBox.Show(
                    Unclassified.TxLib.Tx.T("Please enter a detailed description of at least 20 characters so we can diagnose the issue."),
                    Unclassified.TxLib.Tx.T("Description Too Short"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DescriptionTextBox.Focus();
                return;
            }

            // Raccoglie le categorie selezionate
            var selectedCategories = new List<string>();
            if (CbWorkspace.IsChecked == true) selectedCategories.Add("Workspace");
            if (CbSyntax.IsChecked == true) selectedCategories.Add("Syntax");
            if (CbCommandExec.IsChecked == true) selectedCategories.Add("CommandExec");
            if (CbSqlQuery.IsChecked == true) selectedCategories.Add("SqlQuery");
            if (CbStreamInterrupted.IsChecked == true) selectedCategories.Add("StreamInterrupted");
            if (CbUiFocus.IsChecked == true) selectedCategories.Add("UIFocus");
            if (CbAuthLogin.IsChecked == true) selectedCategories.Add("AuthLogin");
            if (CbAgentSync.IsChecked == true) selectedCategories.Add("AgentSync");
            if (CbOther.IsChecked == true) selectedCategories.Add("Other");

            // Popola il DTO di richiesta
            ResultRequest = new SubmitIssueRequest
            {
                SelectedIssueTypes = selectedCategories,
                UserDescription = description,
                ShareConversationConsent = ConsentCheckBox.IsChecked == true,
                ActiveSessionId = _activeSessionId,
                ClientVersion = _clientVersion,
                OsInfo = _osInfo,
                SelectedAgentName = _selectedAgentName
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
