using CommunityToolkit.Mvvm.ComponentModel;

namespace AIBridge.Models
{
    public partial class ChatMessage : ObservableObject
    {
        [ObservableProperty]
        private string _sender = string.Empty;

        [ObservableProperty]
        private string _text = string.Empty;

        [ObservableProperty]
        private bool _isUser;
        
        [ObservableProperty]
        private bool _isCollapsible;

        [ObservableProperty]
        private bool _isExpanded;
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasFile))]
        private string _filePathToOpen = string.Empty;

        public bool HasFile => !string.IsNullOrEmpty(FilePathToOpen);
        
        [ObservableProperty]
        private bool _hasBackup;
        
        [ObservableProperty]
        private bool _isRejected;

        [ObservableProperty]
        private bool _isSqlResult;

        [ObservableProperty]
        private string _formattedTableText = string.Empty;

        [ObservableProperty]
        private string _tsvData = string.Empty;

        // --- Proprietà per messaggi agentici (Tool Execution) ---
        [ObservableProperty]
        private bool _isToolMessage;

        [ObservableProperty]
        private string _toolName = string.Empty;

        [ObservableProperty]
        private string _toolInput = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsToolExpanded))]
        private string _toolOutput = string.Empty;

        [ObservableProperty]
        private bool _isToolExpanded;

        // Colore badge del tool in base al nome dell'azione
        public string ToolBadgeColor => ToolName?.ToUpper() switch
        {
            "READ_FILE"      => "#3498DB",
            "LIST_DIR"       => "#9B59B6",
            "GREP_SEARCH"    => "#E67E22",
            "RUN_COMMAND"    => "#E74C3C",
            "WRITE_FILE"     => "#27AE60",
            "REPLACE_IN_FILE"=> "#1ABC9C",
            _                => "#4A90E2"
        };

        // Colore di background in base a chi scrive
        public string BackgroundColor => IsUser ? "#2C3E50" : "#34495E";
        
        // Allineamento nella griglia
        public string Alignment => IsUser ? "Right" : "Left";
    }
}
