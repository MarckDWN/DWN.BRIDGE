using AIBridge.Shared.Models;
using System.Windows;
using System.Windows.Controls;

namespace AIBridge.Dialogs
{
    public partial class MyIssuesWindow : Window
    {
        private readonly Guid? _activeSessionId;
        private readonly string? _selectedAgentName;
        private readonly string _hwid;

        public MyIssuesWindow(Guid? activeSessionId, string? selectedAgentName)
        {
            InitializeComponent();

            _activeSessionId = activeSessionId;
            _selectedAgentName = selectedAgentName;
            _hwid = new AIBridge.Services.HwidService().ComputeHwid();

            Loaded += async (s, e) =>
            {
                await RefreshListAsync();
            };
        }

        private async Task RefreshListAsync()
        {
            var client = AIBridge.Services.CloudServiceLocator.Client;
            if (client == null) return;

            // Mostra cursore di caricamento
            var oldCursor = this.Cursor;
            this.Cursor = System.Windows.Input.Cursors.Wait;

            try
            {
                var list = await client.GetMyIssuesAsync(_hwid);
                dgIssues.ItemsSource = list;

                // Controlla se ci sono ticket attivi che bloccano nuove segnalazioni
                bool hasActiveTicket = list.Any(issue =>
                    issue.Status.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                    issue.Status.Equals("InProgress", StringComparison.OrdinalIgnoreCase) ||
                    issue.Status.Equals("NeedsInfo", StringComparison.OrdinalIgnoreCase));

                if (hasActiveTicket)
                {
                    BtnNewTicket.IsEnabled = false;
                    TxtActiveTicketWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    BtnNewTicket.IsEnabled = true;
                    TxtActiveTicketWarning.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    Unclassified.TxLib.Tx.T("Failed to load submissions: {0}", ex.Message),
                    Unclassified.TxLib.Tx.T("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = oldCursor;
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshListAsync();
        }

        private void DgIssues_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgIssues.SelectedItem is UserIssueDto selected &&
                selected.Status.Equals("NeedsInfo", StringComparison.OrdinalIgnoreCase))
            {
                TxtAdminNote.Text = string.IsNullOrEmpty(selected.FeedbackRequest)
                    ? Unclassified.TxLib.Tx.T("No admin note provided.")
                    : Unclassified.TxLib.Tx.T("Admin Note: {0}", selected.FeedbackRequest);

                TxtReplyInput.Text = string.Empty;
                BrdReplyArea.Visibility = Visibility.Visible;
            }
            else
            {
                BrdReplyArea.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnSendReply_Click(object sender, RoutedEventArgs e)
        {
            var reply = TxtReplyInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(reply) || reply.Length < 10)
            {
                CustomMessageBox.Show(
                    Unclassified.TxLib.Tx.T("Please enter a reply of at least 10 characters."),
                    Unclassified.TxLib.Tx.T("Validation Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TxtReplyInput.Focus();
                return;
            }

            if (dgIssues.SelectedItem is UserIssueDto selected)
            {
                var client = AIBridge.Services.CloudServiceLocator.Client;
                if (client == null) return;

                var oldCursor = this.Cursor;
                this.Cursor = System.Windows.Input.Cursors.Wait;

                try
                {
                    bool ok = await client.RespondToIssueAsync(selected.Id, reply);
                    if (ok)
                    {
                        CustomMessageBox.Show(
                            Unclassified.TxLib.Tx.T("Your reply was submitted successfully and the status has been reset to Open."),
                            Unclassified.TxLib.Tx.T("Reply Submitted"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        BrdReplyArea.Visibility = Visibility.Collapsed;
                        await RefreshListAsync();
                    }
                    else
                    {
                        CustomMessageBox.Show(
                            Unclassified.TxLib.Tx.T("Failed to submit reply on the server."),
                            Unclassified.TxLib.Tx.T("Error"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show(
                        Unclassified.TxLib.Tx.T("Network error: {0}", ex.Message),
                        Unclassified.TxLib.Tx.T("Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = oldCursor;
                }
            }
        }

        private async void BtnNewTicket_Click(object sender, RoutedEventArgs e)
        {
            var reportDialog = new ReportIssueDialog(_activeSessionId, _selectedAgentName)
            {
                Owner = this
            };

            if (reportDialog.ShowDialog() == true && reportDialog.ResultRequest != null)
            {
                var client = AIBridge.Services.CloudServiceLocator.Client;
                if (client == null) return;

                var oldCursor = this.Cursor;
                this.Cursor = System.Windows.Input.Cursors.Wait;

                try
                {
                    var response = await client.SubmitIssueAsync(reportDialog.ResultRequest);
                    if (response.Success)
                    {
                        CustomMessageBox.Show(
                            Unclassified.TxLib.Tx.T("Thank you for your report! Every detail helps improve AIBridge for the community."),
                            Unclassified.TxLib.Tx.T("Feedback Submitted"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        await RefreshListAsync();
                    }
                    else
                    {
                        CustomMessageBox.Show(
                            Unclassified.TxLib.Tx.T("Failed to submit report:\n{0}", response.ErrorMessage),
                            Unclassified.TxLib.Tx.T("Error"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show(
                        Unclassified.TxLib.Tx.T("Error connecting to server:\n{0}", ex.Message),
                        Unclassified.TxLib.Tx.T("Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = oldCursor;
                }
            }
        }
    }
}
