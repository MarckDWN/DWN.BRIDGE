using System.Windows;

namespace AIBridge.Dialogs
{
    public partial class TrainingSelectionDialog : Window
    {
        public string SelectedText { get; private set; } = string.Empty;

        public TrainingSelectionDialog(string initialText)
        {
            InitializeComponent();
            SelectedTextBox.Text = initialText;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedText = SelectedTextBox.Text;
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
