using System.Text.Json.Serialization;

namespace AIBridge.Models
{
    public class PersonData
    {
        [JsonPropertyName("nome")]
        public string Nome { get; set; } = string.Empty;

        [JsonPropertyName("cognome")]
        public string Cognome { get; set; } = string.Empty;

        [JsonPropertyName("eta")]
        public int Eta { get; set; }

        [JsonPropertyName("professione")]
        public string Professione { get; set; } = string.Empty;
    }
}
