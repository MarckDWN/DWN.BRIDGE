using System.Windows;
using Unclassified.TxLib;

namespace AIBridge.Dialogs;

/// <summary>
/// Risultato della richiesta di approvazione comando.
/// </summary>
public enum CommandApprovalResult
{
    /// <summary>L'utente nega l'esecuzione. Il loop si interrompe.</summary>
    Deny,
    /// <summary>Esegui solo questa volta. Il prossimo RUN_COMMAND chiederà di nuovo.</summary>
    ExecuteOnce,
    /// <summary>Esegui sempre questo specifico comando in questa directory senza chiedere più.</summary>
    ExecuteAlways
}

/// <summary>
/// Dialog modale che richiede all'utente di approvare o rifiutare un comando
/// generato dall'agente AI prima che venga eseguito.
/// </summary>
public partial class CommandApprovalDialog : Window
{
    public CommandApprovalResult Result { get; private set; } = CommandApprovalResult.Deny;

    public CommandApprovalDialog(string command, string workspaceRoot)
    {
        InitializeComponent();
        CommandTextBlock.Text = command;
        WorkspaceTextBlock.Text = $"📂 Directory: {workspaceRoot}";
    }

    private void AlwaysButton_Click(object sender, RoutedEventArgs e)
    {
        Result = CommandApprovalResult.ExecuteAlways;
        DialogResult = true;
    }

    private void OnceButton_Click(object sender, RoutedEventArgs e)
    {
        Result = CommandApprovalResult.ExecuteOnce;
        DialogResult = true;
    }

    private void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        Result = CommandApprovalResult.Deny;
        DialogResult = false;
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        string title = Tx.T("Command Approval Options");
        string intro = Tx.T("Here is an explanation of the available choices:");
        string denyDesc = Tx.T("Deny: The current command execution is rejected. The agent loop is interrupted.");
        string onceDesc = Tx.T("Execute Once: The current command runs once. The next RUN_COMMAND will request authorization again.");
        string alwaysDesc = Tx.T("Execute Always: Allows this exact command in this specific folder without asking again, but ONLY until the application is closed. These permissions do not persist across app restarts.");

        string message = $"{intro}\n\n• {denyDesc}\n\n• {onceDesc}\n\n• {alwaysDesc}";

        CustomMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
