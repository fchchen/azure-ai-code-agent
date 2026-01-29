namespace CodeAgent.Api.Infrastructure;

public interface ILlmClient
{
    Task<LlmChatCompletion> GetChatCompletionAsync(
        List<LlmMessage> messages,
        List<LlmToolDefinition>? tools = null);

    Task<float[]> GetEmbeddingAsync(string text);

    Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts);

    IAsyncEnumerable<string> GetStreamingChatCompletionAsync(
        List<LlmMessage> messages,
        CancellationToken cancellationToken = default);
}

public class LlmMessage
{
    public required string Role { get; set; } // "system", "user", "assistant", "tool"
    public string? Content { get; set; }
    public List<LlmToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; } // For tool response messages

    public static LlmMessage System(string content) => new() { Role = "system", Content = content };
    public static LlmMessage User(string content) => new() { Role = "user", Content = content };
    public static LlmMessage Assistant(string content) => new() { Role = "assistant", Content = content };
    public static LlmMessage AssistantWithToolCalls(List<LlmToolCall> toolCalls) =>
        new() { Role = "assistant", ToolCalls = toolCalls };
    public static LlmMessage Tool(string toolCallId, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Content = content };
}

public class LlmToolCall
{
    public required string Id { get; set; }
    public required string FunctionName { get; set; }
    public required string Arguments { get; set; }
}

public class LlmToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required object Parameters { get; set; } // JSON schema
}

public class LlmChatCompletion
{
    public string? Content { get; set; }
    public List<LlmToolCall>? ToolCalls { get; set; }
    public bool RequiresToolCalls => ToolCalls?.Count > 0;
}
