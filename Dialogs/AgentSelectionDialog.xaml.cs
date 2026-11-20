using System.Windows;

namespace AIBridge.Dialogs
{
    public partial class AgentSelectionDialog : Window
    {
        public bool DontShowAgain
        {
            get => DontShowAgainCheckBox.IsChecked ?? false;
            set => DontShowAgainCheckBox.IsChecked = value;
        }

        public AgentSelectionDialog()
        {
            InitializeComponent();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
