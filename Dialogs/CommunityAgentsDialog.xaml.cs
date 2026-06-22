using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using AIBridge.Services;
using AIBridge.Shared.Models;
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
            try
            {
                if (CloudServiceLocator.Client?.IsConnected == true)
                {
                    var agents = await CloudServiceLocator.Client.GetCommunityAgentsAsync();
                    
                    // Mostriamo solo quelli privati o in attesa di approvazione per l'utente corrente
                    var myAgents = agents.Where(a => a.Status == "Private" || a.Status == "PendingApproval").ToList();
                    
                    foreach (var a in myAgents)
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
                string customAgentsDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DWN.Bridge", "CustomAgents");

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
    }
}
