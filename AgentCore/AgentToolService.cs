using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AIBridge.AgentCore
{
    public static class AgentToolService
    {
        /// <summary>
        /// Callback invocato dalla UI per richiedere l'approvazione di un RUN_COMMAND.
        /// Parametri: command (string), workspaceRoot (string).
        /// Ritorna: Dialogs.CommandApprovalResult.
        /// </summary>
        public static Func<string, string, AIBridge.Dialogs.CommandApprovalResult>? ApprovalCallback { get; set; }

        /// <summary>
        /// Insieme di chiavi "command|dir" per cui l'utente ha scelto "Esegui sempre".
        /// Formato chiave: "&lt;comando_normalizzato&gt;|&lt;workspace_root_lower&gt;"
        /// </summary>
        private static readonly HashSet<string> _alwaysAllowed = new(StringComparer.OrdinalIgnoreCase);

        // ─────────────────────────────────────────────────────────────────────
        // BLACKLIST: comandi SEMPRE bloccati, nessuna approvazione possibile.
        // Copre azioni irreversibili o potenzialmente devastanti.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly (Regex Pattern, string Reason)[] _blacklist = new (Regex, string)[]
        {
            // ── DISTRUZIONE DISCO ──────────────────────────────────────────
            (new Regex(@"\bformat\b\s+[a-z]:", RegexOptions.IgnoreCase),
             "FORMAT può formattare un'intera unità disco. Operazione irreversibile."),

            (new Regex(@"\bdiskpart\b", RegexOptions.IgnoreCase),
             "DISKPART può ripartizionare e formattare dischi. Operazione irreversibile."),

            (new Regex(@"\bcipher\b.*\/w\b", RegexOptions.IgnoreCase),
             "CIPHER /W sovrascrive lo spazio libero del disco. Può causare perdita di dati."),

            // ── CANCELLAZIONE MASSIVA DI FILE ──────────────────────────────
            (new Regex(@"\bdel\b.+\/[SsFf]", RegexOptions.IgnoreCase),
             "DEL con /S o /F elimina file ricorsivamente o forzatamente. Potenzialmente irreversibile."),

            (new Regex(@"\b(rd|rmdir)\b.+\/[Ss]", RegexOptions.IgnoreCase),
             "RD/RMDIR con /S elimina un'intera directory e tutto il suo contenuto. Irreversibile."),

            (new Regex(@"\brm\b.+(-[a-zA-Z]*r[a-zA-Z]*f|-[a-zA-Z]*f[a-zA-Z]*r|--force|--recursive)", RegexOptions.IgnoreCase),
             "RM con -rf o --force --recursive elimina file ricorsivamente senza conferma. Irreversibile."),

            (new Regex(@"\bRemove-Item\b.+(-Recurse|-Force|-rf|-fr)", RegexOptions.IgnoreCase),
             "Remove-Item -Recurse -Force (PowerShell) elimina file/directory senza conferma. Irreversibile."),

            // ── REGISTRO DI SISTEMA ────────────────────────────────────────
            (new Regex(@"\breg\b.+delete\b", RegexOptions.IgnoreCase),
             "REG DELETE rimuove chiavi dal Registro di sistema. Può rendere il sistema instabile."),

            (new Regex(@"\bregedit\b.+\/s\b", RegexOptions.IgnoreCase),
             "REGEDIT /S importa chiavi nel Registro silenziosamente. Può alterare il sistema operativo."),

            // ── RETE E FIREWALL ────────────────────────────────────────────
            (new Regex(@"\bnetsh\b.+(firewall|advfirewall).+(disable|off|delete)", RegexOptions.IgnoreCase),
             "Disabilitare il Firewall di Windows lascia il sistema esposto ad attacchi di rete."),

            (new Regex(@"\bnetsh\b.+int(erface)?.+delete", RegexOptions.IgnoreCase),
             "Eliminare interfacce di rete può isolare il sistema."),

            // ── ESCALATION PRIVILEGI / UTENTI ──────────────────────────────
            (new Regex(@"\bnet\b.+user\b.+(\/add|\/active:yes|\/passwordreq:no)", RegexOptions.IgnoreCase),
             "Creazione di utenti, attivazione di account nascosti o rimozione password. Rischio sicurezza critico."),

            (new Regex(@"\bnet\b.+localgroup\b.+administrators\b.+\/add\b", RegexOptions.IgnoreCase),
             "Aggiungere un utente al gruppo Administrators escala i privilegi. Rischio sicurezza critico."),

            (new Regex(@"\bwmic\b.+useraccount\b", RegexOptions.IgnoreCase),
             "WMIC useraccount consente di manipolare account utente. Rischio sicurezza."),

            // ── CONFIGURAZIONE DI AVVIO (BOOT) ────────────────────────────
            (new Regex(@"\bbcdedit\b", RegexOptions.IgnoreCase),
             "BCDEDIT modifica la configurazione di avvio del sistema. Un errore può rendere il PC non avviabile."),

            (new Regex(@"\bbootrec\b", RegexOptions.IgnoreCase),
             "BOOTREC modifica il Master Boot Record. Un errore può rendere il PC non avviabile."),

            // ── KILL DI PROCESSI CRITICI ───────────────────────────────────
            (new Regex(@"\btaskkill\b.+(csrss|lsass|winlogon|wininit|smss|services)\.exe", RegexOptions.IgnoreCase),
             "Terminare processi di sistema critici causerà un crash o BSOD immediato."),

            (new Regex(@"\btaskkill\b.+\/f\b.+\/im\b\s*\*", RegexOptions.IgnoreCase),
             "TASKKILL /F /IM * termina TUTTI i processi in esecuzione. Può causare perdita di dati non salvati."),

            // ── SPEGNIMENTO / RIAVVIO FORZATO ─────────────────────────────
            (new Regex(@"\bshutdown\b.+(\/f|\/r|\/s|\/h)", RegexOptions.IgnoreCase),
             "SHUTDOWN con parametri di forza può spegnere o riavviare il PC immediatamente."),

            (new Regex(@"\bwmic\b.+computersystem\b.+call\b.+shutdown", RegexOptions.IgnoreCase),
             "WMIC shutdown forza lo spegnimento del sistema."),

            (new Regex(@"\bStop-Computer\b|\bRestart-Computer\b", RegexOptions.IgnoreCase),
             "Stop-Computer/Restart-Computer (PowerShell) spegne o riavvia il sistema."),

            // ── ESECUZIONE CODICE REMOTO / PIPED ──────────────────────────
            (new Regex(@"(curl|wget|irm|Invoke-WebRequest|iwr)\b.+\|\s*(cmd|powershell|bash|sh|python|node|iex)", RegexOptions.IgnoreCase),
             "Scaricare ed eseguire codice remoto direttamente in pipe è un vettore di attacco classico. Bloccato."),

            (new Regex(@"\biex\b|\bInvoke-Expression\b", RegexOptions.IgnoreCase),
             "Invoke-Expression (IEX) esegue stringhe arbitrarie come codice PowerShell. Rischio injection critico."),

            (new Regex(@"\bpowershell\b.+(-enc(odedcommand)?|-e\s+[A-Za-z0-9+/=]{20,})", RegexOptions.IgnoreCase),
             "PowerShell con comandi codificati in Base64 è una tecnica di offuscamento malware. Sempre bloccato."),

            (new Regex(@"\bpowershell\b.+(-nop|-noninteractive|-w\s+hidden|-windowstyle\s+hidden)", RegexOptions.IgnoreCase),
             "PowerShell in modalità nascosta/non-interattiva è tipico di script malevoli. Sempre bloccato."),

            // ── POLICY DI ESECUZIONE POWERSHELL ───────────────────────────
            (new Regex(@"\bSet-ExecutionPolicy\b.+(Unrestricted|Bypass|Undefined)", RegexOptions.IgnoreCase),
             "Set-ExecutionPolicy Unrestricted/Bypass disabilita le protezioni di sicurezza di PowerShell."),

            (new Regex(@"\bEnable-PSRemoting\b", RegexOptions.IgnoreCase),
             "Enable-PSRemoting abilita l'esecuzione remota di comandi PowerShell sulla macchina."),

            // ── SERVIZI DI SISTEMA ─────────────────────────────────────────
            (new Regex(@"\bsc\b.+(delete|create|config)\b.+(system32|windows|defender|wdfilter|msmpeng)", RegexOptions.IgnoreCase),
             "Creazione, eliminazione o modifica di servizi di sistema critici (es. Windows Defender). Bloccato."),

            (new Regex(@"\bsc\b.+delete\b", RegexOptions.IgnoreCase),
             "SC DELETE elimina un servizio di Windows. Può rendere instabile il sistema."),

            // ── MANIPOLAZIONE ACL / PERMESSI CRITICI ──────────────────────
            (new Regex(@"\b(icacls|cacls|takeown)\b.+(system32|windows|program files|c:\\windows)", RegexOptions.IgnoreCase),
             "Modifica permessi su directory di sistema critiche. Può rendere il sistema inaccessibile."),

            // ── WBADMIN / SHADOW COPIES (antiransomware target) ───────────
            (new Regex(@"\bwbadmin\b.+delete\b", RegexOptions.IgnoreCase),
             "WBADMIN DELETE elimina backup di sistema o shadow copy. Tecnica comune dei ransomware."),

            (new Regex(@"\bvssadmin\b.+(delete|resize).+shadows", RegexOptions.IgnoreCase),
             "VSSADMIN delete shadows elimina le copie shadow (punti di ripristino). Tecnica ransomware comune."),

            (new Regex(@"\bvssadmin\b.+resize\b.+/maxsize", RegexOptions.IgnoreCase),
             "VSSADMIN resize riduce lo spazio per le shadow copy, ostacolando il ripristino del sistema."),
        };

        private static readonly Dictionary<string, Func<ToolRequest, string, Task<string>>> NativeHandlers = new(StringComparer.OrdinalIgnoreCase)
        {
            { "READ_FILE",       (req, root) => ExecuteReadFileAsync(req.Path, root) },
            { "LIST_DIR",        (req, root) => Task.FromResult(ExecuteListDir(req.Path, root)) },
            { "GREP_SEARCH",     (req, root) => ExecuteGrepSearchAsync(req.Path, req.Query, root) },
            { "WRITE_FILE",      (req, root) => ExecuteWriteFileAsync(req.Path, req.Content, root) },
            { "REPLACE_IN_FILE", (req, root) => ExecuteReplaceInFileAsync(req.Path, req.OldContent, req.NewContent, root) }
        };

        public static async Task<string> ProcessToolsAsync(string geminiResponse, string workspaceRoot, IEnumerable<AIBridge.Shared.Models.ToolDefinition>? agentTools = null)
        {
            var match = Regex.Match(geminiResponse, @"```tool\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (!match.Success)
                return string.Empty;

            string json = match.Groups[1].Value;

            ToolRequest? toolRequest = null;
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                toolRequest = System.Text.Json.JsonSerializer.Deserialize<ToolRequest>(json, options);
            }
            catch (System.Text.Json.JsonException)
            {
                // Fallback: il JSON contiene virgolette non escapate (es. percorsi Windows con spazi).
                // Proviamo a sanificarlo, poi re-parsiamo; se fallisce ancora usiamo l'estrazione regex.
                try
                {
                    string sanitized = SanitizeJson(json);
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    toolRequest = System.Text.Json.JsonSerializer.Deserialize<ToolRequest>(sanitized, options);
                }
                catch
                {
                    toolRequest = FallbackExtractToolRequest(json);
                }
            }

            if (toolRequest == null) return Unclassified.TxLib.Tx.T("Error: Could not parse tool JSON (even after sanitization).");

            var actionName = toolRequest.Action?.ToUpper();
            if (string.IsNullOrEmpty(actionName)) return "Error: Action missing.";

            var allTools = new List<AIBridge.Shared.Models.ToolDefinition>();
            if (App.ToolDictionary != null)
                allTools.AddRange(App.ToolDictionary.GetBaseTools());
            if (agentTools != null)
                allTools.AddRange(agentTools);

            var toolDef = allTools.FirstOrDefault(t => t.ActionName.Equals(actionName, StringComparison.OrdinalIgnoreCase));
            if (toolDef == null)
            {
                return $"Error: Unknown tool '{actionName}'. Not defined in agent capabilities.";
            }

            if (toolDef.ExecutionType == AIBridge.Shared.Models.ToolExecutionType.NativeCSharp)
            {
                if (NativeHandlers.TryGetValue(actionName, out var handler))
                {
                    return await handler(toolRequest, workspaceRoot);
                }
                return $"Error: Native handler for '{actionName}' is missing.";
            }
            else
            {
                string cmd = toolDef.CommandTemplate ?? "";
                
                // For generic RUN_COMMAND dove il parametro è fisso su "command"
                if (actionName == "RUN_COMMAND")
                {
                    if (string.IsNullOrWhiteSpace(toolRequest.Command))
                        return "Error: The 'command' field is missing or empty. Tool request failed due to truncated JSON.";
                    
                    cmd = cmd.Replace("{0}", toolRequest.Command);
                }
                else
                {
                    // Per i tool custom, estraiamo dinamicamente tutte le proprietà JSON ignorando "action"
                    try
                    {
                        var args = new List<string>();
                        // Se era stato usato il json sanificato, riparsiamo quello, altrimenti json originale
                        string jsonToParse = json;
                        try { System.Text.Json.JsonDocument.Parse(jsonToParse); } catch { jsonToParse = SanitizeJson(json); }
                        
                        using var doc = System.Text.Json.JsonDocument.Parse(jsonToParse);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (!prop.Name.Equals("action", StringComparison.OrdinalIgnoreCase) && 
                                !prop.Name.Equals("work_dir", StringComparison.OrdinalIgnoreCase))
                            {
                                string val = prop.Value.ToString() ?? "";
                                args.Add(val);
                                // Supporta placeholder nominativi, es. {city}
                                cmd = cmd.Replace($"{{{prop.Name}}}", val, StringComparison.OrdinalIgnoreCase);
                            }
                        }
                        
                        // Supporta placeholder posizionali, es. {0}, {1} in base all'ordine di apparizione
                        for (int i = 0; i < args.Count; i++)
                        {
                            cmd = cmd.Replace($"{{{i}}}", args[i]);
                        }
                    }
                    catch
                    {
                        // Fallback ai campi noti se il parsing generico fallisce
                        if (!string.IsNullOrEmpty(toolRequest.Command)) cmd = cmd.Replace("{0}", toolRequest.Command); 
                        else if (!string.IsNullOrEmpty(toolRequest.Path)) cmd = cmd.Replace("{0}", toolRequest.Path);
                        if (!string.IsNullOrEmpty(toolRequest.Query)) cmd = cmd.Replace("{1}", toolRequest.Query);
                    }
                }

                return await ExecuteRunCommandAsync(cmd, workspaceRoot, toolRequest.WorkDir);
            }
        }

        /// <summary>
        /// Sanitizza JSON malformato: escapa le virgolette doppie e i backslash non quotati
        /// all'interno dei valori stringa, che è il caso comune quando l'LLM genera percorsi Windows.
        /// </summary>
        private static string SanitizeJson(string json)
        {
            var result = new System.Text.StringBuilder(json.Length + 32);
            int i = 0;

            while (i < json.Length)
            {
                char c = json[i];

                if (c == '"')
                {
                    result.Append(c); // virgoletta di apertura
                    i++;

                    // Leggiamo il contenuto della stringa fino alla virgoletta di chiusura
                    while (i < json.Length)
                    {
                        char sc = json[i];

                        if (sc == '\\' && i + 1 < json.Length)
                        {
                            // Sequenza di escape già presente: passiamo attraverso
                            char next = json[i + 1];
                            if (next == '"' || next == '\\' || next == 'n' || next == 'r' ||
                                next == 't' || next == 'b' || next == 'f' || next == 'u' || next == '/')
                            {
                                result.Append(sc); result.Append(next); i += 2;
                            }
                            else
                            {
                                // Backslash non valido (es. \t Windows path) → escapiamo
                                result.Append("\\\\"); i++;
                            }
                        }
                        else if (sc == '\\')
                        {
                            // Backslash finale senza carattere successivo
                            result.Append("\\\\"); i++;
                        }
                        else if (sc == '"')
                        {
                            // È la chiusura della stringa JSON?
                            // Sì se dopo (saltando spazi/tab) viene :  , } ] \n \0
                            int j = i + 1;
                            while (j < json.Length && (json[j] == ' ' || json[j] == '\t')) j++;
                            char next = j < json.Length ? json[j] : '\0';

                            if (next == ':' || next == ',' || next == '}' || next == ']' ||
                                next == '\n' || next == '\r' || next == '\0')
                            {
                                result.Append(sc); i++; // chiusura legittima
                                break;
                            }
                            else
                            {
                                // Virgoletta embedded non escapata → escapiamo
                                result.Append("\\\""); i++;
                            }
                        }
                        else if (sc == '\n' || sc == '\r')
                        {
                            result.Append("\\n"); i++;
                            if (sc == '\r' && i < json.Length && json[i] == '\n') i++;
                        }
                        else
                        {
                            result.Append(sc); i++;
                        }
                    }
                }
                else
                {
                    result.Append(c); i++;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Ultimo tentativo: estrae i campi noti tramite regex senza dipendere dalla struttura JSON.
        /// </summary>
        private static ToolRequest? FallbackExtractToolRequest(string json)
        {
            var r = new ToolRequest();

            var m = Regex.Match(json, @"""action""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (m.Success) r.Action = m.Groups[1].Value;

            m = Regex.Match(json, @"""path""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (m.Success) r.Path = m.Groups[1].Value;

            m = Regex.Match(json, @"""query""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (m.Success) r.Query = m.Groups[1].Value;

            r.Command = ExtractComplexField(json, "command");
            r.Content = ExtractComplexField(json, "content");
            r.OldContent = ExtractComplexField(json, "old_content");
            r.NewContent = ExtractComplexField(json, "new_content");

            return r.Action != null ? r : null;
        }

        /// <summary>Estrae il valore di un campo che può contenere virgolette embedded.</summary>
        private static string? ExtractComplexField(string json, string fieldName)
        {
            var start = Regex.Match(json, $@"""{fieldName}""\s*:\s*""", RegexOptions.IgnoreCase);
            if (!start.Success) return null;

            int valueStart = start.Index + start.Length;
            int pos = valueStart;

            while (pos < json.Length)
            {
                int q = json.IndexOf('"', pos);
                if (q == -1) break;

                // È chiusura? → controlla che il prossimo non-spazio sia , } ] o fine
                int j = q + 1;
                while (j < json.Length && (json[j] == ' ' || json[j] == '\t' || json[j] == '\n' || json[j] == '\r')) j++;
                char next = j < json.Length ? json[j] : '\0';

                if (next == ',' || next == '}' || next == '"' || next == '\0')
                    return json.Substring(valueStart, q - valueStart);

                pos = q + 1;
            }

            return null;
        }

        private static async Task<string> ExecuteReadFileAsync(string path, string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) return Unclassified.TxLib.Tx.T("Error: path is required.");

            string fullPath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(workspaceRoot, path);

            if (!System.IO.File.Exists(fullPath))
                return Unclassified.TxLib.Tx.T("Error: File '{0}' not found.", fullPath);

            try
            {
                var lines = await System.IO.File.ReadAllLinesAsync(fullPath);
                if (lines.Length > 2000)
                {
                    return Unclassified.TxLib.Tx.T("Error: File too large ({0} lines). Read specific portions or use another tool.", lines.Length.ToString());
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== FILE: {path} ({lines.Length} righe) ===");
                sb.AppendLine(string.Join(Environment.NewLine, lines));
                sb.AppendLine($"=== FINE FILE ===");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return Unclassified.TxLib.Tx.T("Error reading file: {0}", ex.Message);
            }
        }

        private static string ExecuteListDir(string path, string workspaceRoot)
        {
            string targetDir = string.IsNullOrWhiteSpace(path) || path == "." ? workspaceRoot : path;
            string fullPath = System.IO.Path.IsPathRooted(targetDir) ? targetDir : System.IO.Path.Combine(workspaceRoot, targetDir);

            if (!System.IO.Directory.Exists(fullPath))
                return Unclassified.TxLib.Tx.T("Error: Directory '{0}' not found.", fullPath);

            try
            {
                var dirs = System.IO.Directory.GetDirectories(fullPath);
                var files = System.IO.Directory.GetFiles(fullPath);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Directory: {fullPath}");
                foreach (var d in dirs)
                {
                    string name = System.IO.Path.GetFileName(d);
                    if (name.StartsWith(".") || name == "bin" || name == "obj" || name == "node_modules") continue;
                    sb.AppendLine($"[DIR]  {name}/");
                }
                foreach (var f in files)
                {
                    sb.AppendLine($"[FILE] {System.IO.Path.GetFileName(f)}");
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return Unclassified.TxLib.Tx.T("Error listing directory: {0}", ex.Message);
            }
        }

        private static async Task<string> ExecuteGrepSearchAsync(string path, string query, string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(query)) return Unclassified.TxLib.Tx.T("Error: query is required.");

            string targetDir = string.IsNullOrWhiteSpace(path) || path == "." ? workspaceRoot : path;
            string fullPath = System.IO.Path.IsPathRooted(targetDir) ? targetDir : System.IO.Path.Combine(workspaceRoot, targetDir);

            if (!System.IO.Directory.Exists(fullPath))
                return Unclassified.TxLib.Tx.T("Error: Directory '{0}' not found.", fullPath);

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Search Results for '{query}':");
                int matchCount = 0;

                var files = System.IO.Directory.EnumerateFiles(fullPath, "*.*", System.IO.SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (matchCount > 50) break;

                    if (file.Contains("\\bin\\") || file.Contains("\\obj\\") || file.Contains("\\.git\\") || file.Contains("\\node_modules\\"))
                        continue;

                    var lines = await System.IO.File.ReadAllLinesAsync(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            string relPath = System.IO.Path.GetRelativePath(workspaceRoot, file);
                            sb.AppendLine($"{relPath}:{i + 1}: {lines[i].Trim()}");
                            matchCount++;
                            if (matchCount > 50) break;
                        }
                    }
                }

                if (matchCount == 0) sb.AppendLine("Nessun risultato trovato.");
                else if (matchCount > 50) sb.AppendLine("...[Risultati troncati per limite massimo di 50 match]...");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return Unclassified.TxLib.Tx.T("Error searching: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Verifica il comando contro la blacklist di operazioni sempre proibite.
        /// Ritorna (true, motivo) se il comando è bloccato, altrimenti (false, "").
        /// </summary>
        private static (bool Blocked, string Reason) IsBlacklisted(string command)
        {
            foreach (var (pattern, reason) in _blacklist)
            {
                if (pattern.IsMatch(command))
                    return (true, reason);
            }
            return (false, string.Empty);
        }

        private static async Task<string> ExecuteRunCommandAsync(string command, string workspaceRoot, string? workDirHint = null)
        {
            if (string.IsNullOrWhiteSpace(command)) return Unclassified.TxLib.Tx.T("Error: command is required.");
            if (string.IsNullOrWhiteSpace(workspaceRoot)) return Unclassified.TxLib.Tx.T("Error: workspaceRoot not set. Open a Workspace before using RUN_COMMAND.");

            // --- BLACKLIST: controllo prima di qualsiasi altra logica ---
            var (blocked, blockReason) = IsBlacklisted(command);
            if (blocked)
                return $"[COMMAND BLOCKED - DANGEROUS OPERATION]: {blockReason}\n" +
                       $"Attempted command: '{command}'\n" +
                       "Questa operazione è permanentemente disabilitata per motivi di sicurezza. Informa l'utente e suggerisci un'alternativa sicura.";

            // --- SANDBOX: determina la directory di esecuzione ---
            // Se il modello suggerisce una workdir, la accettiamo solo se è DENTRO il workspace.
            string execDir = workspaceRoot;
            if (!string.IsNullOrWhiteSpace(workDirHint))
            {
                string candidateFull = System.IO.Path.IsPathRooted(workDirHint)
                    ? workDirHint
                    : System.IO.Path.Combine(workspaceRoot, workDirHint);
                candidateFull = System.IO.Path.GetFullPath(candidateFull);
                string wsNorm = System.IO.Path.GetFullPath(workspaceRoot);
                if (candidateFull.StartsWith(wsNorm, StringComparison.OrdinalIgnoreCase)
                    && System.IO.Directory.Exists(candidateFull))
                {
                    execDir = candidateFull;
                }
                // Se è fuori workspace lo ignoriamo silenziosamente e usiamo la root.
            }

            // --- APPROVAZIONE UTENTE ---
            string alwaysKey = $"{command.Trim().ToLowerInvariant()}|{execDir.ToLowerInvariant()}";
            if (!_alwaysAllowed.Contains(alwaysKey))
            {
                if (ApprovalCallback == null)
                    return Unclassified.TxLib.Tx.T("Error: RUN_COMMAND richiede l'approvazione dell'utente, ma nessun callback di approvazione è registrato.");

                Dialogs.CommandApprovalResult approval = Dialogs.CommandApprovalResult.Deny;
                // Il callback deve essere invocato sul thread UI
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    approval = ApprovalCallback(command, execDir);
                });

                switch (approval)
                {
                    case Dialogs.CommandApprovalResult.Deny:
                        return Unclassified.TxLib.Tx.T("[COMMAND REJECTED]: The user did not authorize execution. Reply explaining your intent and ask for manual confirmation.");

                    case Dialogs.CommandApprovalResult.ExecuteAlways:
                        _alwaysAllowed.Add(alwaysKey);
                        break;

                    // ExecuteOnce: non aggiungiamo alla whitelist, eseguiamo una volta
                    case Dialogs.CommandApprovalResult.ExecuteOnce:
                    default:
                        break;
                }
            }

            // --- ESECUZIONE SANDBOXATA ---
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {command}")
                {
                    WorkingDirectory = execDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new System.Diagnostics.Process { StartInfo = processInfo };
                
                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();
                
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock(outputBuilder) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock(errorBuilder) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(15000); // Timeout 15 secondi come descritto nelle regole
                
                if (exited)
                {
                    // MSDN: To ensure that asynchronous event handling has been completed, 
                    // call the parameterless WaitForExit() overload after a true from WaitForExit(int).
                    process.WaitForExit();
                }

                string output, error;
                lock(outputBuilder) output = outputBuilder.ToString();
                lock(errorBuilder) error = errorBuilder.ToString();

                if (exited)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[Executed Command]: {command}");
                    sb.AppendLine($"[Directory]: {execDir}");
                    sb.AppendLine($"[Exit Code]: {process.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(output)) sb.AppendLine($"[Output]:\n{output.Trim()}");
                    if (!string.IsNullOrWhiteSpace(error))  sb.AppendLine($"[Error]:\n{error.Trim()}");

                    return sb.ToString().Trim();
                }
                else
                {
                    // Il comando è andato in timeout (probabilmente una GUI o un server in ascolto)
                    // Terminiamo solo il wrapper (cmd.exe) lasciando il processo figlio (la GUI) in esecuzione
                    // così l'utente può interagire con l'app generata.
                    try { process.Kill(); } catch { }
                    
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[Command TIMEOUT after 15 seconds]: {command}");
                    sb.AppendLine($"[Directory]: {execDir}");
                    sb.AppendLine($"[Exit Code]: N/A (Timeout)");
                    if (!string.IsNullOrWhiteSpace(output)) sb.AppendLine($"[Partial Output]:\n{output.Trim()}");
                    if (!string.IsNullOrWhiteSpace(error))  sb.AppendLine($"[Partial Error]:\n{error.Trim()}");
                    
                    return sb.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                return Unclassified.TxLib.Tx.T("Error executing command: {0}", ex.Message);
            }
        }

        private static async Task<string> ExecuteWriteFileAsync(string path, string content, string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) return Unclassified.TxLib.Tx.T("Error: path is required.");
            if (content == null) return Unclassified.TxLib.Tx.T("Error: content is required.");

            string fullPath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(workspaceRoot, path);

            try
            {
                // Backup automatico prima di sovrascrivere
                if (System.IO.File.Exists(fullPath))
                {
                    string backupPath = fullPath + ".bak";
                    System.IO.File.Copy(fullPath, backupPath, overwrite: true);
                }
                else
                {
                    // Crea la directory se non esiste
                    var dir = System.IO.Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);
                }

                await System.IO.File.WriteAllTextAsync(fullPath, content, System.Text.Encoding.UTF8);
                return Unclassified.TxLib.Tx.T("OK: File '{0}' successfully written ({1} characters).", path, content.Length.ToString());
            }
            catch (Exception ex)
            {
                return Unclassified.TxLib.Tx.T("Error writing file: {0}", ex.Message);
            }
        }

        private static async Task<string> ExecuteReplaceInFileAsync(string path, string oldContent, string newContent, string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) return Unclassified.TxLib.Tx.T("Error: path is required.");
            if (string.IsNullOrEmpty(oldContent)) return Unclassified.TxLib.Tx.T("Error: old_content is required.");
            if (newContent == null) return Unclassified.TxLib.Tx.T("Error: new_content is required.");

            string fullPath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(workspaceRoot, path);

            if (!System.IO.File.Exists(fullPath))
                return Unclassified.TxLib.Tx.T("Error: File '{0}' not found.", fullPath);

            try
            {
                string originalText = await System.IO.File.ReadAllTextAsync(fullPath);

                int occurrences = CountOccurrences(originalText, oldContent);
                if (occurrences == 0)
                    return $"Error: Il testo 'old_content' non è stato trovato nel file '{path}'. Verifica che la stringa da sostituire sia ESATTAMENTE identica al contenuto del file (inclusi spazi e a capo).";
                if (occurrences > 1)
                    return $"Warning: Il testo 'old_content' è stato trovato {occurrences} volte nel file '{path}'. Fornisci un contesto più ampio per renderlo univoco.";

                // Backup automatico
                string backupPath = fullPath + ".bak";
                System.IO.File.Copy(fullPath, backupPath, overwrite: true);

                string updatedText = originalText.Replace(oldContent, newContent, StringComparison.Ordinal);
                await System.IO.File.WriteAllTextAsync(fullPath, updatedText, System.Text.Encoding.UTF8);

                return $"OK: Sostituzione eseguita in '{path}'. Backup salvato in '{path}.bak'.";
            }
            catch (Exception ex)
            {
                return Unclassified.TxLib.Tx.T("Error replacing in file: {0}", ex.Message);
            }
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        private class ToolRequest
        {
            public string Action { get; set; }
            public string Path { get; set; }
            public string Query { get; set; }
            public string Command { get; set; }
            /// <summary>Directory di lavoro opzionale suggerita dal modello per RUN_COMMAND.</summary>
            [System.Text.Json.Serialization.JsonPropertyName("work_dir")]
            public string? WorkDir { get; set; }
            public string Content { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("old_content")]
            public string OldContent { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("new_content")]
            public string NewContent { get; set; }
        }
    }
}
