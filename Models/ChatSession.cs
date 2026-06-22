using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace AIBridge.Models
{
    /// <summary>
    /// Rappresenta una sessione di chat salvata (sidebar di cronologia).
    /// </summary>
    public partial class ChatSession : ObservableObject
    {
        /// <summary>ID univoco generato dall'adapter (es. Server REST o plugin locale).</summary>
        [ObservableProperty]
        private Guid _serverId;

        /// <summary>ID univoco estratto dall'URL di Gemini (es. 77a046843309c53f).</summary>
        [ObservableProperty]
        private string _geminiId = string.Empty;

        /// <summary>Descrizione visibile in cronologia.</summary>
        [ObservableProperty]
        private string _description = string.Empty;

        /// <summary>RoleType dell'agente attivo quando è stata avviata la chat.</summary>
        [ObservableProperty]
        private string _agentRoleType = string.Empty;

        /// <summary>Nome dell'agente (per la label).</summary>
        [ObservableProperty]
        private string _agentName = string.Empty;

        /// <summary>Percorso del workspace associato alla sessione.</summary>
        [ObservableProperty]
        private string _workspacePath = string.Empty;

        /// <summary>Percorso del file .sqlprofile selezionato per la sessione (se pertinente).</summary>
        [ObservableProperty]
        private string _selectedSqlProfilePath = string.Empty;

        /// <summary>Data/ora di creazione.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>URL completo della chat Gemini.</summary>
        public string GeminiUrl => string.IsNullOrEmpty(GeminiId)
            ? "https://gemini.google.com/app"
            : $"https://gemini.google.com/app/{GeminiId}";

        /// <summary>True se la chat è quella attualmente attiva.</summary>
        [ObservableProperty]
        [property: JsonIgnore]   // [property:] forza l'attributo sulla property generata (non sul campo)
        private bool _isActive;

        /// <summary>Messaggi serializzati della chat.</summary>
        public List<SerializableChatMessage> Messages { get; set; } = new();

        [JsonIgnore]
        public string StorageIcon { get; set; } = "🌐";

        [JsonIgnore]
        public string StorageIconColor { get; set; } = "#00E5FF";

        /// <summary>Header formattato per la lista cronologia.</summary>
        [JsonIgnore]
        public string DisplayHeader => $"{AgentName}  ·  {CreatedAt:dd/MM HH:mm}";

        private const int MaxDescriptionDisplay = 80;

        [JsonIgnore]
        public string DisplayDescription
        {
            get
            {
                var raw = string.IsNullOrEmpty(Description)
                    ? $"Nuova Chat · {AgentName}"
                    : Description;
                // Sicurezza: tronca sempre, indipendentemente da come è stato salvato
                return raw.Length > MaxDescriptionDisplay
                    ? raw[..MaxDescriptionDisplay] + "…"
                    : raw;
            }
        }

        /// <summary>
        /// Da chiamare dopo aver modificato Description manualmente,
        /// per forzare il refresh di DisplayDescription nella UI.
        /// </summary>
        public void RaiseDescriptionChanged()
        {
            OnPropertyChanged(nameof(DisplayDescription));
            OnPropertyChanged(nameof(DisplayHeader));
        }
    }

    /// <summary>
    /// Versione serializzabile di ChatMessage (senza ObservableObject per JSON).
    /// </summary>
    public class SerializableChatMessage
    {
        public string Sender { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool IsUser { get; set; }
        public bool IsCollapsible { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsSqlResult { get; set; }
        public string FormattedTableText { get; set; } = string.Empty;
        public string TsvData { get; set; } = string.Empty;
        public string FilePathToOpen { get; set; } = string.Empty;
        public bool HasBackup { get; set; }

        // properties for tool messages
        public bool IsToolMessage { get; set; }
        public string ToolName { get; set; } = string.Empty;
        public string ToolInput { get; set; } = string.Empty;
        public string ToolOutput { get; set; } = string.Empty;
        public bool IsToolExpanded { get; set; }
    }
}
