using System.Windows;

namespace AIBridge.Dialogs
{
    public partial class TrainingResultDialog : Window
    {
        public string ResultText { get; private set; } = string.Empty;

        public TrainingResultDialog(string initialText)
        {
            InitializeComponent();
            ResultTextBox.Text = initialText;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ResultText = ResultTextBox.Text;
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
