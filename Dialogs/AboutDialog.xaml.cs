using System.Diagnostics;
using System.Windows;

namespace AIBridge.Dialogs
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            VersionTextBlock.Text = GetAppVersion();
        }

        private string GetAppVersion()
        {
            if (System.Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed")?.ToLower() == "true")
            {
                return "v" + (System.Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion") ?? "1.0.0.0");
            }
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            return version == "0.0.0.0" ? "Local Debug" : "v" + version;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void GitHubLinkButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/MarckDWN/DWN.BRIDGE");
        }

        private void DiscordLinkButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://discord.gg/45W4KDue8a");
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
