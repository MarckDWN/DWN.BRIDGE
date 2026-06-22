using AIBridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using AIBridge.AgentCore;
using AIBridge.Models;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using Unclassified.TxLib;

namespace AIBridge.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly GeminiBrowserService _geminiService;

        [ObservableProperty]
        private string _inputText = string.Empty;

        [ObservableProperty]
        private bool _isLocalMode;

        [ObservableProperty]
        private string _localModeBannerText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAnyBusy))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = Tx.T("Ready. Click 'Start Browser' to begin.");

        [ObservableProperty]
        private string _authStatusText = Tx.T("Guest Mode");

        public ObservableCollection<string> AttachedFiles { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSqlAgentSelected))]
        private AgentProfile? _selectedAgent;

        public bool IsSqlAgentSelected => SelectedAgent?.RoleType == "SqlAnalyst";

        public ObservableCollection<SqlProfile> AvailableSqlProfiles { get; } = new();

        [ObservableProperty]
        private SqlProfile? _selectedSqlProfile;

        public bool HasSqlProfiles => AvailableSqlProfiles.Count > 0;

        [ObservableProperty]
        private string _workspaceRoot = string.Empty;

        private LocalDirectoryWatcher? _directoryWatcher;

        [ObservableProperty]
        private bool _isWatcherRunning;

        public ObservableCollection<ChatMessage> ChatMessages { get; } = new();
        public ObservableCollection<AgentProfile> AvailableAgents { get; } = new();
        public ObservableCollection<FileTreeNode> WorkspaceNodes { get; } = new();

        /// <summary>Log voci di rete per l'audit di privacy. Binding per l'Expander Network Audit.</summary>
        public System.Collections.ObjectModel.ObservableCollection<AIBridge.Services.AuditEntry> AuditEntries
            => CloudServiceLocator.Audit.Entries;

        private bool _isCurrentlyStreaming;
        private string _lastInjectedRole = string.Empty;
        private bool _isInternalProfileChange;
        private int _sqlErrorRetryCount = 0;
        /// <summary>True durante navigazione Playwright: sopprime l'evento OnMessageReceivedPartial.</summary>
        private bool _isNavigating;
        private bool _suppressGlobalWatcherHandler;
        private int _currentToolLoop = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAnyBusy))]
        [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
        private bool _isSqlBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAnyBusy))]
        [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
        private bool _isGeminiBusy;

        [ObservableProperty]
        private bool _isTrainingMode;

        /// <summary>True when either a navigation/load op (IsBusy) or Gemini is generating (IsGeminiBusy) or SQL executing.</summary>
        public bool IsAnyBusy => IsBusy || IsGeminiBusy || IsSqlBusy;

        // --- Cronologia Chat ---
        private readonly Services.SettingsService _settingsService = new();
        public ObservableCollection<ChatSession> ChatHistory { get; } = new();

        [ObservableProperty]
        private ChatSession? _activeSession;

        // --- Language selector ---
        public ObservableCollection<Models.LanguageOption> AvailableLanguages { get; } = new();

        [ObservableProperty]
        private Models.LanguageOption? _selectedLanguage;

        private bool _isLeftPanelCollapsed;
        public bool IsLeftPanelCollapsed
        {
            get => _isLeftPanelCollapsed;
            set
            {
                if (SetProperty(ref _isLeftPanelCollapsed, value))
                {
                    OnPropertyChanged(nameof(LeftPanelWidth));
                }
            }
        }

        public System.Windows.GridLength LeftPanelWidth => IsLeftPanelCollapsed 
            ? new System.Windows.GridLength(70) 
            : new System.Windows.GridLength(280);

        [RelayCommand]
        private void ToggleLeftPanel()
        {
            IsLeftPanelCollapsed = !IsLeftPanelCollapsed;
        }

        [RelayCommand]
        private async Task ManualGoogleLoginAsync()
        {
            await _geminiService.ManualBrowserLoginAsync();
        }

        [RelayCommand]
        private void ClearAuditLog()
        {
            CloudServiceLocator.Audit.Clear();
        }

        public async Task SyncSettingsAsync()
        {
            await _settingsService.SyncFromCloudAsync();
            var settings = _settingsService.Load();
            var savedLang = AvailableLanguages.FirstOrDefault(l => l.Code == settings.Language) 
                            ?? AvailableLanguages.FirstOrDefault(l => l.Code == "en-US");

            if (savedLang != null && SelectedLanguage?.Code != savedLang.Code)
            {
                _suppressLanguageSave = true;
                SelectedLanguage = savedLang;
                Tx.SetCulture(savedLang.Code);
                UpdateAgentTranslations();
                _suppressLanguageSave = false;
            }
        }

        public MainViewModel()
        {
            _geminiService = new GeminiBrowserService();
            _geminiService.OnError += (s, e) => 
            {
                Application.Current.Dispatcher.Invoke(() => 
                {
                    StatusMessage = e;
                });
            };

            _geminiService.OnGeminiBlocked += (s, url) =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    // Se rileviamo che Gemini è bloccato (es. schermata di login o captcha),
                    // lanciamo automaticamente la nostra finestra nativa WebView2 per il recupero sessione!
                    await ManualGoogleLoginAsync();
                });
            };

            _geminiService.OnGeminiUnblocked += (s, e) =>
            {
                // Non serve più chiudere il captcha dialog, la finestra di login si chiude da sola
            };

            _geminiService.OnChatNotFound += (s, geminiId) =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var result = AIBridge.Dialogs.CustomMessageBox.Show(
                        Tx.T("This chat was deleted or no longer exists on the Google Gemini server.\n\nDo you want to remove it from DWN.Bridge's local history too?"), 
                        Tx.T("Chat not found"), 
                        System.Windows.MessageBoxButton.YesNo, 
                        System.Windows.MessageBoxImage.Question);
                        
                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        var sessionToDel = ChatHistory.FirstOrDefault(x => x.GeminiId == geminiId);
                        if (sessionToDel != null)
                        {
                            try
                            {
                                await CloudServiceLocator.Session.RemoveAsync(sessionToDel.ServerId);
                                ChatHistory.Remove(sessionToDel);
                                if (ActiveSession == sessionToDel)
                                {
                                    ActiveSession = null;
                                    ChatMessages.Clear();
                                }
                            }
                            catch (Exception ex)
                            {
                                StatusMessage = Tx.T("Error deleting chat: {0}", ex.Message);
                            }
                        }
                    }
                });
            };

            // --- Language selector setup ---
            InitLanguages();

            UpdateAgentTranslations();

            // Inizializza Workspace
            // Usiamo la cartella Documenti di Windows per evitare di inquinare la cartella "bin/Debug"
            var docsFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            WorkspaceRoot = System.IO.Path.Combine(docsFolder, "AIBridge_Workspace");
            
            if (!System.IO.Directory.Exists(WorkspaceRoot))
            {
                System.IO.Directory.CreateDirectory(WorkspaceRoot);
            }
            RefreshWorkspaceTree();
            
            // Carica la cronologia in modo asincrono usando l'adapter
            _ = ReloadSessionsAsync();
            _ = SyncSettingsAsync();

            // Registra il callback di approvazione per RUN_COMMAND
            // Il dialog viene mostrato sul thread UI, il risultato viene restituito al servizio
            AgentToolService.ApprovalCallback = (command, execDir) =>
            {
                var dialog = new Dialogs.CommandApprovalDialog(command, execDir)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.ShowDialog();
                return dialog.Result;
            };

            // Gestione Real-Time Streaming
            _geminiService.OnMessageReceivedPartial += (s, partialText) =>
            {
                if (_isNavigating || _suppressGlobalWatcherHandler) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    string displayText = partialText;

                    // DECODIFICA LE ENTITÀ HTML PRIMA CHE IL MARKDOWN LE LEGGA!
                    displayText = System.Net.WebUtility.HtmlDecode(displayText);

                    var match = System.Text.RegularExpressions.Regex.Match(displayText, @"(\{[\s\S]*?\""action\""[\s\S]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        displayText = displayText.Substring(0, match.Index) + "\n\n" + Tx.T("[Tool running ⚙️...]");
                    }

                    if (!_isCurrentlyStreaming)
                    {
                        bool isCollapsible = SelectedAgent?.RoleType == "Coder" || SelectedAgent?.RoleType == "SqlAnalyst";
                        ChatMessages.Add(new ChatMessage { Sender = "Gemini", Text = displayText, IsUser = false, IsCollapsible = isCollapsible, IsExpanded = !isCollapsible });
                        _isCurrentlyStreaming = true;
                    }
                    else if (ChatMessages.Count > 0)
                    {
                        ChatMessages[ChatMessages.Count - 1].Text = displayText;
                    }
                });
            };

            _geminiService.OnInstallingBrowser += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = Tx.T("Downloading browser engine (first run only)...");
                });
            };

            _geminiService.OnMessageCompleted += async (s, responseText) =>
            {
                if (_isNavigating || _suppressGlobalWatcherHandler) return;
                await HandleMessageCompletedAsync(responseText);
            };

            _geminiService.OnUserPromptDetected += (s, promptText) =>
            {
                if (_isNavigating || _suppressGlobalWatcherHandler) return;
                HandleUserPromptDetected(promptText);
            };

            _geminiService.OnBusyStateChanged += (s, isBusy) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsGeminiBusy = isBusy;
                });
            };
        }

        public void UpdateAuthStatus()
        {
            var client = CloudServiceLocator.Client;
            if (client == null || !client.IsConnected)
            {
                AuthStatusText = Tx.T("Offline");
            }
            else if (client.AuthMode == "oauth")
            {
                string disp = string.IsNullOrEmpty(client.DisplayName) ? Tx.T("Pro User") : client.DisplayName;
                AuthStatusText = $"GitHub: {disp}";
            }
            else if (client.AuthMode == "email")
            {
                string disp = string.IsNullOrEmpty(client.DisplayName) ? Tx.T("Pro User") : client.DisplayName;
                AuthStatusText = $"Magic Link: {disp}";
            }
            else
            {
                AuthStatusText = Tx.T("Guest Mode");
            }
        }

        public async Task ReloadSessionsAsync()
        {
            try
            {
                var sessions = await CloudServiceLocator.Session.GetSessionsAsync();
                
                string currentIcon = CloudServiceLocator.Session.GetType().Name.Contains("Local") ? "💻" : "🌐";
                string currentColor = CloudServiceLocator.Session.GetType().Name.Contains("Local") ? "#4DA6FF" : "#00E5FF";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ChatHistory.Clear();
                    foreach (var s in sessions)
                    {
                        s.StorageIcon = currentIcon;
                        s.StorageIconColor = currentColor;
                        ChatHistory.Add(s);
                    }
                });
            }
            catch { /* fallback silently */ }
        }

        [RelayCommand]
        private async Task StartBrowserAsync()
        {
            if (_geminiService.IsInitialized)
            {
                // Browser già avviato: lo portiamo semplicemente in primo piano
                IsBusy = true;
                StatusMessage = Tx.T("Browser already active. Bringing to front...");
                await _geminiService.InitializeAsync(); // Questo fa solo il BringToFront se è già aperto
                StatusMessage = Tx.T("Browser in front.");
                IsBusy = false;
            }
            else
            {
                // Browser non avviato / nessuna sessione attiva: iniziamo una Nuova Chat
                await NewChatAsync();
            }
        }

        /// <summary>
        /// Ripristina la chat nel browser Gemini e carica i messaggi nella UI AIBridge.
        /// Non ri-naviga se la sessione è già quella attiva.
        /// </summary>
        private async Task RestoreActiveChatInBrowserAsync(ChatSession? session = null)
        {
            session ??= ActiveSession ?? ChatHistory.FirstOrDefault();
            if (session == null) return;

            // 1. Naviga Gemini alla chat
            if (!string.IsNullOrEmpty(session.GeminiId))
            {
                _isNavigating = true;
                _isCurrentlyStreaming = false;
                try { await _geminiService.NavigateToChatAsync(session.GeminiId); }
                finally { _isNavigating = false; }
            }

            // 2. Se non era già la sessione attiva, carica i messaggi nell'app
            if (ActiveSession != session)
            {
                ChatMessages.Clear();
                foreach (var sm in session.Messages)
                {
                    ChatMessages.Add(new ChatMessage
                    {
                        Sender = sm.Sender,
                        Text = sm.Text,
                        IsUser = sm.IsUser,
                        IsCollapsible = sm.IsCollapsible,
                        IsExpanded = sm.IsExpanded,
                        IsSqlResult = sm.IsSqlResult,
                        FormattedTableText = sm.FormattedTableText,
                        TsvData = sm.TsvData,
                        FilePathToOpen = sm.FilePathToOpen,
                        HasBackup = sm.HasBackup
                    });
                }

                _lastInjectedRole = string.Empty;

                var matchingAgent = AvailableAgents.FirstOrDefault(a => a.RoleType == session.AgentRoleType);
                if (matchingAgent != null)
                    SelectedAgent = matchingAgent;

                if (!string.IsNullOrEmpty(session.WorkspacePath))
                {
                    WorkspaceRoot = session.WorkspacePath;
                    RefreshWorkspaceTree();
                }

                foreach (var s in ChatHistory) s.IsActive = (s == session);
                ActiveSession = session;
            }
        }

        [RelayCommand]
        private void SelectFile()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var file in openFileDialog.FileNames)
                {
                    if (!AttachedFiles.Contains(file))
                    {
                        AttachedFiles.Add(file);
                    }
                }
                StatusMessage = Tx.T("{0} files attached.", AttachedFiles.Count);
            }
        }

        [RelayCommand]
        private void RemoveAttachedFile(string file)
        {
            if (AttachedFiles.Contains(file))
            {
                AttachedFiles.Remove(file);
                StatusMessage = AttachedFiles.Count > 0 ? Tx.T("{0} files attached.", AttachedFiles.Count) : Tx.T("Ready.");
            }
        }

        [RelayCommand]
        private void CopyMessage(Models.ChatMessage msg)
        {
            if (msg == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{msg.Sender}]:");
            sb.AppendLine(msg.Text);
            if (msg.IsSqlResult && !string.IsNullOrEmpty(msg.FormattedTableText))
            {
                sb.AppendLine(msg.FormattedTableText);
            }
            try
            {
                System.Windows.Clipboard.SetText(sb.ToString().TrimEnd());
                StatusMessage = Tx.T("Message copied to clipboard.");
            }
            catch { }
        }

        [RelayCommand]
        private async Task NewChatAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = Tx.T("Starting new chat...");
            
            try
            {
                // 0. Se il browser non è avviato, avviarlo automaticamente
                if (!_geminiService.IsInitialized)
                {
                    StatusMessage = Tx.T("Starting browser...");
                    await _geminiService.InitializeAsync(headless: false);
                }

                // 1. Salva la sessione corrente prima di cambiarla
                if (ActiveSession != null)
                {
                    SaveCurrentMessagesToSession(ActiveSession);
                    _ = CloudServiceLocator.Session.SaveSessionAsync(ActiveSession);
                    ActiveSession.IsActive = false;
                }
                
                // 2. Naviga Gemini alla home (nuova chat)
                _isNavigating = true;
                _isCurrentlyStreaming = false;
                try { await _geminiService.NewChatAsync(); }
                finally { _isNavigating = false; }
                
                // 3. Attendi un secondo e poi leggi il nuovo ID (potrebbe non esserci ancora)
                await Task.Delay(1500);
                string newId = await _geminiService.GetCurrentChatIdAsync();
                
                // 4. Crea una nuova sessione
                var newSession = new ChatSession
                {
                    GeminiId = newId,
                    Description = Tx.T("New Chat · {0}", SelectedAgent?.Name ?? "Default"),
                    AgentRoleType = SelectedAgent?.RoleType ?? "Default",
                    AgentName = SelectedAgent?.Name ?? "Default",
                    WorkspacePath = WorkspaceRoot,
                    CreatedAt = DateTime.Now,
                    IsActive = true
                };
                
                // Registra la sessione sull'adapter (fondamentale per il Cloud)
                newSession.ServerId = await CloudServiceLocator.Session.BeginSessionAsync(
                    newSession.AgentRoleType, newSession.GeminiId,
                    newSession.Description,   newSession.WorkspacePath);
                
                ChatHistory.Insert(0, newSession);
                ActiveSession = newSession;
                _ = CloudServiceLocator.Session.SaveSessionAsync(newSession);
                
                // 5. Resetta la chat nell'app
                ChatMessages.Clear();
                _lastInjectedRole = string.Empty; // Forza reiniezione del prompt iniziale
                
                StatusMessage = Tx.T("New chat ready.");
            }
            catch (Exception ex)
            {
                StatusMessage = Tx.T("New chat error: {0}", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task LoadSessionAsync(ChatSession? session)
        {
            if (session == null || IsBusy) return;
            IsBusy = true;
            StatusMessage = Tx.T("Loading chat: {0}...", session.DisplayDescription);
            
            try
            {
                // 1. Salva la sessione corrente
                if (ActiveSession != null && ActiveSession != session)
                {
                    SaveCurrentMessagesToSession(ActiveSession);
                    _ = CloudServiceLocator.Session.SaveSessionAsync(ActiveSession);
                    ActiveSession.IsActive = false;
                }
                
                // 2. Avvia il browser se non è ancora aperto, poi naviga
                if (!_geminiService.IsInitialized)
                {
                    StatusMessage = Tx.T("Starting browser...");
                    await _geminiService.InitializeAsync(headless: false);
                }

                _isNavigating = true;
                _isCurrentlyStreaming = false;
                try
                {
                    if (!string.IsNullOrEmpty(session.GeminiId))
                        await _geminiService.NavigateToChatAsync(session.GeminiId);
                }
                finally
                {
                    _isNavigating = false;
                }
                
                // 3. Ripristina i messaggi nella chat AIBridge
                ChatMessages.Clear();

                // Se la sessione ha messaggi in-memory usali (modalità locale),
                // altrimenti scaricali dall'adapter (server o DLL)
                IReadOnlyList<AIBridge.Models.SerializableChatMessage> msgs;
                if (session.Messages.Count > 0)
                {
                    msgs = session.Messages;
                }
                else
                {
                    var dtos = await CloudServiceLocator.Session.GetMessagesAsync(session.ServerId);
                    msgs = dtos;
                }

                foreach (var sm in msgs)
                {
                    ChatMessages.Add(new ChatMessage
                    {
                        Sender = sm.Sender,
                        Text = sm.Text,
                        IsUser = sm.IsUser,
                        IsCollapsible = sm.IsCollapsible,
                        IsExpanded = sm.IsExpanded,
                        IsSqlResult = sm.IsSqlResult,
                        FormattedTableText = sm.FormattedTableText,
                        TsvData = sm.TsvData,
                        FilePathToOpen = sm.FilePathToOpen,
                        HasBackup = sm.HasBackup,
                        IsToolMessage = sm.IsToolMessage,
                        ToolName = sm.ToolName,
                        ToolInput = sm.ToolInput,
                        ToolOutput = sm.ToolOutput,
                        IsToolExpanded = sm.IsToolExpanded
                    });
                }
                
                // 4. Forza la reiniezione del prompt iniziale dell'agente
                _lastInjectedRole = string.Empty;
                session.IsActive = true;
                ActiveSession = session;
                // 5. Seleziona l'agente corretto se necessario
                var matchingAgent = AvailableAgents.FirstOrDefault(a => a.RoleType == session.AgentRoleType);
                if (matchingAgent != null)
                    SelectedAgent = matchingAgent;

                // 6. Ripristina il workspace corretto se salvato
                System.Diagnostics.Debug.WriteLine($"[LOAD] Session GeminiId={session.GeminiId}, WorkspacePath={session.WorkspacePath}");
                if (!string.IsNullOrEmpty(session.WorkspacePath))
                {
                    WorkspaceRoot = session.WorkspacePath;
                    RefreshWorkspaceTree();
                }
                
                // 7. Ripristina il profilo SQL se pertinente
                if (!string.IsNullOrEmpty(session.SelectedSqlProfilePath))
                {
                    var profile = AvailableSqlProfiles.FirstOrDefault(p => string.Equals(p.FilePath, session.SelectedSqlProfilePath, StringComparison.OrdinalIgnoreCase));
                    if (profile != null)
                    {
                        _isInternalProfileChange = true;
                        try { SelectedSqlProfile = profile; } finally { _isInternalProfileChange = false; }
                    }
                }
                

                StatusMessage = Tx.T("Chat '{0}' loaded.", session.DisplayDescription);
            }
            catch (Exception ex)
            {
                StatusMessage = Tx.T("Chat loading error: {0}", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void SaveCurrentAgentAndWorkspace(ChatSession? session)
        {
            if (session == null) return;
            session.AgentRoleType = SelectedAgent?.RoleType ?? "Default";
            session.AgentName = SelectedAgent?.Name ?? "Default";
            session.WorkspacePath = WorkspaceRoot;
            _ = CloudServiceLocator.Session.SaveSessionAsync(session);
            session.RaiseDescriptionChanged();
            StatusMessage = Tx.T("Agent and Workspace saved for chat: {0}", session.DisplayDescription);
        }

        [RelayCommand]
        private void DeleteSessionFromHistory(ChatSession? session)
        {
            if (session == null) return;
            _ = CloudServiceLocator.Session.RemoveAsync(session.ServerId);
            ChatHistory.Remove(session);
            if (ActiveSession == session)
            {
                ActiveSession = null;
                ChatMessages.Clear();
            }
        }

        [RelayCommand]
        private void RenameChat(ChatSession? session)
        {
            if (session == null) return;
            var dialog = new AIBridge.Dialogs.RenameChatDialog(session.Description)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                session.Description = dialog.NewName;
                _ = CloudServiceLocator.Session.SaveSessionAsync(session);
                // Notifica la UI del cambio di DisplayDescription
                session.RaiseDescriptionChanged();
            }
        }

        [RelayCommand]
        private async Task SyncChatNameAsync(ChatSession? session)
        {
            if (session == null || string.IsNullOrEmpty(session.GeminiId)) return;
            StatusMessage = Tx.T("Synchronizing chat name...");
            try
            {
                var name = await _geminiService.GetChatNameFromGeminiAsync(session.GeminiId);
                if (string.IsNullOrEmpty(name))
                {
                    StatusMessage = Tx.T("Name sync failed: chat not found in ChatBot sidebar.");
                    return;
                }
                session.Description = name;
                _ = CloudServiceLocator.Session.SaveSessionAsync(session);
                session.RaiseDescriptionChanged();
                StatusMessage = Tx.T("Name updated: \"{0}\"", name);
            }
            catch (Exception ex)
            {
                StatusMessage = Tx.T("Name sync failed: {0}", ex.Message);
            }
        }

        [RelayCommand]
        private void EditDbProfiles()
        {
            if (string.IsNullOrEmpty(WorkspaceRoot) || !System.IO.Directory.Exists(WorkspaceRoot))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = Tx.T("Please select a workspace first.");
                });
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new Dialogs.SqlProfileEditorDialog(WorkspaceRoot, AvailableSqlProfiles)
                {
                    Owner = Application.Current.MainWindow
                };
                
                if (dialog.ShowDialog() == true)
                {
                    RefreshWorkspaceTree();
                }
            });
        }

        // ─── Document Arena ─────────────────────────────────────────────────────
        /// <summary>Salva i messaggi correnti nella sessione prima di navigare altrove.</summary>
        private void SaveCurrentMessagesToSession(ChatSession session)
        {
            session.Messages = ChatMessages.Select(m => new SerializableChatMessage
            {
                Sender = m.Sender,
                Text = m.Text,
                IsUser = m.IsUser,
                IsCollapsible = m.IsCollapsible,
                IsExpanded = m.IsExpanded,
                IsSqlResult = m.IsSqlResult,
                FormattedTableText = m.FormattedTableText,
                TsvData = m.TsvData,
                FilePathToOpen = m.FilePathToOpen,
                HasBackup = m.HasBackup,
                IsToolMessage = m.IsToolMessage,
                ToolName = m.ToolName,
                ToolInput = m.ToolInput,
                ToolOutput = m.ToolOutput,
                IsToolExpanded = m.IsToolExpanded
            }).ToList();

            session.AgentRoleType = SelectedAgent?.RoleType ?? "Default";
            session.AgentName = SelectedAgent?.Name ?? "Default";
            session.WorkspacePath = WorkspaceRoot;
            session.SelectedSqlProfilePath = SelectedSqlProfile?.FilePath ?? string.Empty;
            System.Diagnostics.Debug.WriteLine($"[SAVE] Session GeminiId={session.GeminiId}, WorkspacePath={session.WorkspacePath}, SqlProfile={session.SelectedSqlProfilePath}");
            
            // Auto-imposta la descrizione solo se non è ancora stata personalizzata manualmente,
            // e usa SOLO messaggi inviati dall'utente reale (non messaggi di sistema taggati IsUser=true)
            if (session.Description.StartsWith("Nuova Chat"))
            {
                var firstRealUserMsg = session.Messages
                    .FirstOrDefault(m => m.IsUser);
                if (firstRealUserMsg != null)
                {
                    const int cap = 80;
                    var text = firstRealUserMsg.Text.Trim();
                    session.Description = text.Length > cap
                        ? text[..cap] + "…"
                        : text;
                }
            }
        }

        private bool CanSendMessage() => !IsGeminiBusy && !IsSqlBusy;

        [RelayCommand(CanExecute = nameof(CanSendMessage))]
        private async Task ReinjectSkillAsync()
        {
            if (ActiveSession == null)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Please open a new session with the ChatBot (New chat) or choose one from the history."), Tx.T("Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _lastInjectedRole = string.Empty;
            InputText = Tx.T("Please, read and keep in mind your main directives (skills) that I am providing you again.");
            await SendMessageAsync();
        }

        [RelayCommand]
        private async Task ToggleTrainingModeAsync(System.Collections.IList? selectedItems)
        {
            if (!IsTrainingMode)
            {
                IsTrainingMode = true;
                StatusMessage = Tx.T("Training Mode: Select messages then click the training icon again to confirm.");
            }
            else
            {
                IsTrainingMode = false;
                StatusMessage = Tx.T("Ready.");
                if (selectedItems != null && selectedItems.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in selectedItems)
                    {
                        if (item is Models.ChatMessage msg)
                        {
                            sb.AppendLine($"[{msg.Sender}]:");
                            sb.AppendLine(msg.Text);
                            sb.AppendLine();
                        }
                    }
                    string collected = sb.ToString();
                    
                    var selectionDialog = new Dialogs.TrainingSelectionDialog(collected) { Owner = Application.Current.MainWindow };
                    if (selectionDialog.ShowDialog() == true)
                    {
                        string finalSelection = selectionDialog.SelectedText;
                        if (!string.IsNullOrWhiteSpace(finalSelection))
                        {
                            IsBusy = true;
                            StatusMessage = Tx.T("Generating skill rule in background...");
                            try
                            {
                                string prompt = await CloudServiceLocator.Orchestrator.BuildPromptAsync("SkillExtraction", finalSelection);
                                string response = await _geminiService.SendAndAwaitResponseAsync(prompt);
                                
                                response = response.Trim();
                                if (response.StartsWith("```markdown", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    response = response.Substring(11);
                                    if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);
                                }
                                else if (response.StartsWith("```"))
                                {
                                    response = response.Substring(3);
                                    if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);
                                }
                                response = response.Trim();

                                var resultDialog = new Dialogs.TrainingResultDialog(response) { Owner = Application.Current.MainWindow };
                                if (resultDialog.ShowDialog() == true)
                                {
                                    string finalSkill = resultDialog.ResultText;
                                    if (!string.IsNullOrWhiteSpace(finalSkill))
                                    {
                                        AppendSkillToWorkspace(finalSkill);
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {
                                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error during skill extraction: {0}", ex.Message), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            finally
                            {
                                IsBusy = false;
                                StatusMessage = Tx.T("Ready.");
                            }
                        }
                    }
                }
            }
        }
        
        private void AppendSkillToWorkspace(string newSkill)
        {
            try
            {
                if (string.IsNullOrEmpty(WorkspaceRoot))
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("No workspace is currently open. Cannot save skill."), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string agentFolder = System.IO.Path.Combine(WorkspaceRoot, ".agent");
                if (!System.IO.Directory.Exists(agentFolder))
                {
                    System.IO.Directory.CreateDirectory(agentFolder);
                }
                
                string path = System.IO.Path.Combine(agentFolder, "skill.md");
                
                // Crea il file se non esiste
                if (!System.IO.File.Exists(path))
                {
                    System.IO.File.WriteAllText(path, "");
                }

                System.IO.File.AppendAllText(path, $"\n\n### {Tx.T("New Learned Rules")}\n{newSkill}\n");
                _lastInjectedRole = string.Empty;
                ChatMessages.Add(new Models.ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Workspace skill updated successfully!"), IsUser = false });
            }
            catch (System.Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Failed to save skill: {0}", ex.Message), Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanSendMessage))]
        private async Task SendMessageAsync()
        {
            if (ActiveSession == null)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Please open a new session with the ChatBot (New chat) or choose one from the history."), Tx.T("Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(InputText)) return;

            string msg = InputText;
            InputText = string.Empty;
            
            // Se il messaggio è digitato dall'utente, azzeriamo il contatore dei retry
            if (!msg.StartsWith("[AUTO-FIX]"))
            {
                _sqlErrorRetryCount = 0;
            }
            else
            {
                // Rimuoviamo il prefisso interno
                msg = msg.Replace("[AUTO-FIX]", "").Trim();
            }
            
            ChatMessages.Add(new ChatMessage { Sender = Tx.T("User"), Text = msg, IsUser = true });
            
            // Salva subito il messaggio utente nel Cloud (OnUserPromptDetected è soppresso
            // per i messaggi programmatici, quindi salviamo qui prima dell'invio a Gemini)
            if (ActiveSession.ServerId != Guid.Empty)
                _ = CloudServiceLocator.Session.AppendAsync(ActiveSession.ServerId, "user", msg);
            
            // Forza il rendering dell'interfaccia utente aggiungendo un micro delay
            await Task.Delay(50);
            
            string currentRole = SelectedAgent?.RoleType ?? "Default";
            if (_lastInjectedRole != currentRole)
            {
                string generalSkillContent = "";
                if (!string.IsNullOrEmpty(WorkspaceRoot))
                {
                    string skillPath = System.IO.Path.Combine(WorkspaceRoot, ".agent", "skill.md");
                    if (System.IO.File.Exists(skillPath))
                    {
                        try
                        {
                            generalSkillContent = System.IO.File.ReadAllText(skillPath);
                            msg = $"{Tx.T("[GENERAL WORKSPACE RULES (SKILL)]:")}\n{generalSkillContent}\n\n{Tx.T("[USER REQUEST]:")}\n{msg}";
                        }
                        catch { }
                    }
                }
            }
            
            try
            {
                // Inietta il prompt speciale in base all'agente selezionato
                if (SelectedAgent?.RoleType == "Coder")
                {
                    if (_lastInjectedRole != "Coder")
                    {
                        msg = await BuildPromptAsync(msg, "Coder", string.Empty, string.Empty);
                        _lastInjectedRole = "Coder";
                        ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("[Initial Prompt Sent]: Developer Agent configured"), IsUser = true });
                    }
                    else
                    {
                        msg = msg + "\n\n" + Tx.T("(CRITICAL Reminder: ALWAYS rewrite the COMPLETE file with @@@FILE: path@@@ ... @@@END_FILE@@@. ABSOLUTE PROHIBITION of partial edits or Canvas. Print everything in chat!)");
                    }
                }
                else if (SelectedAgent?.RoleType == "JsonExtractor")
                {
                    if (_lastInjectedRole != "JsonExtractor")
                    {
                        msg = await BuildPromptAsync(msg, "JsonExtractor", string.Empty, string.Empty);
                        _lastInjectedRole = "JsonExtractor";
                        ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("[Initial Prompt Sent]: JSON Agent configured"), IsUser = true });
                    }
                    else
                    {
                        msg = msg + "\n\n" + Tx.T("(Reminder: Provide ONLY the pure structured JSON.)");
                    }
                }
                else if (SelectedAgent?.RoleType == "SqlAnalyst")
                {
                    if (_lastInjectedRole != "SqlAnalyst")
                    {
                        string schema = Tx.T("No schema available.");
                        string skill = "";
                        
                        if (SelectedSqlProfile != null)
                        {
                            try 
                            {
                                Application.Current.Dispatcher.Invoke(() => 
                                {
                                    IsSqlBusy = true;
                                    StatusMessage = Tx.T("Reading database schema...");
                                });
                                var sqlService = new Services.SqlExecutionService();
                                schema = await sqlService.GetSchemaAsync(SelectedSqlProfile.Provider, SelectedSqlProfile.ConnectionString);
                                
                                string profileDir = System.IO.Path.GetDirectoryName(SelectedSqlProfile.FilePath) ?? WorkspaceRoot;
                                if (!string.IsNullOrEmpty(SelectedSqlProfile.SkillFile))
                                {
                                    string skillPath = System.IO.Path.Combine(profileDir, SelectedSqlProfile.SkillFile);
                                    if (System.IO.File.Exists(skillPath))
                                        skill = System.IO.File.ReadAllText(skillPath);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                schema = $"{Tx.T("SCHEMA EXTRACTION ERROR:")} {ex.Message}";
                            }
                            finally
                            {
                                Application.Current.Dispatcher.Invoke(() => 
                                {
                                    IsSqlBusy = false;
                                    StatusMessage = Tx.T("Waiting for AI response...");
                                });
                            }
                        }
                        else
                        {
                            schema = Tx.T("ERROR: No SQL Profile selected or .sqlprofile file not found in the Workspace.");
                        }

                        msg = await BuildPromptAsync(msg, "SqlAnalyst", schema, skill);
                        _lastInjectedRole = "SqlAnalyst";
                        ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("[Initial Prompt Sent]: SQL Agent configured with current schema"), IsUser = true });
                    }
                    else
                    {
                        msg = msg + "\n\n" + Tx.T("(Reminder: If you need to do a data extraction, ALWAYS wrap the query between @@@SQL_QUERY@@@ and @@@END_SQL@@@. If instead you are answering a general question or a greeting, DO NOT use these tags and write only normal text.)");
                    }
                }
                else
                {
                    _lastInjectedRole = "Default";
                }

                // Iniezione dei Tool Custom (estratti dal Database remoto) nel prompt iniziale
                if (_lastInjectedRole != "Default" && SelectedAgent != null && SelectedAgent.Tools != null && SelectedAgent.Tools.Count > 0)
                {
                    // Aggiungiamo i tool solo quando stiamo inviando il prompt formativo iniziale (o se lo riteniamo utile sempre, ma tipicamente basta all'inizio)
                    // Dato che _lastInjectedRole viene aggiornato subito prima, possiamo verificare se la stringa `msg` è appena stata estesa con il template base
                    bool isInitialPrompt = msg.Contains("=== CUSTOM DYNAMIC TOOLS ===") == false && 
                                           (msg.Contains("[CONVERSATION]") || msg.Length > 500); 

                    // In realtà, dato che la logica sopra ricrea il msg *solo* se _lastInjectedRole è cambiato, 
                    // la variabile `msg` qui contiene il mega-prompt base se è la prima volta.
                    // Per sicurezza, se è il primo prompt, appendiamo la guida ai custom tool.
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("\n\n=== CUSTOM DYNAMIC TOOLS ===");
                    sb.AppendLine("In addition to your native tools, you have access to the following custom tools provided by the user's database.");
                    sb.AppendLine("To execute them, use the exact same JSON format: ```tool\\n{\"action\": \"ACTION_NAME\"}\\n```");
                    foreach(var tool in SelectedAgent.Tools)
                    {
                        sb.AppendLine($"- **{tool.ActionName}**: {tool.Description}");
                    }
                    sb.AppendLine("============================");
                    
                    if (msg.Length > 200) // E' sicuramente il prompt inziale o esteso
                    {
                        msg += sb.ToString();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsGeminiBusy = false;
                    StatusMessage = Tx.T("Orchestrator error: {0}", ex.Message);
                    ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Operation aborted. {0}", ex.Message), IsUser = true });
                });
                return;
            }
            
            // Prepariamo i file (se ce ne sono)
            string[]? filesToUpload = AttachedFiles.Count > 0 ? AttachedFiles.ToArray() : null;
            AttachedFiles.Clear();
            
            // Aggiramento del blocco estensioni di Gemini (es. .xaml)
            if (filesToUpload != null)
            {
                for (int i = 0; i < filesToUpload.Length; i++)
                {
                    string file = filesToUpload[i];
                    string ext = System.IO.Path.GetExtension(file).ToLower();
                    if (ext == ".xaml" || ext == ".csproj" || ext == ".xml")
                    {
                        string safeName = System.IO.Path.GetFileNameWithoutExtension(file) + ext.Replace(".", "_") + ".txt";
                        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), safeName);
                        System.IO.File.Copy(file, tempPath, true);
                        filesToUpload[i] = tempPath;
                    }
                }
            }
            
            _isCurrentlyStreaming = false;
            StatusMessage = Tx.T("Generating (Google Gemini)...");

            try
            {
                await _geminiService.SendPromptAsync(msg, filesToUpload);
            }
            catch (System.Exception ex)
            {
                StatusMessage = Tx.T("Error sending: {0}", ex.Message);
            }
        }

        [RelayCommand]
        private void ResetState()
        {
            _geminiService.ForceResetBusyState();
            IsSqlBusy = false;
            _isCurrentlyStreaming = false;
            _currentToolLoop = 0;
            StatusMessage = Tx.T("State manually reset.");
        }

        private async Task<string> BuildPromptAsync(string userMsg, string agentType, string schema, string skill)
        {
            var client = CloudServiceLocator.Client;
            if (client != null && client.IsConnected)
            {
                var request = new AIBridge.Shared.Models.ContextRequest 
                { 
                    SessionId = ActiveSession?.GeminiId ?? Guid.NewGuid().ToString(),
                    AgentType = agentType, 
                    UserMessage = userMsg, 
                    SchemaContext = schema,
                    Language = SelectedLanguage?.Code ?? "en-US"
                };
                var cmd = await client.GetCommandAsync(request);
                if (cmd?.BuiltPrompt != null) 
                {
                    Application.Current.Dispatcher.Invoke(() => IsLocalMode = false);
                    return cmd.BuiltPrompt;
                }
            }
            
            // Fallback locale
            Application.Current.Dispatcher.Invoke(() => 
            {
                IsLocalMode = true;
                LocalModeBannerText = Tx.T("Local Mode (Server unreachable or offline)");
            });
            
            // Switch on agent type via Orchestrator
            return await CloudServiceLocator.Orchestrator.BuildPromptAsync(agentType, userMsg, schema, skill);
        }

        private void HandleUserPromptDetected(string promptText)
        {
            if (ActiveSession != null)
            {
                _ = CloudServiceLocator.Session.AppendAsync(ActiveSession.ServerId, "user", promptText);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                ChatMessages.Add(new ChatMessage { Sender = Tx.T("User"), Text = promptText, IsUser = true });
                _isCurrentlyStreaming = false;
                StatusMessage = Tx.T("Generating (Google Gemini)...");
            });
        }

        private async Task HandleMessageCompletedAsync(string responseText)
        {
            await Task.Run(async () =>
            {
                try
                {
                    // Pulisci subito l'HTML safificato dall'LLM
                    string response = System.Net.WebUtility.HtmlDecode(responseText);
                    int maxToolLoops = 5;

                // 1. RIMUOVIAMO IL MESSAGGIO DI STREAMING ("Gemini")
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_isCurrentlyStreaming && ChatMessages.Count > 0)
                    {
                        var lastMsg = ChatMessages[ChatMessages.Count - 1];
                        if (lastMsg.Sender == "Gemini" || lastMsg.Sender == Tx.T("Generating (Google Gemini)..."))
                        {
                            ChatMessages.RemoveAt(ChatMessages.Count - 1);
                        }
                    }
                    _isCurrentlyStreaming = false;
                    StatusMessage = Tx.T("Response completed.");
                });

                if (string.IsNullOrEmpty(response))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = Tx.T("Error: no response or timeout.");
                    });
                    return;
                }

                // 2. PARSING DEL CONTENUTO TRAMITE ESTRATTORI DINAMICI
                // Gli estrattori built-in sono sempre presenti come fallback nativo (come nella versione pre-refactoring)
                var allExtractors = new System.Collections.Generic.List<AIBridge.Shared.Models.BlockExtractorDefinition>
                {
                    // FALLBACK NATIVI: sempre attivi, garantiscono il comportamento pre-refactoring
                    new AIBridge.Shared.Models.BlockExtractorDefinition { StartTagPattern = @"@@@FILE:\s*(.*?)@@@", EndTag = "@@@END_FILE@@@", TargetAction = "WRITE_FILE" },
                    new AIBridge.Shared.Models.BlockExtractorDefinition { StartTagPattern = @"@@@SQL\\?_QUERY@@@", EndTag = @"@@@END\\?_SQL@@@", TargetAction = "EXECUTE_SQL" }
                };
                // Sovrascritura/estensione con gli estrattori dinamici da ToolCore (se disponibile)
                if (App.ToolDictionary != null)
                    allExtractors.AddRange(App.ToolDictionary.GetBaseExtractors());
                if (SelectedAgent?.Extractors != null)
                    allExtractors.AddRange(SelectedAgent.Extractors);

                bool hasMatchedNativeExtractor = false;

                foreach (var ext in allExtractors)
                {
                    var regex = new System.Text.RegularExpressions.Regex(ext.StartTagPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    bool isMatch = regex.IsMatch(response);
                    if (isMatch)
                    {
                        if (ext.TargetAction == "WRITE_FILE")
                        {
                            hasMatchedNativeExtractor = true;
                            MultiFileEditCommand multiCommand = null;
                            try { multiCommand = MarkdownCodeExtractor.Parse(response); }
                            catch (System.Exception ex) { Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("System I/O"), Text = $"{Tx.T("MARKDOWN PARSING ERROR!")}\n{ex.Message}", IsUser = false }); }); return; }

                            if (multiCommand != null)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (!string.IsNullOrWhiteSpace(multiCommand.Message))
                                        ChatMessages.Add(new ChatMessage { Sender = Tx.T("Developer Agent"), Text = multiCommand.Message, IsUser = false });
                                });

                                if (multiCommand.Files != null && multiCommand.Files.Count > 0)
                                {
                                    foreach (var editCommand in multiCommand.Files)
                                    {
                                        if (!string.IsNullOrWhiteSpace(editCommand.FilePath))
                                        {
                                            try
                                            {
                                                var savePath = System.IO.Path.Combine(WorkspaceRoot, editCommand.FilePath);
                                                var directory = System.IO.Path.GetDirectoryName(savePath);
                                                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory)) System.IO.Directory.CreateDirectory(directory);
                                                bool hasBak = false;
                                                if (System.IO.File.Exists(savePath))
                                                {
                                                    string bakPath = savePath + ".bak";
                                                    System.IO.File.Copy(savePath, bakPath, true);
                                                    hasBak = true;
                                                }
                                                System.IO.File.WriteAllText(savePath, editCommand.NewContent, System.Text.Encoding.UTF8);
                                                Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("System I/O"), Text = $"💾 {Tx.T("Written")}: {editCommand.FilePath} ({editCommand.NewContent.Length} chars)", IsUser = false, FilePathToOpen = savePath, HasBackup = hasBak }); });
                                            }
                                            catch (System.Exception ex)
                                            {
                                                Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("System I/O"), Text = $"❌ {Tx.T("FILE WRITE ERROR")}\n{ex.Message}", IsUser = false }); });
                                            }
                                        }
                                    }
                                    Application.Current.Dispatcher.Invoke(() => RefreshWorkspaceTree());
                                }

                                if (!string.IsNullOrWhiteSpace(multiCommand.PostMessage))
                                {
                                    Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("Developer Agent"), Text = multiCommand.PostMessage, IsUser = false }); });
                                }
                                response = multiCommand.Message + "\n\n" + (multiCommand.PostMessage ?? "");
                            }
                            break;
                        }
                        else if (ext.TargetAction == "EXECUTE_SQL")
                        {
                            hasMatchedNativeExtractor = true;
                            var query = AgentCore.SqlExtractor.ExtractSql(response);
                            var conversationalText = AgentCore.SqlExtractor.StripSql(response);

                            if (!string.IsNullOrWhiteSpace(query))
                            {
                                Application.Current.Dispatcher.Invoke(() => { if (!string.IsNullOrWhiteSpace(conversationalText)) ChatMessages.Add(new ChatMessage { Sender = Tx.T("SQL Agent"), Text = conversationalText, IsUser = false, IsCollapsible = conversationalText.Contains("\n") || conversationalText.Length > 100, IsExpanded = false }); });

                                if (SelectedSqlProfile == null)
                                {
                                    Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("SQL Agent"), Text = Tx.T("⚠️ No SQL Profile selected. Please select a .sqlprofile from the workspace before running queries."), IsUser = false }); });
                                    break;
                                }

                                try
                                {
                                    Application.Current.Dispatcher.Invoke(() => { IsSqlBusy = true; StatusMessage = Tx.T("Executing SQL query on local DB..."); });
                                    var sqlService = new Services.SqlExecutionService();
                                    var dt = await sqlService.ExecuteQueryAsync(SelectedSqlProfile.Provider, SelectedSqlProfile.ConnectionString, query);
                                    var (tableText, tsvData) = AgentCore.SqlExtractor.FormatDataTable(dt);
                                    Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("Query Result (Local DB)"), Text = Tx.T("Execution completed ({0} rows extracted).", dt.Rows.Count.ToString()), IsUser = false, IsSqlResult = true, FormattedTableText = tableText, TsvData = tsvData, IsCollapsible = false }); StatusMessage = Tx.T("Query executed successfully."); _sqlErrorRetryCount = 0; });
                                }
                                catch (System.Exception ex)
                                {
                                    Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("Query Error (Local DB)"), Text = Tx.T("Cannot execute query:\n{0}\n\nQuery attempted:\n{1}", ex.Message, query), IsUser = false, IsCollapsible = true, IsExpanded = true }); StatusMessage = Tx.T("Query execution failed."); });
                                    if (_sqlErrorRetryCount < 2)
                                    {
                                        _sqlErrorRetryCount++;
                                        Application.Current.Dispatcher.Invoke(() => { StatusMessage = Tx.T("SQL Error. Auto-fix attempt ({0}/2)...", _sqlErrorRetryCount.ToString()); });
                                        string autoFixPrompt = Tx.T("[AUTO-FIX] The SQL query you just generated returned this syntax or logic error on the database:\n{0}\n\nPlease analyze the error, check the schema and fix the query always returning it wrapped in @@@SQL_QUERY@@@ and @@@END_SQL@@@ tags.", ex.Message);
                                        await Task.Delay(1500);
                                        try { await _geminiService.SendPromptAsync(autoFixPrompt, null); } catch { }
                                        return;
                                    }
                                    else
                                    {
                                        Application.Current.Dispatcher.Invoke(() => { StatusMessage = Tx.T("SQL Error. Auto-fix failed after 2 attempts."); });
                                    }
                                }
                                finally { Application.Current.Dispatcher.Invoke(() => { IsSqlBusy = false; }); }
                            }
                            else
                            {
                                Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = Tx.T("SQL Agent"), Text = response, IsUser = false, IsCollapsible = response.Contains("\n"), IsExpanded = false }); });
                            }
                            break;
                        }
                        else
                        {
                            hasMatchedNativeExtractor = true;
                            var blockRegex = new System.Text.RegularExpressions.Regex(ext.StartTagPattern + @"([\s\S]*?)" + ext.EndTag, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            var match = blockRegex.Match(response);
                            if (match.Success)
                            {
                                string content = match.Groups[1].Value.Trim();
                                string mockJson = $"{{\"action\": \"{ext.TargetAction}\", \"content\": \"{content.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")}\"}}";
                                var customResult = await AIBridge.AgentCore.AgentToolService.ProcessToolsAsync($"```tool\n{mockJson}\n```", WorkspaceRoot, SelectedAgent?.Tools);
                                Application.Current.Dispatcher.Invoke(() => { ChatMessages.Add(new ChatMessage { Sender = SelectedAgent?.RoleType ?? "System I/O", Text = $"[{ext.TargetAction}]\n{customResult}", IsUser = false }); });
                            }
                            break;
                        }
                    }
                }

                if (!hasMatchedNativeExtractor)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ChatMessages.Add(new ChatMessage { Sender = SelectedAgent?.RoleType ?? "AI", Text = response, IsUser = false, IsCollapsible = response.Contains("\n"), IsExpanded = false });
                    });
                }

                // 3. ESECUZIONE TOOL
                while (!string.IsNullOrEmpty(response) && _currentToolLoop < maxToolLoops)
                {
                    var toolMatch = System.Text.RegularExpressions.Regex.Match(response, @"```tool\s*([\s\S]*?)\s*```", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!toolMatch.Success) break;
                    
                    _currentToolLoop++;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = Tx.T("Running tool ({0}/{1})...", _currentToolLoop.ToString(), maxToolLoops.ToString());
                    });
                    
                    string jsonContent = toolMatch.Groups[1].Value;
                    var actionMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, @"\""action\""\s*:\s*\""([^\""]+)\""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var pathMatch   = System.Text.RegularExpressions.Regex.Match(jsonContent, @"\""path\""\s*:\s*\""([^\""]+)\""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var queryMatch  = System.Text.RegularExpressions.Regex.Match(jsonContent, @"\""query\""\s*:\s*\""([^\""]+)\""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var cmdMatch    = System.Text.RegularExpressions.Regex.Match(jsonContent, @"\""command\""\s*:\s*\""([^\""]+)\""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    string toolName  = actionMatch.Success ? actionMatch.Groups[1].Value.ToUpper() : "TOOL";
                    string toolPath  = pathMatch.Success  ? pathMatch.Groups[1].Value  : "";
                    string toolQuery = queryMatch.Success  ? queryMatch.Groups[1].Value : "";
                    string toolCmd   = cmdMatch.Success    ? cmdMatch.Groups[1].Value   : "";

                    string toolInput = toolMatch.Value.Trim();

                    // Cerca il template UI nel dizionario dinamico
                    var allTools = new System.Collections.Generic.List<AIBridge.Shared.Models.ToolDefinition>();
                    if (App.ToolDictionary != null)
                        allTools.AddRange(App.ToolDictionary.GetBaseTools());
                    if (SelectedAgent?.Tools != null)
                        allTools.AddRange(SelectedAgent.Tools);

                    var toolDef = System.Linq.Enumerable.FirstOrDefault(allTools, t => t.ActionName.Equals(toolName, System.StringComparison.OrdinalIgnoreCase));
                    if (toolDef != null && !string.IsNullOrEmpty(toolDef.UiFormatTemplate))
                    {
                        toolInput = toolDef.UiFormatTemplate;
                        if (!string.IsNullOrEmpty(toolCmd)) toolInput = toolInput.Replace("{0}", toolCmd);
                        else if (!string.IsNullOrEmpty(toolPath)) toolInput = toolInput.Replace("{0}", toolPath);
                        if (!string.IsNullOrEmpty(toolQuery)) toolInput = toolInput.Replace("{1}", toolQuery);
                    }
                    
                    var toolMsg = new Models.ChatMessage
                    {
                        IsToolMessage = true,
                        ToolName = toolName,
                        ToolInput = toolInput,
                        ToolOutput = Tx.T("In progress..."),
                        IsToolExpanded = false
                    };
                    Application.Current.Dispatcher.Invoke(() => ChatMessages.Add(toolMsg));
                    
                    var toolResult = await AIBridge.AgentCore.AgentToolService.ProcessToolsAsync(response, WorkspaceRoot, SelectedAgent?.Tools);
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        toolMsg.ToolOutput = toolResult;
                    });
                    
                    string nextPrompt = Tx.T("[TOOL RESULT]\n{0}\n\n(If you need more data, use another JSON query. If you are done, reply to the user in natural language and DO NOT output JSON anymore.)", toolResult);
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = Tx.T("Generating (Google Gemini)...");
                    });

                    try
                    {
                        await _geminiService.SendPromptAsync(nextPrompt, null);
                    }
                    catch (System.Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = Tx.T("Error sending tool result: {0}", ex.Message);
                            _currentToolLoop = 0;
                        });
                    }
                    return;
                }

                if (_currentToolLoop >= maxToolLoops)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ChatMessages.Add(new ChatMessage { 
                            Sender = Tx.T("System I/O"), 
                            Text = $"⚠️ {Tx.T("Maximum consecutive autonomous actions ({0}) reached. Stopping to prevent infinite loops.", maxToolLoops.ToString())}", 
                            IsUser = false 
                        });
                    });
                }

                _currentToolLoop = 0;
                
                await AutoSaveCurrentSessionAsync();
                
                if (ActiveSession != null)
                {
                    _ = CloudServiceLocator.Session.AppendAsync(ActiveSession.ServerId, "assistant", responseText);
                }
                }
                catch (System.Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = Tx.T("Unhandled error during message processing: {0}", ex.Message);
                    });
                }
                finally
                {
                    _geminiService.ForceResetBusyState();
                    IsSqlBusy = false;
                    _isCurrentlyStreaming = false;
                }
            });
        }

        /// <summary>
        /// Legge l'ID della chat da Gemini (se non lo abbiamo ancora),
        /// crea una sessione se non esiste, e salva i messaggi.
        /// </summary>
        private async Task AutoSaveCurrentSessionAsync()
        {
            try
            {
                // Se non abbiamo ancora una sessione attiva, ne creiamo una al volo
                if (ActiveSession == null)
                {
                    string geminiId = await _geminiService.GetCurrentChatIdAsync();
                    
                    if (string.IsNullOrEmpty(geminiId))
                    {
                        // Gemini impiega qualche millisecondo per aggiornare l'URL con il nuovo ID, attendiamo
                        await Task.Delay(1500);
                        geminiId = await _geminiService.GetCurrentChatIdAsync();
                    }

                    // Se è ancora vuoto, il messaggio ha probabilmente fallito o Gemini non ha generato la chat.
                    // Evitiamo di inquinare il DB con chat "vuote" o "orfane" che non si possono riaprire.
                    if (string.IsNullOrEmpty(geminiId)) return;

                    string currentIcon = CloudServiceLocator.Session.GetType().Name.Contains("Local") ? "💻" : "🌐";
                    string currentColor = CloudServiceLocator.Session.GetType().Name.Contains("Local") ? "#4DA6FF" : "#00E5FF";

                    var newSession = new ChatSession
                    {
                        GeminiId = geminiId,
                        Description = Tx.T("New Chat · {0}", SelectedAgent?.Name ?? "Default"),
                        AgentRoleType = SelectedAgent?.RoleType ?? "Default",
                        AgentName = SelectedAgent?.Name ?? "Default",
                        CreatedAt = DateTime.Now,
                        IsActive = true,
                        StorageIcon = currentIcon,
                        StorageIconColor = currentColor
                    };
                    // Se è già in cronologia (stessa sessione riaperta), non duplichiamo
                    var existing = ChatHistory.FirstOrDefault(s => s.GeminiId == geminiId);
                    if (existing == null)
                    {
                        ChatHistory.Insert(0, newSession);
                        ActiveSession = newSession;
                    }
                    else
                    {
                        ActiveSession = existing;
                    }
                }
                else if (string.IsNullOrEmpty(ActiveSession.GeminiId))
                {
                    // Abbiamo creato la sessione prima che Gemini generasse l'URL univoco
                    string oldTemp = ActiveSession.CreatedAt.ToString("yyyyMMdd_HHmmss");
                    string newId = await _geminiService.GetCurrentChatIdAsync();
                    if (!string.IsNullOrEmpty(newId))
                    {
                        _ = CloudServiceLocator.Session.SyncGeminiIdAsync(ActiveSession.ServerId, newId);
                        ActiveSession.GeminiId = newId;
                    }
                }

                // Deseleziona tutte le altre sessioni
                foreach (var s in ChatHistory)
                    s.IsActive = (s == ActiveSession);

                SaveCurrentMessagesToSession(ActiveSession);
                _ = CloudServiceLocator.Session.SaveSessionAsync(ActiveSession);
            }
            catch { /* Non bloccare mai la UI per errori di auto-save */ }
        }


        [RelayCommand]
        private void CopySqlText(ChatMessage msg)
        {
            if (msg != null && !string.IsNullOrEmpty(msg.FormattedTableText))
            {
                System.Windows.Clipboard.SetText(msg.FormattedTableText);
                StatusMessage = Tx.T("Table copied to clipboard!");
            }
        }

        [RelayCommand]
        private void CopySqlTsv(ChatMessage msg)
        {
            if (msg != null && !string.IsNullOrEmpty(msg.TsvData))
            {
                System.Windows.Clipboard.SetText(msg.TsvData);
                StatusMessage = Tx.T("Data (TSV) copied for Excel!");
            }
        }

        [RelayCommand]
        private void ToggleFileReject(ChatMessage msg)
        {
            if (msg == null || !msg.HasBackup || string.IsNullOrEmpty(msg.FilePathToOpen)) return;

            string originalPath = msg.FilePathToOpen;
            string bakPath = originalPath + ".bak";
            string newPath = originalPath + ".new";

            try
            {
                if (!msg.IsRejected)
                {
                    // RIFIUTA MODIFICHE
                    if (System.IO.File.Exists(originalPath))
                        System.IO.File.Move(originalPath, newPath, true);
                    
                    if (System.IO.File.Exists(bakPath))
                        System.IO.File.Move(bakPath, originalPath, true);
                        
                    msg.IsRejected = true;
                    msg.Text = msg.Text.Replace(Tx.T("Written:"), Tx.T("Rejected:"));
                }
                else
                {
                    // RIPRISTINA MODIFICHE
                    if (System.IO.File.Exists(originalPath))
                        System.IO.File.Move(originalPath, bakPath, true);
                        
                    if (System.IO.File.Exists(newPath))
                        System.IO.File.Move(newPath, originalPath, true);
                        
                    msg.IsRejected = false;
                    msg.Text = msg.Text.Replace("Rifiutato:", "Scritto:");
                }
                
                // Forza aggiornamento dell'albero Workspace
                RefreshWorkspaceTree();
            }
            catch (System.Exception ex)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Tx.T("Error during restore: {0}", ex.Message));
            }
        }

        [RelayCommand]
        private async Task TestDbConnectionAsync()
        {
            IsBusy = true;
            StatusMessage = Tx.T("DB connection test in progress...");
            try
            {
                if (SelectedSqlProfile == null)
                {
                    StatusMessage = Tx.T("No SQL Profile selected.");
                    return;
                }

                string testProvider = SelectedSqlProfile.Provider;
                string testConnectionString = SelectedSqlProfile.ConnectionString;
                
                var sqlService = new Services.SqlExecutionService();
                string schema = await sqlService.GetSchemaAsync(testProvider, testConnectionString);
                
                ChatMessages.Add(new ChatMessage { Sender = Tx.T("System DB Test"), Text = Tx.T("CONNECTION SUCCESSFUL!\n\nString: {0}\n\nExtracted Schema:\n{1}...\n\n(Schema truncated for brevity)", testConnectionString, schema.Substring(0, System.Math.Min(schema.Length, 1000))), IsUser = false });
                StatusMessage = Tx.T("DB test completed successfully.");
            }
            catch (System.Exception ex)
            {
                ChatMessages.Add(new ChatMessage { Sender = Tx.T("System DB Test"), Text = Tx.T("CONNECTION ERROR:\n{0}\n\nPlease check your .sqlprofile configuration in the current workspace.", ex.Message), IsUser = false });
                StatusMessage = Tx.T("DB test error.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task TestJsonExtractionAsync()
        {
            IsBusy = true;
            _isCurrentlyStreaming = false;
            StatusMessage = Tx.T("Structured data request to ChatBot...");

            var prompt = await CloudServiceLocator.Orchestrator.BuildPromptAsync("JsonExtractor", Tx.T("Generate plausible personal data for a certain John Doe, 45 years old plumber. Return the keys: firstname, lastname, age, profession."));
            ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Structured Extraction (JSON) -> Mario Rossi"), IsUser = true });
            
            var rawResponse = await _geminiService.SendAndAwaitResponseAsync(prompt);
            var pureJson = JsonExtractor.ExtractJson(rawResponse);

            try 
            {
                var person = JsonSerializer.Deserialize<PersonData>(pureJson);
                if (person != null)
                {
                    ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Data deserialized correctly in C#!\nObject -> Name: {0}, Job: {1}, Age: {2}", person.Nome, person.Professione, person.Eta.ToString()), IsUser = true });
                    StatusMessage = Tx.T("JSON extraction completed.");
                }
            }
            catch(JsonException ex)
            {
                ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Parse Error: Cannot deserialize JSON.\nDetail: {0}\nRaw data: {1}", ex.Message, pureJson), IsUser = true });
                StatusMessage = Tx.T("JSON extraction error.");
            }

            IsBusy = false;
        }

        [RelayCommand]
        private async Task TestFileEditorAsync()
        {
            IsBusy = true;
            _isCurrentlyStreaming = false;
            StatusMessage = Tx.T("Generating code (Agent Coder)...");

            var instruction = Tx.T("Write a public class 'Calculator' in C# with methods public int Add(int a, int b) and public int Multiply(int a, int b). Save it as 'Calculator.cs'.");
            var prompt = await CloudServiceLocator.Orchestrator.BuildPromptAsync("Coder", instruction);
            
            ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Sent Developer Task:\n{0}", instruction), IsUser = true });

            var rawResponse = await _geminiService.SendAndAwaitResponseAsync(prompt);

            try 
            {
                var multiCommand = MarkdownCodeExtractor.Parse(rawResponse);
                
                if (multiCommand != null)
                {
                    if (!string.IsNullOrWhiteSpace(multiCommand.Message))
                    {
                        ChatMessages.Add(new ChatMessage { Sender = Tx.T("Developer Agent"), Text = multiCommand.Message, IsUser = false });
                    }

                    if (multiCommand.Files != null && multiCommand.Files.Count > 0)
                    {
                        foreach(var editCommand in multiCommand.Files)
                        {
                            if (!string.IsNullOrWhiteSpace(editCommand.FilePath))
                            {
                                var savePath = System.IO.Path.Combine(WorkspaceRoot, editCommand.FilePath);
                                var dir = System.IO.Path.GetDirectoryName(savePath);
                                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                                {
                                    System.IO.Directory.CreateDirectory(dir);
                                }
                                System.IO.File.WriteAllText(savePath, editCommand.NewContent);
                                ChatMessages.Add(new ChatMessage { Sender = Tx.T("Developer Agent"), Text = Tx.T("OPERATION COMPLETED!\nCreated/Overwritten: {0}\nReal location: {1}\nCharacters: {2}", editCommand.FilePath, savePath, editCommand.NewContent.Length.ToString()), IsUser = false });
                            }
                        }
                        StatusMessage = Tx.T("File generation completed successfully.");
                        Application.Current.Dispatcher.Invoke(() => RefreshWorkspaceTree());
                    }
                }
            }
            catch (System.Exception ex)
            {
                ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("I/O error during save or parsing: {0}", ex.Message), IsUser = true });
                StatusMessage = Tx.T("Agent task error.");
            }

            IsBusy = false;
        }

        [RelayCommand]
        private void ToggleWatcher()
        {
            if (IsWatcherRunning)
            {
                if (_directoryWatcher != null)
                {
                    _directoryWatcher.Dispose();
                    _directoryWatcher = null;
                }
                IsWatcherRunning = false;
                ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Dir Watcher stopped."), IsUser = true });
            }
            else
            {
                var syncPath = WorkspaceRoot;
                if (!System.IO.Directory.Exists(syncPath))
                {
                    System.IO.Directory.CreateDirectory(syncPath);
                }

                _directoryWatcher = new LocalDirectoryWatcher(syncPath);
                _directoryWatcher.OnFileChanged += async (s, filePath) =>
                {
                    // Riporta nel thread UI per aggiornare l'interfaccia
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        ChatMessages.Add(new ChatMessage { Sender = Tx.T("Watcher"), Text = Tx.T("Change detected in {0}. Starting sync...", System.IO.Path.GetFileName(filePath)), IsUser = true });
                        IsBusy = true;
                        _isCurrentlyStreaming = false;
                        StatusMessage = Tx.T("Synchronizing file with ChatBot...");
                    });

                    var prompt = Tx.T("The following file ({0}) has been updated in the local filesystem. Read it and provide me a very short 1-line summary of what it contains now.", System.IO.Path.GetFileName(filePath));
                    var fileArray = new[] { filePath };
                    
                    var response = await _geminiService.SendAndAwaitResponseAsync(prompt, fileArray);
                    
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        // Non aggiungiamo più la risposta qui perché lo fa già l'evento di Streaming in tempo reale!
                        StatusMessage = Tx.T("Sync completed.");
                        _isCurrentlyStreaming = false;
                        IsBusy = false;
                    });
                };

                IsWatcherRunning = true;
                ChatMessages.Add(new ChatMessage { Sender = Tx.T("System"), Text = Tx.T("Watcher started on folder: {0}\nCreate or modify a file there to test automation!", syncPath), IsUser = true });
            }
        }

        [RelayCommand]
        private void OpenFile(string fullPath)
        {
            if (System.IO.File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
            }
        }

        [RelayCommand]
        private void OpenFolder(string fullPath)
        {
            if (System.IO.Directory.Exists(fullPath))
            {
                Process.Start("explorer.exe", $"\"{fullPath}\"");
            }
            else if (System.IO.File.Exists(fullPath))
            {
                Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
        }

        [RelayCommand]
        private void ChooseWorkspace()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Tx.T("Select workspace folder")
            };
            if (dialog.ShowDialog() == true)
            {
                WorkspaceRoot = dialog.FolderName;
                RefreshWorkspaceTree();

                if (ActiveSession != null)
                {
                    ActiveSession.WorkspacePath = WorkspaceRoot;
                    _ = CloudServiceLocator.Session.SaveSessionAsync(ActiveSession);
                }
            }
        }

        private void RefreshWorkspaceTree()
        {
            _isInternalProfileChange = true;
            try
            {
                WorkspaceNodes.Clear();

                if (string.IsNullOrEmpty(WorkspaceRoot) || !System.IO.Directory.Exists(WorkspaceRoot))
                    return;
                
                var rootNode = new FileTreeNode { Name = "Workspace", FullPath = WorkspaceRoot, IsDirectory = true };
                PopulateTree(WorkspaceRoot, rootNode);
                WorkspaceNodes.Add(rootNode);

                // Scansione profili SQL
                AvailableSqlProfiles.Clear();
                try 
                {
                    var sqlFiles = System.IO.Directory.GetFiles(WorkspaceRoot, "*.sqlprofile", System.IO.SearchOption.AllDirectories);
                    foreach (var file in sqlFiles)
                    {
                        try
                        {
                            var json = System.IO.File.ReadAllText(file);
                            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var profile = System.Text.Json.JsonSerializer.Deserialize<Models.SqlProfile>(json, options);
                            if (profile != null)
                            {
                                profile.FilePath = file;
                                profile.Name = System.IO.Path.GetFileNameWithoutExtension(file);
                                // Decifra la ConnectionString se è protetta con DPAPI
                                if (DpapiHelper.IsProtected(profile.ConnectionString))
                                    profile.ConnectionString = DpapiHelper.Unprotect(profile.ConnectionString);
                                AvailableSqlProfiles.Add(profile);
                            }
                        }
                        catch { /* ignora profili non validi */ }
                    }
                    if (AvailableSqlProfiles.Count > 0 && SelectedSqlProfile == null)
                    {
                        SelectedSqlProfile = AvailableSqlProfiles[0];
                    }
                    OnPropertyChanged(nameof(HasSqlProfiles));
                }
                catch { /* ignora errori di directory mancante */ }
            }
            finally
            {
                _isInternalProfileChange = false;
            }
        }

        [RelayCommand]
        private void RefreshFolder(FileTreeNode node)
        {
            if (node == null || !node.IsDirectory) return;
            node.Children.Clear();
            PopulateTree(node.FullPath, node);
        }

        private void PopulateTree(string dir, FileTreeNode node)
        {
            try
            {
                foreach (var d in System.IO.Directory.GetDirectories(dir))
                {
                    var childNode = new FileTreeNode { Name = System.IO.Path.GetFileName(d), FullPath = d, IsDirectory = true };
                    PopulateTree(d, childNode);
                    node.Children.Add(childNode);
                }
                foreach (var f in System.IO.Directory.GetFiles(dir))
                {
                    node.Children.Add(new FileTreeNode { Name = System.IO.Path.GetFileName(f), FullPath = f, IsDirectory = false });
                }
            }
            catch { }
        }

        // ─── Language Selector ──────────────────────────────────────────────────

        /// <summary>
        /// Map of known culture codes → flag emoji + label.
        /// Add more cultures here as needed.
        /// </summary>
        private static readonly Dictionary<string, (string Flag, string Label)> _cultureInfo = new()
        {
            ["en-US"] = ("🇬🇧", "English"),
            ["en-GB"] = ("🇬🇧", "English (UK)"),
            ["it-IT"] = ("🇮🇹", "Italiano"),
            ["de-DE"] = ("🇩🇪", "Deutsch"),
            ["fr-FR"] = ("🇫🇷", "Français"),
            ["es-ES"] = ("🇪🇸", "Español"),
            ["pt-BR"] = ("🇧🇷", "Português"),
            ["nl-NL"] = ("🇳🇱", "Nederlands"),
            ["pl-PL"] = ("🇵🇱", "Polski"),
            ["ja-JP"] = ("🇯🇵", "日本語"),
            ["zh-CN"] = ("🇨🇳", "中文"),
        };

        private bool _suppressLanguageSave;

       private void InitLanguages()
        {

            AvailableLanguages.Clear();
            // English is always available (it's the key language — no entry needed in txd)
            AvailableLanguages.Add(new Models.LanguageOption { Code = "en-US", Display = "🇬🇧 English" });

            // Parse languages.txd to discover additional cultures
            try
            {
                string txdPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "localization", "languages.txd");

                if (System.IO.File.Exists(txdPath))
                {
                    var xdoc = XDocument.Load(txdPath);
                    var cultures = xdoc.Descendants("culture")
                        .Select(c => c.Attribute("name")?.Value)
                        .Where(c => !string.IsNullOrEmpty(c) && c != "en-US" && c != "en-GB")
                        .Distinct()
                        .ToList();

                    foreach (var code in cultures)
                    {
                        if (_cultureInfo.TryGetValue(code!, out var info))
                            AvailableLanguages.Add(new Models.LanguageOption
                                { Code = code!, Display = $"{info.Flag} {info.Label}" });
                        else
                            AvailableLanguages.Add(new Models.LanguageOption
                                { Code = code!, Display = $"🌐 {code}" });
                    }
                }
            }
            catch { /* if txd is missing just show English */ }

            // Restore last saved language — suppress save during init
            var settings = _settingsService.Load();
            var savedLang = AvailableLanguages.FirstOrDefault(l => l.Code == settings.Language)
                            ?? AvailableLanguages[0];

            _suppressLanguageSave = true;
            SelectedLanguage = savedLang;   // uses the generated property — no MVVMTK0034
            Tx.SetCulture(SelectedLanguage.Code);
            _suppressLanguageSave = false;
        }

        //partial void OnSelectedLanguageChanged(Models.LanguageOption? value)
        //{
        //    // Se value è null o stiamo inizializzando, non fare nulla
        //    if (value == null || _suppressLanguageSave) return;

        //    // 1. Cambia la cultura di TxLib usando il PARAMETRO REALE, non la proprietà della classe
        //    Tx.SetCulture(value.Code);

        //    // 2. Salva nei settings in modo isolato, senza rileggere o triggerare eventi della UI
        //    try
        //    {
        //        var settings = _settingsService.Load();
        //        if (settings.Language != value.Code)
        //        {
        //            settings.Language = value.Code;
        //            _settingsService.Save(settings);
        //        }
        //    }
        //    catch
        //    {
        //        // Evita che un blocco di I/O sul file dei settings rompa il thread visivo
        //    }
        //}

        private async void UpdateAgentTranslations()
        {
            var oldRole = SelectedAgent?.RoleType ?? "Default";
            
            AvailableAgents.Clear();
            
            // Sync local agents and fetch the complete list from server
            if (CloudServiceLocator.Client != null && CloudServiceLocator.Client.IsConnected)
            {
                await SyncLocalCustomAgentsAsync();
                
                var communityAgents = await CloudServiceLocator.Client.GetCommunityAgentsAsync();
                foreach (var ca in communityAgents)
                {
                    // Translation support for legacy base agents that are now coming from the DB
                    string translatedName = Tx.T(ca.DisplayName);
                    string translatedDesc = Tx.T(string.IsNullOrEmpty(ca.Description) ? "Community Agent" : ca.Description);

                    AvailableAgents.Add(new AgentProfile 
                    {
                        Name = translatedName,
                        Description = translatedDesc,
                        RoleType = ca.AgentKey,
                        IconCode = string.IsNullOrEmpty(ca.Icon) ? "🌐" : ca.Icon,
                        Tools = ca.Tools,
                        Extractors = ca.Extractors
                    });
                }
            }
            else 
            {
                // Fallback offline minimale se il server non è raggiungibile
                AvailableAgents.Add(new AgentProfile { Name = Tx.T("Default Chatbot"), Description = Tx.T("Free conversation (Offline)"), RoleType = "Default", IconCode = "🤖" });
            }

            SelectedAgent = AvailableAgents.FirstOrDefault(a => a.RoleType == oldRole) ?? AvailableAgents.FirstOrDefault();
        }

        private async Task SyncLocalCustomAgentsAsync()
        {
            try
            {
                string customAgentsDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DWN.Bridge", "CustomAgents");

                if (!System.IO.Directory.Exists(customAgentsDir))
                {
                    System.IO.Directory.CreateDirectory(customAgentsDir);
                    return;
                }

                var files = System.IO.Directory.GetFiles(customAgentsDir, "*.md");
                foreach (var file in files)
                {
                    string content = await System.IO.File.ReadAllTextAsync(file);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(file);

                    // Extract basic metadata. A real manifest could be used in the future.
                    var req = new Shared.Models.SyncAgentRequest
                    {
                        AgentKey = fileName,
                        DisplayName = fileName.Replace("_", " "),
                        Icon = "📝",
                        Description = Tx.T("Local Custom Agent"),
                        MarkdownContent = content
                    };

                    await CloudServiceLocator.Client.SyncCommunityAgentAsync(req);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error syncing custom agents: {ex.Message}");
            }
        }

        partial void OnSelectedLanguageChanged(Models.LanguageOption? value)
        {
            System.Diagnostics.Debug.WriteLine($"[LANG] Method triggered. Parameter 'value': {value?.Code} | Property 'SelectedLanguage': {SelectedLanguage?.Code}");
            if (value == null || _suppressLanguageSave) return;

            // 1. Allinea IMMEDIATAMENTE la cultura del thread .NET corrente con la combo
            var culture = System.Globalization.CultureInfo.GetCultureInfo(value.Code);
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

            Tx.SetCulture(value.Code);
            UpdateAgentTranslations();
            var settings = _settingsService.Load();
            settings.Language = value.Code;
            _settingsService.Save(settings);
            
            _lastInjectedRole = string.Empty;
        }

        partial void OnSelectedAgentChanged(AgentProfile? value)
        {
            if (value != null && ActiveSession != null)
            {
                ActiveSession.AgentRoleType = value.RoleType;
                ActiveSession.AgentName = value.Name;
                _ = CloudServiceLocator.Session.SaveSessionAsync(ActiveSession);
            }
        }

        partial void OnSelectedSqlProfileChanged(SqlProfile? value)
        {
            if (ActiveSession != null && !_isInternalProfileChange)
            {
                ActiveSession.SelectedSqlProfilePath = value?.FilePath ?? string.Empty;
                _ = CloudServiceLocator.Session.SaveSessionAsync(ActiveSession);
            }
        }

        public async Task SaveActiveSessionOnCloseAsync()
        {
            var tasks = new List<Task>();

            if (ActiveSession != null)
            {
                SaveCurrentMessagesToSession(ActiveSession);
                tasks.Add(CloudServiceLocator.Session.SaveSessionAsync(ActiveSession));
            }

            // Pulisce eventuali sessioni orfane (create da "New Chat" ma mai utilizzate, quindi senza GeminiId)
            var emptySessions = ChatHistory.Where(s => string.IsNullOrEmpty(s.GeminiId)).ToList();
            foreach (var s in emptySessions)
            {
                tasks.Add(CloudServiceLocator.Session.RemoveAsync(s.ServerId));
            }

            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
            }
        }

        // Metodi helper per REPLACE rimossi: le modifiche ai file avvengono sempre per riscrittura totale.
    }
}

