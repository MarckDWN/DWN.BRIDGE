using System.Windows;

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
}
