using System.Diagnostics;
using System.Windows;

namespace AIBridge.Dialogs
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
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
