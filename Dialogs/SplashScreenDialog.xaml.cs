using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using AIBridge.Services;
using Unclassified.TxLib;

namespace AIBridge.Dialogs
{
    public partial class SplashScreenDialog : Window
    {
        private readonly string _hwid;
        private readonly SettingsService _settingsService;
        private bool _isInitializing;

        public ObservableCollection<Models.LanguageOption> AvailableLanguages { get; } = new();

        public SplashScreenDialog(string hwid)
        {
            InitializeComponent();
            _hwid = hwid;
            _settingsService = new SettingsService();

            InitLanguages();

            Loaded += SplashScreenDialog_Loaded;
        }

        private void InitLanguages()
        {
            _isInitializing = true;
            AvailableLanguages.Clear();
            AvailableLanguages.Add(new Models.LanguageOption { Code = "en-US", Display = "🇬🇧 English" });

            try
            {
                string txdPath = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "localization", "languages.txd");

                if (System.IO.File.Exists(txdPath))
                {
                    var xdoc = XDocument.Load(txdPath);
                    var cultures = xdoc.Descendants("culture")
                        .Select(c => c.Attribute("name")?.Value)
                        .Where(c => !string.IsNullOrEmpty(c) && c != "en-US" && c != "en-GB")
                        .Distinct()
                        .ToList();

                    foreach (var code in cultures)
                    {
                        string display = code switch
                        {
                            "it-IT" => "🇮🇹 Italiano",
                            "fr-FR" => "🇫🇷 Français",
                            "de-DE" => "🇩🇪 Deutsch",
                            "es-ES" => "🇪🇸 Español",
                            "pt-PT" => "🇵🇹 Português",
                            _ => $"🌐 {code}"
                        };
                        AvailableLanguages.Add(new Models.LanguageOption { Code = code!, Display = display });
                    }
                }
            }
            catch { }

            LanguageComboBox.ItemsSource = AvailableLanguages;

            // Select the saved language — Tx culture is already set by App.OnStartup
            var settings = _settingsService.Load();
            string savedCode = string.IsNullOrEmpty(settings.Language) ? "en-US" : settings.Language;
            var savedLang = AvailableLanguages.FirstOrDefault(l => l.Code == savedCode) ?? AvailableLanguages[0];

            LanguageComboBox.SelectedItem = savedLang;
            _isInitializing = false;
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || LanguageComboBox.SelectedItem is not Models.LanguageOption selected) return;

            // Update culture — Tx fires DictionaryChanged which refreshes all {tx:T} bindings in XAML
            var culture = System.Globalization.CultureInfo.GetCultureInfo(selected.Code);
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            Tx.SetCulture(selected.Code);

            // Persist the choice
            var settings = _settingsService.Load();
            if (settings.Language != selected.Code)
            {
                settings.Language = selected.Code;
                _settingsService.Save(settings);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void SplashScreenDialog_Loaded(object sender, RoutedEventArgs e)
        {
            var client = CloudServiceLocator.Client;
            if (client != null && client.HasValidSavedToken())
            {
                LastUserButton.IsEnabled = true;
            }
        }

        private void LastUserButton_Click(object sender, RoutedEventArgs e)
        {
            var client = CloudServiceLocator.Client;
            if (client != null && client.AutoLogin())
            {
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Unable to restore previous session. Please log in again."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                LastUserButton.IsEnabled = false;
            }
        }

        private async void MagicLinkButton_Click(object sender, RoutedEventArgs e)
        {
            var client = CloudServiceLocator.Client;
            if (client == null)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Server unavailable. Offline mode active."), Tx.T("Offline"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new MagicLinkLoginDialog(client);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Token))
            {
                // Accesso Guest prima, così recuperiamo l'account basato su HWID (con l'eventuale cronologia)
                await client.LoginGuestAsync(_hwid);
                // Poi facciamo il merge del Guest dentro l'account MagicLink
                bool linked = await client.LinkAccountAsync(dialog.Token);
                
                if (linked)
                {
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error during account link/fusion."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            var client = CloudServiceLocator.Client;
            if (client == null)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Server unavailable. Offline mode active."), Tx.T("Offline"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var startRes = await client.StartOAuthAsync("github");
                if (startRes == null || string.IsNullOrEmpty(startRes.AuthorizationUrl))
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error starting GitHub login flow."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dialog = new OAuthLoginDialog(startRes.AuthorizationUrl) { Owner = this };
                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Code) && !string.IsNullOrEmpty(dialog.State))
                {
                    // Se lo State non corrisponde a quello ritornato, potremmo rigettarlo, ma il server lo controllerà.
                    var (token, error) = await client.OAuthCallbackAsync("github", dialog.Code, dialog.State);

                    if (token != null)
                    {
                        // Accesso Guest prima, così recuperiamo l'account basato su HWID (con l'eventuale cronologia)
                        await client.LoginGuestAsync(_hwid);
                        // Fonde l'utente Guest con l'account GitHub
                        bool linked = await client.LinkAccountAsync(token);
                        
                        if (linked)
                        {
                            this.DialogResult = true;
                            this.Close();
                        }
                        else
                        {
                            AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error during account link/fusion with GitHub."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Login failed: ") + error, Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Login error: ") + ex.Message, Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GuestButton_Click(object sender, RoutedEventArgs e)
        {
            var result = AIBridge.Dialogs.CustomMessageBox.Show(
                Tx.T("Warning: by logging in as Guest your settings will not be saved to the cloud and your conversations will be lost when closing the application.") + "\n\n" +
                Tx.T("Do you want to proceed as Guest?"),
                Tx.T("Guest Access"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var client = CloudServiceLocator.Client;
                if (client != null)
                {
                    await client.LoginGuestAsync(_hwid);
                }
                this.DialogResult = true;
                this.Close();
            }
        }
    }
}
