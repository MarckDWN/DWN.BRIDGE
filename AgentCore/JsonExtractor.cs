using System.Text.RegularExpressions;

namespace AIBridge.AgentCore
{
    public static class JsonExtractor
    {
        public static string ExtractJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // 1. Cerca blocchi Markdown con codice JSON (es. ```json ... ```)
            var match = Regex.Match(text, @"```(?:json)?\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // 2. Fallback: cerca la prima parentesi graffa/quadra e l'ultima (per isolare l'oggetto saltando il testo normale come "Certo, ecco i dati:")
            var start = text.IndexOfAny(new[] { '{', '[' });
            var end = text.LastIndexOfAny(new[] { '}', ']' });

            if (start != -1 && end != -1 && end > start)
            {
                return text.Substring(start, end - start + 1).Trim();
            }

            return text.Trim(); // Ritorna l'originale se fallisce (o se era già solo json)
        }
    }
}
