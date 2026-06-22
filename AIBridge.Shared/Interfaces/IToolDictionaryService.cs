using System.Collections.Generic;
using AIBridge.Shared.Models;

namespace AIBridge.Shared.Interfaces
{
    public interface IToolDictionaryService
    {
        List<ToolDefinition> GetBaseTools();
        List<BlockExtractorDefinition> GetBaseExtractors();
    }
}
