using System.Collections.Generic;

namespace AIBridge.Models
{
    public class AgentProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RoleType { get; set; } = string.Empty; 
        public string IconCode { get; set; } = string.Empty;
        
        public List<AIBridge.Shared.Models.ToolDefinition> Tools { get; set; } = new();
        public List<AIBridge.Shared.Models.BlockExtractorDefinition> Extractors { get; set; } = new();
    }
}
