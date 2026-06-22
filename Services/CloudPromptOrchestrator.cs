using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIBridge.Shared.Interfaces;

namespace AIBridge.Services
{
    public class CloudPromptOrchestrator : IPromptOrchestrator
    {
        private readonly HttpClient _http;
        private IPromptOrchestrator? _localFallback;
        private bool _fallbackInitialized;

        public CloudPromptOrchestrator(HttpClient http)
        {
            _http = http;
        }

        private IPromptOrchestrator? GetLocalFallback()
        {
            if (_fallbackInitialized) return _localFallback;
            _fallbackInitialized = true;
            try
            {
                // Caricamento riflessivo (Plugin mode) dalla cartella extensions
                string extensionsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "extensions");
                string dllPath = System.IO.Path.Combine(extensionsPath, "AIBridge.Core.dll");
                
                if (System.IO.File.Exists(dllPath))
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
                    var type = assembly.GetType("AIBridge.Core.LocalPromptOrchestrator");
                    if (type != null)
                    {
                        _localFallback = (IPromptOrchestrator?)Activator.CreateInstance(type);
                    }
                }
            }
            catch
            {
                _localFallback = null;
            }
            return _localFallback;
        }

        public async Task<string> BuildPromptAsync(string agentType, string userInput, string? schema = null, string? skill = null)
        {
            try
            {
                var req = new
                {
                    AgentType = agentType,
                    UserInput = userInput,
                    Schema = schema,
                    Skill = skill
                };

                var res = await _http.PostAsJsonAsync("api/orchestrator/build-prompt", req);
                if (res.IsSuccessStatusCode)
                {
                    var dict = await res.Content.ReadFromJsonAsync<System.Collections.Generic.Dictionary<string, string>>();
                    if (dict != null && dict.TryGetValue("prompt", out string? prompt))
                    {
                        return prompt;
                    }
                }
            }
            catch
            {
                // Rete irraggiungibile o server off
            }

            // Se arriviamo qui, il server cloud ha fallito (offline o errore).
            var fallback = GetLocalFallback();
            if (fallback != null)
            {
                return await fallback.BuildPromptAsync(agentType, userInput, schema, skill);
            }

            // Nessun fallback disponibile (App SaaS senza DLL)
            throw new InvalidOperationException("Impossibile connettersi al Server Cloud e i componenti Standalone (AIBridge.Core.dll) non sono presenti. Il prompt protetto non può essere generato in locale.");
        }

        public async Task<string> ParseOutputAsync(string llmOutput)
        {
            try
            {
                var res = await _http.PostAsJsonAsync("api/orchestrator/parse-output", new { Output = llmOutput });
                if (res.IsSuccessStatusCode)
                {
                    var dict = await res.Content.ReadFromJsonAsync<System.Collections.Generic.Dictionary<string, string>>();
                    if (dict != null && dict.TryGetValue("parsed", out string? parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch { }

            var fallback = GetLocalFallback();
            if (fallback != null)
            {
                return await fallback.ParseOutputAsync(llmOutput);
            }

            throw new InvalidOperationException("Impossibile contattare il server per il parsing.");
        }
    }
}
