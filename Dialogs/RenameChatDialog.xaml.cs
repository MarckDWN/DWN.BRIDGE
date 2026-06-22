using System.Windows;

namespace AIBridge.Dialogs;

public partial class RenameChatDialog : Window
{
    public string NewName { get; private set; } = string.Empty;

    public RenameChatDialog(string currentName)
    {
        InitializeComponent();
        NameTextBox.Text = currentName;
        NameTextBox.SelectAll();
        NameTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var trimmed = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        NewName = trimmed;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
