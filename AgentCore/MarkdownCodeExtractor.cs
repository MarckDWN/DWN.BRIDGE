using System.Collections.Generic;
using AIBridge.Models;

namespace AIBridge.AgentCore
{
    public static class MarkdownCodeExtractor
    {
        public static MultiFileEditCommand Parse(string response)
        {
            var command = new MultiFileEditCommand();

            int index = 0;
            var fileIndices = new List<int>();
            while ((index = response.IndexOf("@@@FILE:", index)) != -1)
            {
                int pathEnd = response.IndexOf("@@@", index + "@@@FILE:".Length);
                if (pathEnd != -1)
                {
                    string path = response.Substring(index + "@@@FILE:".Length, pathEnd - (index + "@@@FILE:".Length)).Trim();
                    // Ignora se l'agente sta solo citando le regole del prompt ("..." o "path" o vuoto)
                    if (path == "..." || path.Contains("path/to/") || path == "path" || string.IsNullOrWhiteSpace(path))
                    {
                        index += "@@@FILE:".Length;
                        continue; 
                    }
                }

                fileIndices.Add(index);
                index += "@@@FILE:".Length;
            }

            if (fileIndices.Count == 0)
            {
                command.Message = response.Trim();
                return command;
            }

            int currentTextPtr = 0;
            System.Text.StringBuilder messageBuilder = new System.Text.StringBuilder();

            for (int i = 0; i < fileIndices.Count; i++)
            {
                int currentStartIndex = fileIndices[i];

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

                int pathEndDelimiterIndex = response.IndexOf("@@@", currentStartIndex + "@@@FILE:".Length);
                if (pathEndDelimiterIndex == -1) 
                {
                    currentTextPtr = currentStartIndex + "@@@FILE:".Length;
                    continue; 
                }

                string filePath = response.Substring(
                    currentStartIndex + "@@@FILE:".Length,
                    pathEndDelimiterIndex - (currentStartIndex + "@@@FILE:".Length)).Trim();

                int contentStart = pathEndDelimiterIndex + 3;
                int contentEnd = response.Length;
                
                int nextFileIndex = (i + 1 < fileIndices.Count) ? fileIndices[i + 1] : response.Length;

                int endFileIndex = -1;
                int currentEndTagLength = 0;
                var matchEnd = System.Text.RegularExpressions.Regex.Match(response.Substring(contentStart), @"@@@END\\?_FILE@@@", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (matchEnd.Success)
                {
                    endFileIndex = contentStart + matchEnd.Index;
                    currentEndTagLength = matchEnd.Length;
                }

                if (endFileIndex != -1 && endFileIndex < nextFileIndex)
                {
                    contentEnd = endFileIndex;
                    currentTextPtr = endFileIndex + currentEndTagLength;
                }
                else
                {
                    contentEnd = nextFileIndex;
                    currentTextPtr = nextFileIndex;
                }

                string content = response.Substring(contentStart, contentEnd - contentStart);
                content = TrimNewLines(content);

                // Strip all lines that contain only markdown code block wrappers (``` or ```csharp)
                // This handles backticks at the start, end, or even injected in the middle of the code.
                content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*```[a-zA-Z]*\s*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);

                content = TrimNewLines(content);

                // Rimuove l'etichetta testuale della lingua (es. "XML", "C#") che il web renderer di Gemini inietta prima del blocco di codice
                content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*(XML|C#|CSHARP|JSON|BASH|POWERSHELL|HTML|CSS|JS|JAVASCRIPT|TYPESCRIPT|TS|SQL|PYTHON|MARKDOWN|MD|TXT|TEXT)\s*[\r\n]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                content = TrimNewLines(content);

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
    }
}
