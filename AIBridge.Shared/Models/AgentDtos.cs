using System;
using System.Collections.Generic;

namespace AIBridge.Shared.Models
{
    public enum ToolExecutionType 
    { 
        NativeCSharp, 
        ShellCommand 
    }

    public class ToolDefinition
    {
        public string ActionName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ToolExecutionType ExecutionType { get; set; } = ToolExecutionType.ShellCommand;
        public string CommandTemplate { get; set; } = string.Empty;
        public string UiFormatTemplate { get; set; } = string.Empty;
    }

    public class BlockExtractorDefinition
    {
        public string StartTagPattern { get; set; } = string.Empty;
        public string EndTag { get; set; } = string.Empty;
        public string TargetAction { get; set; } = string.Empty;
    }

    public class CommunityAgentDto
    {
        public Guid Id { get; set; }
        public string Hwid { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string AgentKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = "Private"; // Private, Pending, Approved, Rejected
        public DateTime SubmissionDate { get; set; }
        
        public List<ToolDefinition> Tools { get; set; } = new();
        public List<BlockExtractorDefinition> Extractors { get; set; } = new();
    }

    public class SyncAgentRequest
    {
        public string AgentKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Description { get; set; }
        public string MarkdownContent { get; set; } = string.Empty;
        
        public List<ToolDefinition> Tools { get; set; } = new();
        public List<BlockExtractorDefinition> Extractors { get; set; } = new();
    }

    public class SubmitAgentRequest
    {
        public List<string> AgentKeys { get; set; } = new();
    }
}
