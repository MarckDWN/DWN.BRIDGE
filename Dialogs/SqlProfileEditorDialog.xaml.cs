using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Unclassified.TxLib;
using AIBridge.Models;
using AIBridge.Services;

namespace AIBridge.Dialogs
{
    public partial class SqlProfileEditorDialog : Window
    {
        private string _workspaceRoot;
        private List<SqlProfile> _profiles;
        private SqlProfile? _currentProfile;

        public SqlProfileEditorDialog(string workspaceRoot, IEnumerable<SqlProfile> existingProfiles)
        {
            InitializeComponent();
            _workspaceRoot = workspaceRoot;
            
            // Lavoriamo su copie per non sporcare la memoria finché non salviamo su disco
            _profiles = existingProfiles.Select(p => new SqlProfile
            {
                Name = p.Name,
                FilePath = p.FilePath,
                Provider = p.Provider,
                ConnectionString = p.ConnectionString,
                SchemaFile = p.SchemaFile,
                SkillFile = p.SkillFile
            }).ToList();

            ProfilesListBox.ItemsSource = _profiles;
            if (_profiles.Count > 0)
            {
                ProfilesListBox.SelectedIndex = 0;
            }
        }

        private void ProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentProfile = ProfilesListBox.SelectedItem as SqlProfile;
            
            if (_currentProfile != null)
            {
                EditorPanel.IsEnabled = true;
                NameTextBox.Text = _currentProfile.Name;
                
                // Select provider
                ProviderComboBox.SelectedItem = ProviderComboBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i => i.Content.ToString()?.ToLower() == _currentProfile.Provider?.ToLower());
                
                if (ProviderComboBox.SelectedItem == null && !string.IsNullOrEmpty(_currentProfile.Provider))
                {
                    // Fallback se il provider non è in lista
                    var customItem = new ComboBoxItem { Content = _currentProfile.Provider };
                    ProviderComboBox.Items.Add(customItem);
                    ProviderComboBox.SelectedItem = customItem;
                }

                ConnectionStringTextBox.Text = _currentProfile.ConnectionString;
                SchemaFileTextBox.Text = _currentProfile.SchemaFile;
                SkillFileTextBox.Text = _currentProfile.SkillFile;
            }
            else
            {
                EditorPanel.IsEnabled = false;
                NameTextBox.Text = "";
                ProviderComboBox.SelectedIndex = -1;
                ConnectionStringTextBox.Text = "";
                SchemaFileTextBox.Text = "";
                SkillFileTextBox.Text = "";
            }
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string provider = item.Content.ToString()!.ToLower();
                
                // Ignora l'elemento placeholder
                if (provider == Tx.T("<Choose database type>").ToLower() || !item.IsEnabled)
                    return;

