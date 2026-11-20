using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AIBridge.AgentCore
{
    public enum FileNature
    {
        SourceCode,     // Codice sorgente (Web, Desktop, Script, Config, Style)
        UserDataExport, // Export dati personali (Netscape Bookmarks, CSV massivi, Dump, Log)
        Binary          // File binari/eseguibili
    }

    public static class FileNatureClassifier
    {
        private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".cpp", ".c", ".h", ".hpp",
            ".java", ".go", ".rs", ".php", ".rb", ".sql", ".xaml", ".csproj", ".sln",
            ".css", ".scss", ".less", ".config", ".props", ".targets"
        };

        public static FileNature Classify(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return FileNature.SourceCode; // Fallback sicuro

            string ext = Path.GetExtension(filePath);

            // 1. Controllo file binari
            byte[] buffer = new byte[4096];
            int bytesRead = 0;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                bytesRead = fs.Read(buffer, 0, buffer.Length);
            }
            catch
            {
                return FileNature.SourceCode;
            }

            if (bytesRead == 0) return FileNature.SourceCode;

            // Rilevamento caratteri NUL (binario)
            int nullCount = 0;
            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0) nullCount++;
            }
            if (nullCount > 2) return FileNature.Binary;

            string contentHeader = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // 2. Controllo firme UserDataExport (Netscape Bookmarks, CSV massivo, Log, Dump SQL, Chat Log)
            if (IsNetscapeBookmarks(contentHeader)) return FileNature.UserDataExport;
            if (IsCsvOrTabularData(contentHeader)) return FileNature.UserDataExport;
            if (IsLogFile(contentHeader)) return FileNature.UserDataExport;
            if (IsSqlDataDump(contentHeader)) return FileNature.UserDataExport;
            if (IsSerializedChatLog(contentHeader)) return FileNature.UserDataExport;

            // 3. Controllo estensioni sorgente notorie (senza firme data export)
            if (CodeExtensions.Contains(ext)) return FileNature.SourceCode;

            // 4. Controllo pagine web HTML (se .html / .htm)
            if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            {
                if (IsWebHtmlPage(contentHeader)) return FileNature.SourceCode;
            }

            // 5. Fallback per file generici (.json, .xml, .config)
            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase) || ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                if (IsJsonOrXmlConfig(contentHeader)) return FileNature.SourceCode;
            }

            return FileNature.SourceCode;
        }

        private static bool IsNetscapeBookmarks(string header)
        {
            if (header.Contains("NETSCAPE-Bookmark-file-1", StringComparison.OrdinalIgnoreCase)) return true;
            if (Regex.IsMatch(header, @"<H3\s+[^>]*ADD_DATE=", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(header, @"<A\s+[^>]*HREF=.*ADD_DATE=", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static bool IsCsvOrTabularData(string header)
        {
            var lines = header.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 4) return false;

            char[] delimiters = new[] { ',', ';', '\t' };
            foreach (var d in delimiters)
            {
                int firstCount = lines[0].Split(d).Length;
                if (firstCount >= 3)
                {
                    bool isConsistent = true;
                    for (int i = 1; i < Math.Min(5, lines.Length); i++)
                    {
                        if (lines[i].Split(d).Length != firstCount)
                        {
                            isConsistent = false;
                            break;
                        }
                    }
                    if (isConsistent) return true;
                }
            }
            return false;
        }

        private static bool IsLogFile(string header)
        {
            var lines = header.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3) return false;

            int logMatchCount = 0;
            var logPattern = new Regex(@"^(\d{4}[-/.]\d{2}[-/.]\d{2}|\d{2}:[0-5]\d:[0-5]\d|\[(INFO|WARN|ERROR|DEBUG|TRACE|FATAL)\])", RegexOptions.IgnoreCase);
            for (int i = 0; i < Math.Min(5, lines.Length); i++)
            {
                if (logPattern.IsMatch(lines[i].Trim())) logMatchCount++;
            }
            return logMatchCount >= 2;
        }

        private static bool IsSqlDataDump(string header)
        {
            return Regex.IsMatch(header, @"INSERT\s+INTO\s+.*?\s+VALUES", RegexOptions.IgnoreCase | RegexOptions.Singleline) ||
                   Regex.IsMatch(header, @"COPY\s+.*?\s+FROM", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static bool IsSerializedChatLog(string header)
        {
            return header.Contains("\"messages\": [", StringComparison.OrdinalIgnoreCase) ||
                   header.Contains("\"chat_history\":", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWebHtmlPage(string header)
        {
            if (header.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)) return true;
            if (Regex.IsMatch(header, @"<(html|head|body|script|style|div|nav|header|footer)\b", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static bool IsJsonOrXmlConfig(string header)
        {
            if (header.Contains("\"dependencies\":") || header.Contains("\"devDependencies\":") || header.Contains("\"version\":") || header.Contains("\"scripts\":")) return true;
            if (header.Contains("<Project") || header.Contains("<Configuration") || header.Contains("<Settings")) return true;
            return false;
        }
    }
}
