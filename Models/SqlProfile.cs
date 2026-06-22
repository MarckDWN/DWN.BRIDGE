using System.Text.Json.Serialization;

namespace AIBridge.Models
{
    public class SqlProfile
    {
        [JsonIgnore]
        public string Name { get; set; } = string.Empty;

        [JsonIgnore]
        public string FilePath { get; set; } = string.Empty;

        public string Provider { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public string SchemaFile { get; set; } = string.Empty;
        public string SkillFile { get; set; } = string.Empty;
    }
}