                bool isNewUnsaved = _currentProfile != null && string.IsNullOrEmpty(_currentProfile.FilePath);
                bool connectionEmpty = string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text);

                if (isNewUnsaved || connectionEmpty)
                {
                    ConnectionStringTextBox.Text = GetDefaultConnectionString(provider);
                }
            }
        }

        private string GetDefaultConnectionString(string provider)
        {
            return provider switch
            {
                "sqlserver" => "Server=<insert server address here>;Database=<insert database name here>;User Id=<insert username here>;Password=<insert password here>;TrustServerCertificate=True;",
                "mysql" => "Server=<insert server address here>;Database=<insert database name here>;Uid=<insert username here>;Pwd=<insert password here>;",
                "postgresql" => "Host=<insert server address here>;Database=<insert database name here>;Username=<insert username here>;Password=<insert password here>;",
                "sqlite" => "Data Source=<insert path to .db or .sqlite file here>;Version=3;",
                "excel" => "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=<insert path to .xls or .xlsx here>;Extended Properties=\"Excel 12.0 Xml;HDR=YES;IMEX=1\";",
                "oledb" => "Provider=<insert OLEDB provider here>;Data Source=<insert data source here>;",
                "odbc" => "Driver={<insert driver name here>};Server=<insert server here>;Database=<insert database here>;Uid=<insert username here>;Pwd=<insert password here>;",
                "db2" => "Driver={IBM DB2 ODBC DRIVER};Database=<insert database here>;Hostname=<insert server here>;Port=50000;Protocol=TCPIP;Uid=<insert username here>;Pwd=<insert password here>;",
                "oracle" => "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=<insert server here>)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=<insert service name here>)));User Id=<insert username here>;Password=<insert password here>;",
                _ => ""
            };
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            // Profilo nuovo: non ha ancora un FilePath (usato come flag per "non salvato")
            var newProfile = new SqlProfile
            {
                Name = "NewProfile",
                Provider = "",          // vuoto = nessun provider scelto ancora
                ConnectionString = "",
                FilePath = ""           // vuoto = non ancora salvato su disco
            };
            
            _profiles.Add(newProfile);
            ProfilesListBox.Items.Refresh();
            ProfilesListBox.SelectedItem = newProfile;
            
            // Seleziona il placeholder nel ComboBox
            ProviderComboBox.SelectedIndex = 0;
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfile != null)
            {
                var result = AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Are you sure you want to delete the profile '{0}'?", _currentProfile.Name), Tx.T("Confirm deletion"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    // Se esiste un file su disco, cancellalo
                    if (!string.IsNullOrEmpty(_currentProfile.FilePath) && File.Exists(_currentProfile.FilePath))
                    {
                        try
                        {
                            File.Delete(_currentProfile.FilePath);
                        }
                        catch (Exception ex)
                        {
                            AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error while deleting the file: {0}", ex.Message), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }

                    _profiles.Remove(_currentProfile);
                    ProfilesListBox.Items.Refresh();
                    if (_profiles.Count > 0)
                        ProfilesListBox.SelectedIndex = 0;
                    else
                        _currentProfile = null;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfile == null) return;

            string oldName = _currentProfile.Name;
            string newName = NameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(newName))
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("The profile name cannot be empty."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verifica che sia stato scelto un provider valido (non il placeholder)
            var selectedProviderItem = ProviderComboBox.SelectedItem as ComboBoxItem;
            string selectedProvider = selectedProviderItem?.IsEnabled == true ? (selectedProviderItem.Content?.ToString() ?? "") : "";
            if (string.IsNullOrEmpty(selectedProvider))
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Please select a database provider before saving."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Se stiamo cambiando nome, assicuriamoci che non esista già
            if (oldName != newName && _profiles.Any(p => p != _currentProfile && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("A profile with this name already exists."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetPath = Path.Combine(_workspaceRoot, newName + ".sqlprofile");

            // Aggiorniamo il profilo in memoria
            _currentProfile.Name = newName;
            _currentProfile.Provider = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            _currentProfile.ConnectionString = ConnectionStringTextBox.Text.Trim();
            _currentProfile.SchemaFile = SchemaFileTextBox.Text.Trim();
            _currentProfile.SkillFile = SkillFileTextBox.Text.Trim();

            // Rimuoviamo il vecchio file se il nome è cambiato e il vecchio file esisteva
            if (oldName != newName && !string.IsNullOrEmpty(_currentProfile.FilePath) && File.Exists(_currentProfile.FilePath))
            {
                try
                {
                    File.Delete(_currentProfile.FilePath);
                }
                catch (Exception ex)
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Unable to rename/delete the old file: {0}", ex.Message), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            _currentProfile.FilePath = targetPath;

            // Salvataggio JSON
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                // Cifra la ConnectionString con DPAPI prima di scrivere su disco
                var dto = new
                {
                    Provider = _currentProfile.Provider,
                    ConnectionString = DpapiHelper.Protect(_currentProfile.ConnectionString),
                    SchemaFile = _currentProfile.SchemaFile,
                    SkillFile = _currentProfile.SkillFile
                };
                
                string json = JsonSerializer.Serialize(dto, options);
                File.WriteAllText(targetPath, json);
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error during save: {0}", ex.Message), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ProfilesListBox.Items.Refresh();
            AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Profile saved successfully!"), Tx.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Chiudiamo dicendo che abbiamo fatto modifiche
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
