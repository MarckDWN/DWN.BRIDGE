using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AIBridge.Models
{
    public class MultiFileEditCommand
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        public string PostMessage { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<FileEditCommand> Files { get; set; } = new();
    }

    public class FileEditCommand
    {
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = string.Empty;

        [JsonPropertyName("newContent")]
        public string NewContent { get; set; } = string.Empty;
    }
}
