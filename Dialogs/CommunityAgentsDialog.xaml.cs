using AIBridge.Services;
using AIBridge.Shared.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Unclassified.TxLib;

namespace AIBridge.Dialogs
{
    public class SelectableAgent
    {
        public bool IsSelected { get; set; }
        public string AgentKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public partial class CommunityAgentsDialog : Window
    {
        public ObservableCollection<SelectableAgent> AgentsList { get; set; } = new();

        public CommunityAgentsDialog()
        {
            InitializeComponent();
            AgentsListBox.ItemsSource = AgentsList;
            Loaded += CommunityAgentsDialog_Loaded;
        }

        private async void CommunityAgentsDialog_Loaded(object sender, RoutedEventArgs e)
        {
            if (CloudServiceLocator.Client?.AuthMode == "guest")
            {
                FilterComboBox.SelectedIndex = 1;
                FilterComboBox.IsEnabled = false;
                OpenFolderButton.Visibility = Visibility.Collapsed;
                SubmitButton.Visibility = Visibility.Collapsed;
            }
            await RefreshListAsync();
        }

        private async System.Threading.Tasks.Task RefreshListAsync()
        {
            try
            {
                if (CloudServiceLocator.Client?.IsConnected == true)
                {
                    if (Application.Current.MainWindow?.DataContext is AIBridge.ViewModels.MainViewModel vm)
                    {
                        await vm.SyncLocalCustomAgentsAsync();
                    }

                    var agents = await CloudServiceLocator.Client.GetCommunityAgentsAsync();

                    AgentsList.Clear();

                    bool isCommunity = FilterComboBox.SelectedIndex == 1;
                    List<CommunityAgentDto> filteredAgents;

                    if (isCommunity)
                    {
                        TitleLabel.Text = Tx.T("Actual approved community agents:");
                        filteredAgents = agents.Where(a => a.Status == "Approved").ToList();
                    }
                    else
                    {
                        TitleLabel.Text = Tx.T("Select agents to submit for approval:");
                        filteredAgents = agents.Where(a => a.Status == "Private" || a.Status == "PendingApproval").ToList();
                    }

                    foreach (var a in filteredAgents)
                    {
                        AgentsList.Add(new SelectableAgent
                        {
                            AgentKey = a.AgentKey,
                            DisplayName = a.DisplayName,
                            Icon = string.IsNullOrEmpty(a.Icon) ? "🌐" : a.Icon,
                            Status = a.Status
                        });
                    }

                    if (AgentsList.Count == 0)
                    {
                        // Messaggio se non ci sono agenti locali
                        AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("No local agents found. Click 'Open Local Folder' to create your first .md agent!"), Tx.T("Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("You must be connected to the AIBridge Cloud to submit agents."), Tx.T("Offline"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                }
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(ex.Message, Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string customAgentsDir = CloudServiceLocator.CustomAgentsDir;

                if (!System.IO.Directory.Exists(customAgentsDir))
                {
                    System.IO.Directory.CreateDirectory(customAgentsDir);
                }

                Process.Start("explorer.exe", customAgentsDir);
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(ex.Message, Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedKeys = AgentsList.Where(a => a.IsSelected && a.Status == "Private").Select(a => a.AgentKey).ToList();

            if (selectedKeys.Count == 0)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Please select at least one 'Private' agent to submit."), Tx.T("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CloudServiceLocator.Client?.IsConnected == true)
            {
                bool success = await CloudServiceLocator.Client.SubmitCommunityAgentsAsync(selectedKeys);
                if (success)
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Agents submitted successfully! They will be reviewed by the community admins."), Tx.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
                else
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error submitting agents. Please try again later."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/MarckDWN/DWN.BRIDGE",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.gg/45W4KDue8a",
                    UseShellExecute = true
                });
            }
            catch { }
        }
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshListAsync();
        }

        private async void FilterComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                await RefreshListAsync();
            }
        }

        private async void ViewScriptButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SelectableAgent agent)
            {
                try
                {
                    bool isCustom = FilterComboBox.SelectedIndex == 0;
                    string localFilePath = "";
                    string scriptContent = "";

                    if (isCustom)
                    {
                        string customAgentsDir = CloudServiceLocator.CustomAgentsDir;

                        // Cerca di indovinare il file locale in base al DisplayName
                        string expectedFileName = agent.DisplayName.Replace(" ", "_") + ".md";
                        localFilePath = System.IO.Path.Combine(customAgentsDir, expectedFileName);

                        // Fallback: cerca qualsiasi file .md
                        if (!System.IO.File.Exists(localFilePath))
                        {
                            var files = System.IO.Directory.GetFiles(customAgentsDir, "*.md");
                            foreach (var f in files)
                            {
                                string name = System.IO.Path.GetFileNameWithoutExtension(f);
                                if (agent.AgentKey.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                                {
                                    localFilePath = f;
                                    break;
                                }
                            }
                        }

                        if (System.IO.File.Exists(localFilePath))
                        {
                            scriptContent = await System.IO.File.ReadAllTextAsync(localFilePath);
                        }
                    }

                    if (string.IsNullOrEmpty(scriptContent))
                    {
                        // Usa l'orchestratore per recuperare il template dal server
                        scriptContent = await CloudServiceLocator.Orchestrator.BuildPromptAsync(agent.AgentKey, "");
                    }

                    var viewer = new ViewScriptWindow(agent.DisplayName, scriptContent, isCustom, localFilePath)
                    {
                        Owner = this
                    };
                    viewer.ShowDialog();

                    // Sincronizza subito sul server le modifiche locali alla chiusura della finestra di preview
                    if (isCustom && CloudServiceLocator.Client?.IsConnected == true)
                    {
                        if (Application.Current.MainWindow?.DataContext is AIBridge.ViewModels.MainViewModel vm)
                        {
                            await vm.SyncLocalCustomAgentsAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error retrieving script: {0}", ex.Message), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteAgentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SelectableAgent agent)
            {
                var result = AIBridge.Dialogs.CustomMessageBox.Show(
                    Tx.T("Are you sure you want to delete this custom agent from the server?") + "\n" +
                    Tx.T("This will also move your local agent file to the 'removed' subfolder to deactivate it."),
                    Tx.T("Delete Agent"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                try
                {
                    bool serverDeleteSuccess = true;
                    if (CloudServiceLocator.Client?.IsConnected == true)
                    {
                        serverDeleteSuccess = await CloudServiceLocator.Client.DeleteCommunityAgentAsync(agent.AgentKey);
                    }

                    // Sposta il file locale
                    string customAgentsDir = CloudServiceLocator.CustomAgentsDir;
                    string expectedFileName = agent.DisplayName.Replace(" ", "_") + ".md";
                    string localFilePath = System.IO.Path.Combine(customAgentsDir, expectedFileName);

                    if (!System.IO.File.Exists(localFilePath))
                    {
                        var files = System.IO.Directory.GetFiles(customAgentsDir, "*.md");
                        foreach (var f in files)
                        {
                            string name = System.IO.Path.GetFileNameWithoutExtension(f);
                            if (agent.AgentKey.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                            {
                                localFilePath = f;
                                break;
                            }
                        }
                    }

                    if (System.IO.File.Exists(localFilePath))
                    {
                        string removedDir = System.IO.Path.Combine(customAgentsDir, "removed");
                        if (!System.IO.Directory.Exists(removedDir))
                        {
                            System.IO.Directory.CreateDirectory(removedDir);
                        }

                        string destPath = System.IO.Path.Combine(removedDir, System.IO.Path.GetFileName(localFilePath));
                        System.IO.File.Move(localFilePath, destPath, overwrite: true);
                    }

                    if (serverDeleteSuccess)
                    {
                        AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Agent successfully deleted and local file moved to 'removed' folder."), Tx.T("Deleted"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Local file moved to 'removed', but server deletion failed. The server agent could not be found or you do not have permission."), Tx.T("Partial Delete"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    await RefreshListAsync();
                }
                catch (Exception ex)
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error deleting agent: {0}", ex.Message), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
