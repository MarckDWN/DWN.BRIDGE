using AIBridge.Models;

namespace AIBridge.AgentCore
{
    public static class MarkdownCodeExtractor
    {
        public static MultiFileEditCommand Parse(string response)
        {
            var command = new MultiFileEditCommand();

            bool explicitTruncation = response.Contains("[TRUNCATED]") || response.Contains("[RESPONSE_TRUNCATED]");

            // Match all file start tags dynamically: e.g., @@@FILE: path @@@, @@A_FILE: path @@, @@FILE: path @@
            var startTagRegex = new System.Text.RegularExpressions.Regex(
                @"@+(?:[A-Za-z]_)?FILE:\s*([^\s@]+)\s*@+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var matches = startTagRegex.Matches(response);

            var fileBlocks = new List<(int startIdx, int contentStartIdx, string path)>();

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                // ReverseMarkdown esegue l'escaping dei caratteri speciali Markdown (es. \_ → _);
                // senza unescape, "sort\_bookmarks.py" diventerebbe la sottocartella "sort\_bookmarks.py" su Windows.
                string path = match.Groups[1].Value.Trim()
                    .Replace("\\_", "_")
                    .Replace("\\*", "*")
                    .Replace("\\-", "-")
                    .Replace("\\.", ".");
                // Ignora se l'agente sta solo citando le regole del prompt ("..." o "path" o vuoto)
                if (path == "..." || path.Contains("path/to/") || path == "path" || string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }
                fileBlocks.Add((match.Index, match.Index + match.Length, path));
            }

            if (fileBlocks.Count == 0)
            {
                command.Message = response.Trim();
                if (explicitTruncation)
                {
                    command.IsTruncated = true;
                }
                return command;
            }

            int currentTextPtr = 0;
            System.Text.StringBuilder messageBuilder = new System.Text.StringBuilder();

            for (int i = 0; i < fileBlocks.Count; i++)
            {
                var block = fileBlocks[i];
                int currentStartIndex = block.startIdx;
                int contentStart = block.contentStartIdx;
                string filePath = block.path;

                // Extract text before the file block
                if (currentStartIndex > currentTextPtr)
                {
                    string preText = response.Substring(currentTextPtr, currentStartIndex - currentTextPtr).Trim();
                    if (!string.IsNullOrEmpty(preText))
                    {
                        if (messageBuilder.Length > 0) messageBuilder.AppendLine("\n");
                        messageBuilder.Append(preText);
                    }
                }

                int contentEnd = response.Length;
                int nextFileIndex = (i + 1 < fileBlocks.Count) ? fileBlocks[i + 1].startIdx : response.Length;

                int endFileIndex = -1;
                int currentEndTagLength = 0;

                // Match any variation of end tag: e.g. @@@END_FILE@@@, @@END_FILE@@, @@@END@@@, @@END@@
                var endTagRegex = new System.Text.RegularExpressions.Regex(
                    @"@+END(?:\\?_FILE)?@+",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var matchEnd = endTagRegex.Match(response.Substring(contentStart));
                if (matchEnd.Success)
                {
                    endFileIndex = contentStart + matchEnd.Index;
                    currentEndTagLength = matchEnd.Length;
                }

                bool isBlockUnclosed = false;
                if (endFileIndex != -1 && endFileIndex < nextFileIndex)
                {
                    contentEnd = endFileIndex;
                    currentTextPtr = endFileIndex + currentEndTagLength;
                }
                else
                {
                    contentEnd = nextFileIndex;
                    currentTextPtr = nextFileIndex;
                    isBlockUnclosed = true;
                }

                string content = response.Substring(contentStart, contentEnd - contentStart);
                content = TrimNewLines(content);

                // Rimuove eventuale intestazione "Snippet di codice" / "Code snippet" inserita dalla GUI WebChat prima del code block
                content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*(Snippet di codice|Code snippet)\s*[\r\n]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Strip all lines that contain only markdown code block wrappers (``` or ```csharp)
                content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*```[a-zA-Z0-9#+-]*\s*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);

                content = TrimNewLines(content);

                // Rimuove l'etichetta testuale della lingua (es. "XML", "C#", "Snippet di codice")
                content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*(Snippet di codice|Code snippet|XML|C#|CSHARP|JSON|BASH|POWERSHELL|HTML|CSS|JS|JAVASCRIPT|TYPESCRIPT|TS|SQL|PYTHON|MARKDOWN|MD|TXT|TEXT)\s*[\r\n]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                content = TrimNewLines(content);

                // --- SANITIZZAZIONE ARTEFATTI MARKDOWN ---
                // 1. Rimuove l'escaping dei caratteri speciali del markdown (es. \_ -> _, \* -> *)
                content = content.Replace("\\_", "_");
                content = content.Replace("\\*", "*");

                // 2. Rimuove prefissi di intestazione markdown inseriti erroneamente davanti ai commenti (es. "## --" -> "--")
                content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*#+\s*(--|//|#)", "$1", System.Text.RegularExpressions.RegexOptions.Multiline);

                content = TrimNewLines(content);

                if (isBlockUnclosed || explicitTruncation)
                {
                    command.IsTruncated = true;
                    command.TruncatedFilePath = filePath;

                    // Estrae le ultime 5 righe per passarle come contesto
                    var lines = content.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
                    int linesToTake = System.Math.Min(5, lines.Length);
                    var lastLinesList = new List<string>();
                    for (int l = lines.Length - linesToTake; l < lines.Length; l++)
                    {
                        lastLinesList.Add(lines[l]);
                    }
                    command.TruncatedLastLines = string.Join("\n", lastLinesList);
                }

                command.Files.Add(new FileEditCommand
                {
                    FilePath = filePath,
                    NewContent = content
                });
            }

            // Extract any remaining text after the last file block
            if (currentTextPtr < response.Length)
            {
                string postText = response.Substring(currentTextPtr).Trim();
                if (!string.IsNullOrEmpty(postText))
                {
                    command.PostMessage = postText;
                }
            }

            command.Message = messageBuilder.ToString().Trim();
            return command;
        }

        private static string TrimNewLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            int start = 0;
            while (start < text.Length && (text[start] == '\r' || text[start] == '\n'))
                start++;

            int end = text.Length - 1;
            while (end >= start && (text[end] == '\r' || text[end] == '\n'))
                end--;

            if (start > end) return string.Empty;
            return text.Substring(start, end - start + 1);
        }

        private static readonly HashSet<string> XmlExtensions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ".csproj", ".props", ".targets", ".xml", ".xaml", ".config", ".txd", ".manifest", ".resx"
        };

        /// <summary>
        /// Valida strutturalmente il contenuto estratto prima di sovrascrivere il file su disco.
        /// Impedisce che file XML/.csproj privi di tag (es. quando la WebChat ha mangiato i tag &lt;...&gt;)
        /// o file vuoti corrompano il workspace all'insaputa dell'agente.
        /// </summary>
        public static bool ValidateFileContent(string filePath, string? content, out string? validationError)
        {
            validationError = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                validationError = "File path is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                validationError = $"Extracted content for '{filePath}' is empty (0 characters). Make sure the code is wrapped inside a markdown code block (e.g. ```xml or ```csharp) between @@@FILE: {filePath}@@@ and @@@END_FILE@@@.";
                return false;
            }

            string ext = System.IO.Path.GetExtension(filePath);
            if (XmlExtensions.Contains(ext))
            {
                string trimmed = content.TrimStart();
                if (!trimmed.StartsWith("<", System.StringComparison.Ordinal))
                {
                    string preview = trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
                    validationError =
                        $"File '{filePath}' must be valid XML starting with '<', but extracted content starts with: \"{preview.Replace("\r", " ").Replace("\n", " ")}\". " +
                        $"All XML angle brackets (<...>) were stripped by the WebChat HTML renderer because the content was NOT wrapped inside a ```xml ... ``` code block! " +
                        $"Rewrite '{filePath}' wrapping the XML inside ```xml ... ``` between @@@FILE: {filePath}@@@ and @@@END_FILE@@@.";
                    return false;
                }

                try
                {
                    _ = System.Xml.Linq.XDocument.Parse(content);
                }
                catch (System.Xml.XmlException ex)
                {
                    validationError =
                        $"File '{filePath}' is not valid XML ({ex.Message}). " +
                        $"Ensure all XML tags are properly closed and wrapped inside a ```xml ... ``` code block between @@@FILE: {filePath}@@@ and @@@END_FILE@@@.";
                    return false;
                }
            }

            return true;
        }
    }
}
