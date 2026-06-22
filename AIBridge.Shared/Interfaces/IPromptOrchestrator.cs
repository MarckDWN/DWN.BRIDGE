using System.Threading.Tasks;

namespace AIBridge.Shared.Interfaces
{
    public interface IPromptOrchestrator
    {
        Task<string> BuildPromptAsync(string agentType, string userInput, string? schema = null, string? skill = null);
        Task<string> ParseOutputAsync(string llmOutput);
    }
}
