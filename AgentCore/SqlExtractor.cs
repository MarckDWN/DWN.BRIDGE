using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace AIBridge.AgentCore
{
    public static class SqlExtractor
    {
        public static string ExtractSql(string rawResponse)
        {
            var match = Regex.Match(rawResponse, @"@{2,4}\s*SQL\\?_QUERY\s*@{2,4}\s*(.*?)\s*@{2,4}\s*END\\?_SQL\s*@*", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string query = match.Groups[1].Value.Trim();

                // Rimuove eventuali blocchi markdown inseriti all'interno dei tag
                query = Regex.Replace(query, @"^```[a-zA-Z]*\s*", "");
                query = Regex.Replace(query, @"\s*```$", "");

                // Unescape di caratteri markdown (ReverseMarkdown aggiunge backslash per _, *, [, ])
                query = query.Replace(@"\_", "_");
                query = query.Replace(@"\*", "*");
                query = query.Replace(@"\[", "[");
                query = query.Replace(@"\]", "]");
                query = query.Replace(@"\`", "`");
                query = query.Replace(@"\@", "@");
                query = query.Replace("`", ""); // Rimuove eventuali backtick spuri introdotti dal markdown converter nei blocchi di codice

                return query.Trim();
            }
            return string.Empty;
        }

        public static string StripSql(string rawResponse)
        {
            // Rimuove l'intero blocco SQL per lasciare solo il testo discorsivo generato dall'LLM
            string stripped = Regex.Replace(rawResponse, @"@{2,4}\s*SQL\\?_QUERY\s*@{2,4}.*?@{2,4}\s*END\\?_SQL\s*@*", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return stripped.Trim();
        }

        public static (string formattedTable, string tsvData) FormatDataTable(DataTable table)
        {
            if (table == null || table.Columns.Count == 0)
                return ("Nessun dato restituito.", "");

            var tsvBuilder = new StringBuilder();

            // Calcolo larghezze colonne per il formattatore monospaced
            int[] colWidths = new int[table.Columns.Count];
            for (int i = 0; i < table.Columns.Count; i++)
            {
                colWidths[i] = table.Columns[i].ColumnName.Length;

                // TSV Header
                tsvBuilder.Append(table.Columns[i].ColumnName);
                if (i < table.Columns.Count - 1) tsvBuilder.Append("\t");
            }
            tsvBuilder.AppendLine();

            foreach (DataRow row in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    string val = row[i]?.ToString() ?? "NULL";
                    if (val.Length > colWidths[i])
                        colWidths[i] = val.Length;

                    // TSV Data
                    tsvBuilder.Append(val);
                    if (i < table.Columns.Count - 1) tsvBuilder.Append("\t");
                }
                tsvBuilder.AppendLine();
            }

            // Costruzione Tabella Monospaced
            var sb = new StringBuilder();

            // Intestazione
            for (int i = 0; i < table.Columns.Count; i++)
            {
                sb.Append(table.Columns[i].ColumnName.PadRight(colWidths[i] + 2));
                sb.Append("| ");
            }
            sb.AppendLine();

            // Separatore
            for (int i = 0; i < table.Columns.Count; i++)
            {
                sb.Append(new string('-', colWidths[i] + 2));
                sb.Append("|-");
            }
            sb.AppendLine();

            // Dati
            foreach (DataRow row in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    string val = row[i]?.ToString() ?? "NULL";
                    sb.Append(val.PadRight(colWidths[i] + 2));
                    sb.Append("| ");
                }
                sb.AppendLine();
            }

            return (sb.ToString(), tsvBuilder.ToString());
        }

        public static bool IsSqlTruncated(string rawResponse)
        {
            if (rawResponse.Contains("[TRUNCATED]") || rawResponse.Contains("[RESPONSE_TRUNCATED]"))
                return true;

            var startMatch = Regex.Match(rawResponse, @"@{2,4}\s*SQL\\?_QUERY\s*@{2,4}", RegexOptions.IgnoreCase);
            if (startMatch.Success)
            {
                var endMatch = Regex.Match(rawResponse, @"@{2,4}\s*END\\?_SQL\s*@*", RegexOptions.IgnoreCase);
                if (!endMatch.Success)
                {
                    return true;
                }
            }
            return false;
        }

        public static string ExtractTruncatedSqlLastLines(string rawResponse)
        {
            var startMatch = Regex.Match(rawResponse, @"@{2,4}\s*SQL\\?_QUERY\s*@{2,4}", RegexOptions.IgnoreCase);
            if (startMatch.Success)
            {
                string content = rawResponse.Substring(startMatch.Index + startMatch.Length);
                content = content.Trim();
                content = Regex.Replace(content, @"^```[a-zA-Z]*\s*", "");
                content = Regex.Replace(content, @"\s*```$", "");
                content = content.Trim();

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                int linesToTake = Math.Min(5, lines.Length);
                var lastLinesList = new System.Collections.Generic.List<string>();
                for (int l = lines.Length - linesToTake; l < lines.Length; l++)
                {
                    lastLinesList.Add(lines[l]);
                }
                return string.Join("\n", lastLinesList);
            }
            return string.Empty;
        }
    }
}
