using AIBridge.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AIBridge.Services
{
    /// <summary>
    /// Gestisce la persistenza della cronologia delle chat su disco.
    /// Le sessioni sono salvate come file JSON in %AppData%\AIBridge\History\.
    /// </summary>
    public class ChatHistoryService
    {
        private readonly string _historyDir;
        private readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ChatHistoryService()
        {
            _historyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIBridge", "History");
            Directory.CreateDirectory(_historyDir);
        }

        /// <summary>Carica tutte le sessioni ordinate dalla più recente (CreatedAt desc).</summary>
        public List<ChatSession> LoadAll()
        {
            var sessions = new List<ChatSession>();
            foreach (var file in Directory.GetFiles(_historyDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var s = JsonSerializer.Deserialize<ChatSession>(json, _jsonOpts);
                    if (s != null) sessions.Add(s);
                }
                catch { /* File corrotto, skip */ }
            }
            // Ordina per data di creazione decrescente: la chat più recente in cima
            return sessions.OrderByDescending(s => s.CreatedAt).ToList();
        }

        /// <summary>Salva o aggiorna una sessione (usa GeminiId come chiave, o filename univoco).</summary>
        public void Save(ChatSession session)
        {
            string fileName = string.IsNullOrEmpty(session.GeminiId)
                ? $"session_{session.CreatedAt:yyyyMMdd_HHmmss}.json"
                : $"session_{session.GeminiId}.json";

            string path = Path.Combine(_historyDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(session, _jsonOpts));
        }

        /// <summary>Elimina una sessione dal disco.</summary>
        public void Delete(ChatSession session)
        {
            string fileName = string.IsNullOrEmpty(session.GeminiId)
                ? $"session_{session.CreatedAt:yyyyMMdd_HHmmss}.json"
                : $"session_{session.GeminiId}.json";

            string path = Path.Combine(_historyDir, fileName);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// Rinomina il file quando otteniamo finalmente l'ID Gemini (la prima volta la chat non ha ancora un ID).
        /// </summary>
        public void UpdateGeminiId(ChatSession session, string oldTempId)
        {
            if (string.IsNullOrEmpty(oldTempId)) return;
            string oldPath = Path.Combine(_historyDir, $"session_{oldTempId}.json");
            string newPath = Path.Combine(_historyDir, $"session_{session.GeminiId}.json");
            if (File.Exists(oldPath) && !File.Exists(newPath))
                File.Move(oldPath, newPath);
        }
    }
}
