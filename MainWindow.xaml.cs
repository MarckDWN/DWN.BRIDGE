using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AIBridge;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        ChatListBox.CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Copy,
            (s, e) => 
            {
                var sb = new System.Text.StringBuilder();
                foreach (var item in ChatListBox.SelectedItems)
                {
                    if (item is Models.ChatMessage msg)
                    {
                        sb.AppendLine($"[{msg.Sender}]:");
                        sb.AppendLine(msg.Text);
                        if (msg.IsSqlResult && !string.IsNullOrEmpty(msg.FormattedTableText))
                        {
                            sb.AppendLine(msg.FormattedTableText);
                        }
                        sb.AppendLine();
                    }
                }
                if (sb.Length > 0)
                {
                    System.Windows.Clipboard.SetText(sb.ToString().TrimEnd());
                }
            },
            (s, e) => 
            {
                e.CanExecute = ChatListBox.SelectedItems.Count > 0;
            }
        ));
        
        // Auto-scroll chat to bottom
        this.Loaded += (s, e) => 
        {
            if (this.DataContext is ViewModels.MainViewModel vm)
            {
                vm.ChatMessages.CollectionChanged += (sender, args) =>
                {
                    if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    {
                        var scrollViewer = GetDescendantByType<ScrollViewer>(ChatListBox);
                        scrollViewer?.ScrollToBottom();
                    }
                };
            }
            
            // Setup Drag & Drop da TreeView a InputArea
            if (InputAreaGrid != null)
            {
                InputAreaGrid.AllowDrop = true;
                
                InputAreaGrid.PreviewDragOver += (sender, args) =>
                {
                    if (args.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        args.Effects = DragDropEffects.Copy;
                        args.Handled = true;
                    }
                    else
                    {
                        args.Effects = DragDropEffects.None;
                    }
                };

                InputAreaGrid.PreviewDrop += (sender, args) =>
                {
                    if (args.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        string[] files = (string[])args.Data.GetData(DataFormats.FileDrop);
                        if (this.DataContext is ViewModels.MainViewModel vmodel)
                        {
                            foreach(var f in files)
                            {
                                if (System.IO.File.Exists(f) && !vmodel.AttachedFiles.Contains(f))
                                {
                                    vmodel.AttachedFiles.Add(f);
                                }
                            }
                            vmodel.StatusMessage = $"{vmodel.AttachedFiles.Count} file allegati tramite trascinamento.";
                        }
                    }
                };

                // Ensure UI reflects correct auth status on startup
                ((ViewModels.MainViewModel)this.DataContext).UpdateAuthStatus();
            }
        };
    }
    
    private Point _startDragPoint;
    
    private void WorkspaceTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startDragPoint = e.GetPosition(null);
    }
    
    private void WorkspaceTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Point mousePos = e.GetPosition(null);
            Vector diff = _startDragPoint - mousePos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var treeView = sender as TreeView;
                if (treeView?.SelectedItem is AIBridge.Models.FileTreeNode node && !node.IsDirectory)
                {
                    DataObject dragData = new DataObject(DataFormats.FileDrop, new string[] { node.FullPath });
                    DragDrop.DoDragDrop(treeView, dragData, DragDropEffects.Copy);
                }
            }
        }
    }

    public static T GetDescendantByType<T>(Visual element) where T : Visual
    {
        if (element == null) return null;
        if (element.GetType() == typeof(T)) return element as T;
        T foundElement = null;
        if (element is FrameworkElement)
        {
            (element as FrameworkElement).ApplyTemplate();
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            Visual visual = VisualTreeHelper.GetChild(element, i) as Visual;
            foundElement = GetDescendantByType<T>(visual);
            if (foundElement != null) break;
        }
        return foundElement;
    }

    public static T GetAncestorByType<T>(DependencyObject element) where T : DependencyObject
    {
        if (element == null) return null;
        var parent = VisualTreeHelper.GetParent(element);
        if (parent == null) return null;
        if (parent is T p) return p;
        return GetAncestorByType<T>(parent);
    }

    private void ChatTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            
            var textBox = sender as DependencyObject;
            var listBoxItem = GetAncestorByType<ListBoxItem>(textBox);
            
            if (listBoxItem != null)
            {
                var args = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, e.ChangedButton)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = sender
                };
                listBoxItem.RaiseEvent(args);
            }
        }
    }

    private void MarkdownScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MdXaml.MarkdownScrollViewer mdViewer)
        {


            // 1. Configurazione iniziale dei Titoli (manteniamo la tua logica funzionante, ma corretta per il Foreground)
            if (mdViewer.Engine != null)
            {
                var baseForeground = System.Windows.Media.Brushes.White;

                Style MakeHeadingStyle(double fontSize)
                {
                    var style = new Style(typeof(System.Windows.Documents.Paragraph));
                    style.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, baseForeground));
                    style.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontSizeProperty, fontSize));
                    style.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.Bold));
                    return style;
                }

                mdViewer.Engine.Heading1Style = MakeHeadingStyle(24);
                mdViewer.Engine.Heading2Style = MakeHeadingStyle(20);
                mdViewer.Engine.Heading3Style = MakeHeadingStyle(18);
                mdViewer.Engine.Heading4Style = MakeHeadingStyle(16);
            }

            // 2. TRUCCO CRITICO PER LO STREAMING: Forza il ricalcolo del colore dei singoli backtick
            // ogni volta che il testo o il DataContext cambiano, o quando il documento viene rigenerato.
            ApplyInlineCodeGreenFix(mdViewer);

            mdViewer.DataContextChanged += (s, args) => ApplyInlineCodeGreenFix(mdViewer);

            // Questo intercetta il momento esatto in cui MdXaml sputa fuori il nuovo FlowDocument parsatizzato
            var propDescriptor = System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(MdXaml.MarkdownScrollViewer.DocumentProperty, typeof(MdXaml.MarkdownScrollViewer));

            if (propDescriptor != null)
            {
                propDescriptor.AddValueChanged(mdViewer, (s, args) => ApplyInlineCodeGreenFix(mdViewer));
            }

            // FIX ROTELLA MOUSE: Intercettiamo la rotella prima che MdXaml la blocchi internamente
            mdViewer.PreviewMouseWheel += (s, args) =>
            {
                if (!args.Handled)
                {
                    // Marchiamo l'evento come gestito per bloccare il tunneling interno di MdXaml
                    args.Handled = true;

                    // Generiamo un evento di scorrimento identico che fa Bubbling verso l'alto
                    var bubblingArgs = new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = mdViewer
                    };

                    // Spingiamo l'evento sulla ListBox / ScrollViewer genitore
                    if (mdViewer.Parent is UIElement parent)
                    {
                        parent.RaiseEvent(bubblingArgs);
                    }
                }
            };

            // Dopo aver renderizzato, coloriamo le tabelle
            mdViewer.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                ColorizeTables(mdViewer);
            }));
        }
    }

    private void ApplyInlineCodeGreenFix(MdXaml.MarkdownScrollViewer mdViewer)
    {
        if (mdViewer == null || mdViewer.Document == null) return;

        // Il tuo verde smeraldo brillante
        var verdeEvidenziato = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString("#2ECC71");

        // Scansioniamo i blocchi generati dall'engine di MdXaml
        foreach (var block in mdViewer.Document.Blocks)
        {
            if (block is System.Windows.Documents.Paragraph paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    // FIX: Usiamo .Source invece di .Name per controllare il nome del font in WPF
                    if (inline is System.Windows.Documents.Inline inlineElement &&
                        inlineElement.FontFamily != null &&
                        (inlineElement.FontFamily.Source.Contains("Consolas") || inlineElement.FontFamily.Source.Contains("Courier")))
                    {
                        // L'assegnazione locale via codice ha priorità su QUALSIASI stile hardcoded della libreria
                        inlineElement.Background = System.Windows.Media.Brushes.Transparent; // Rimuove l'inverted grigio
                        inlineElement.Foreground = verdeEvidenziato;                       // Forza il testo verde
                        inlineElement.FontWeight = FontWeights.SemiBold;
                    }
                }
            }
        }
    }

    private void ColorizeTables(MdXaml.MarkdownScrollViewer mdViewer)
    {
        if (mdViewer.Document == null) return;

        var headerBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#93BBD9"));
        var rowEvenBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9BA3C2"));
        var rowOddBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A1E26"));
        var textBrush = System.Windows.Media.Brushes.Black; // Usiamo nero per header chiaro e grigio chiaro

        foreach (var block in mdViewer.Document.Blocks)
        {
            var tables = GetVisualsRecursive<System.Windows.Documents.Table>(block);
            foreach (var table in tables)
            {
                int globalRowIndex = 0; // CONTATORE GLOBALE

                foreach (var rowGroup in table.RowGroups)
                {
                    foreach (var row in rowGroup.Rows)
                    {
                        foreach (var c in row.Cells)
                        {
                            if (globalRowIndex == 0)
                            {
                                // Riga 0: Header
                                c.Background = headerBrush;
                                c.Foreground = System.Windows.Media.Brushes.Black; // Contrasto su azzurro
                                c.FontWeight = FontWeights.Bold;
                            }
                            else
                            {
                                // Righe successive: alternanza dati
                                bool isEven = (globalRowIndex % 2 == 0);
                                c.Background = isEven ? rowEvenBrush : rowOddBrush;
                                c.Foreground = System.Windows.Media.Brushes.White;
                            }
                        }
                        globalRowIndex++; // Incrementa sempre, ignorando il gruppo
                    }
                }
            }
        }
    }
    // Helper per trovare i figli di un certo tipo nel FlowDocument
    private IEnumerable<T> GetVisuals<T>(System.Windows.DependencyObject root) where T : System.Windows.Documents.TextElement
    {
        if (root is T element) yield return element;

        // Scansione ricorsiva (adattata per FlowDocument/BlockCollection)
        if (root is MdXaml.MarkdownScrollViewer sv && sv.Document != null)
            foreach (var block in sv.Document.Blocks)
                foreach (var res in GetVisualsRecursive<T>(block)) yield return res;
    }

    private IEnumerable<T> GetVisualsRecursive<T>(System.Windows.Documents.Block block) where T : System.Windows.Documents.TextElement
    {
        if (block is T element) yield return element;
        if (block is System.Windows.Documents.Table table)
            yield return (T)(System.Windows.Documents.TextElement)table;

        // Aggiungere logica se necessario per navigare in Section o List
    }
    private void xxxMarkdownScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {

        return;
        if (sender is MdXaml.MarkdownScrollViewer mdViewer && mdViewer.Engine != null)
        {
            var baseForeground = System.Windows.Media.Brushes.White;

            Style MakeHeadingStyle(double fontSize)
            {
                var style = new Style(typeof(System.Windows.Documents.Paragraph));
                style.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, baseForeground));
                style.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontSizeProperty, fontSize));
                style.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.Bold));
                style.Setters.Add(new Setter(System.Windows.Documents.Block.MarginProperty, new Thickness(0, 10, 0, 5)));
                return style;
            }

            mdViewer.Engine.Heading1Style = MakeHeadingStyle(24);
            mdViewer.Engine.Heading2Style = MakeHeadingStyle(20);
            mdViewer.Engine.Heading3Style = MakeHeadingStyle(18);
            mdViewer.Engine.Heading4Style = MakeHeadingStyle(16);

            // ── Code Block (``` ... ```) ──────────────────────────────────────────────
            // MdXaml renderizza i blocchi codice come Paragraph, non Section.
            // Background: dark come il tema; Foreground: BIANCO per leggibilità garantita.
            var codeBlockBg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1A, 0x1E, 0x26)); // quasi-nero, come Gemini
            var codeBlockFg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xD4, 0xF1, 0xBE)); // verde-bianco come il terminale

            var codeBlockStyle = new Style(typeof(System.Windows.Documents.Paragraph));
            codeBlockStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.BackgroundProperty, codeBlockBg));
            codeBlockStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, codeBlockFg));
            codeBlockStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas, Courier New, monospace")));
            codeBlockStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontSizeProperty, 12.5));
            codeBlockStyle.Setters.Add(new Setter(System.Windows.Documents.Block.PaddingProperty, new Thickness(12, 8, 12, 8)));
            codeBlockStyle.Setters.Add(new Setter(System.Windows.Documents.Block.MarginProperty, new Thickness(0, 4, 0, 4)));
            mdViewer.Engine.CodeBlockStyle = codeBlockStyle;

            /* ── Inline Code (`...`) ───────────────────────────────────────────────────
            var inlineCodeBg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2D, 0x33, 0x3B));
            var inlineCodeFg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF0, 0xC6, 0x74)); // giallo-oro come backtick Gemini

            var inlineCodeStyle = new Style(typeof(System.Windows.Documents.Span));
            inlineCodeStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.BackgroundProperty, inlineCodeBg));
            inlineCodeStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, inlineCodeFg));
            inlineCodeStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas, Courier New, monospace")));
            mdViewer.Engine.CodeStyle = inlineCodeStyle;
            */
            // ── Inline Code (`...`) ───────────────────────────────────────────────────

            // Modificato per rimuovere l'effetto invertito e fare il testo verde brillante
            var inlineCodeBg = System.Windows.Media.Brushes.Transparent; // <-- Via lo sfondo grigio!
            var inlineCodeFg = (System.Windows.Media.SolidColorBrush)new BrushConverter().ConvertFromString("#2ECC71"); // <-- Il tuo verde smeraldo

            var inlineCodeStyle = new Style(typeof(System.Windows.Documents.Span));
            inlineCodeStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.BackgroundProperty, inlineCodeBg));
            inlineCodeStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, inlineCodeFg));
            inlineCodeStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas, Courier New, monospace")));
            inlineCodeStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.SemiBold)); // Un filo più spesso per farlo risaltare

            mdViewer.Engine.CodeStyle = inlineCodeStyle;
            // Aggiorna il binding senza distruggerlo!
            var binding = mdViewer.GetBindingExpression(MdXaml.MarkdownScrollViewer.MarkdownProperty);
            binding?.UpdateTarget();
        }
    }

    private void PromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Intercetta Ctrl+V solo se gli appunti contengono un'immagine (non testo)
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Clipboard.ContainsImage() && !Clipboard.ContainsText())
            {
                e.Handled = true; // impedisce il paste di testo vuoto nella TextBox

                try
                {
                    var bitmapSource = Clipboard.GetImage();
                    if (bitmapSource == null) return;

                    // Salva l'immagine in una cartella temporanea dentro il workspace o AppData
                    string tempDir = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "AIBridge_ClipboardImages");
                    System.IO.Directory.CreateDirectory(tempDir);

                    string fileName = $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                    string filePath = System.IO.Path.Combine(tempDir, fileName);

                    // Salva come PNG
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                    using (var stream = System.IO.File.OpenWrite(filePath))
                    {
                        encoder.Save(stream);
                    }

                    // Aggiunge il file agli allegati tramite ViewModel
                    if (this.DataContext is ViewModels.MainViewModel vm)
                    {
                        if (!vm.AttachedFiles.Contains(filePath))
                        {
                            vm.AttachedFiles.Add(filePath);
                            vm.StatusMessage = $"🖼️ Immagine dagli appunti allegata: {fileName}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Clipboard Paste] Errore: {ex.Message}");
                }
            }
            // Se contiene testo (o entrambi), lascia fare il paste normale
        }
    }

    private void HistoryItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Doppio click: carica la sessione
            if (sender is FrameworkElement fe && fe.DataContext is Models.ChatSession session)
            {
                if (this.DataContext is ViewModels.MainViewModel vm)
                {
                    vm.LoadSessionCommand.Execute(session);
                    e.Handled = true;
                }
            }
        }
    }

    private bool _isClosing = false;
    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) return;

        if (this.DataContext is ViewModels.MainViewModel vm)
        {
            e.Cancel = true;
            _isClosing = true;

            Mouse.OverrideCursor = Cursors.Wait;
            vm.StatusMessage = Unclassified.TxLib.Tx.T("Saving and cleaning up sessions... Please wait.");

            try
            {
                await vm.SaveActiveSessionOnCloseAsync();
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Application.Current.Shutdown();
            }
        }
    }

    private async void LoginEmail_Click(object sender, RoutedEventArgs e)
    {
        var client = AIBridge.Services.CloudServiceLocator.Client;
        var vm = this.DataContext as ViewModels.MainViewModel;

        if (client.AuthMode == "oauth" || client.AuthMode == "email")
        {
            var res = AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("You are already logged in. Do you want to log out and return to Guest mode?"), Unclassified.TxLib.Tx.T("Logout"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                string hwid = new AIBridge.Services.HwidService().ComputeHwid();
                bool ok = await client.LogoutAsync(hwid);
                if (ok && vm != null)
                {
                    vm.UpdateAuthStatus();
                    vm.StatusMessage = Unclassified.TxLib.Tx.T("Logout successful. You are back in Guest mode.");
                    await vm.ReloadSessionsAsync();
                }
            }
            return;
        }

        var dialog = new MagicLinkLoginDialog(client);
        dialog.Owner = this;
        
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Token))
        {
            bool linked = await client.LinkAccountAsync(dialog.Token);
            if (linked)
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Login via Email and Account Linking successfully completed!"), Unclassified.TxLib.Tx.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                
                if (vm != null)
                {
                    vm.UpdateAuthStatus();
                    await vm.ReloadSessionsAsync();
                    await vm.SyncSettingsAsync();
                }
            }
            else
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Account Linking failed on server."), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var client = AIBridge.Services.CloudServiceLocator.Client;
        var vm = this.DataContext as ViewModels.MainViewModel;

        if (client.AuthMode == "oauth" || client.AuthMode == "email")
        {
            var res = AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("You are already logged in via OAuth. Do you want to log out and return to Guest mode?"), Unclassified.TxLib.Tx.T("Logout"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                string hwid = new AIBridge.Services.HwidService().ComputeHwid();
                bool ok = await client.LogoutAsync(hwid);
                if (ok && vm != null)
                {
                    vm.UpdateAuthStatus();
                    vm.StatusMessage = Unclassified.TxLib.Tx.T("Logout successful. You are back in Guest mode.");
                    await vm.ReloadSessionsAsync(); // Carica le sessioni guest (vuote)
                }
            }
            return;
        }

        var startResponse = await client.StartOAuthAsync("github"); 
        if (startResponse == null)
        {
            AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Error connecting to server for login."), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBridge", "WebView2");
        var dialog = new OAuthLoginDialog(startResponse.AuthorizationUrl, userDataFolder);
        dialog.Owner = this;
        
        if (dialog.ShowDialog() == true)
        {
            string code = dialog.AuthorizationCode;
            string state = dialog.ReturnedState;

            if (state != startResponse.State)
            {
                 AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Security Error: CSRF State mismatch."), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            var result = await client.OAuthCallbackAsync("github", code, state);
            if (!string.IsNullOrEmpty(result.Token))
            {
                bool linked = await client.LinkAccountAsync(result.Token);
                if (linked)
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("OAuth Login and Account Linking successfully completed!"), Unclassified.TxLib.Tx.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    if (vm != null)
                    {
                        vm.UpdateAuthStatus();
                        vm.StatusMessage = Unclassified.TxLib.Tx.T("OAuth login completed and history synchronized.");
                        await vm.ReloadSessionsAsync();
                    }
                }
                else
                {
                    AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Login successful but Account Linking failed."), Unclassified.TxLib.Tx.T("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                AIBridge.Dialogs.CustomMessageBox.Show(Unclassified.TxLib.Tx.T("Error exchanging OAuth token with the server:\n{0}", result.Error), Unclassified.TxLib.Tx.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ShowSplash_Click(object sender, RoutedEventArgs e)
    {
        var about = new Dialogs.AboutDialog();
        about.Owner = this;
        about.ShowDialog();
    }

    private void MyAgents_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.CommunityAgentsDialog();
        dialog.Owner = this;
        dialog.ShowDialog();
    }
}
