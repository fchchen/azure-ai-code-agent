using System.Text.Json.Serialization;

namespace CodeAgent.Api.Models;

public class AgentResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = new();
    public List<AgentStep> ReasoningSteps { get; set; } = new();
    public bool IsComplete { get; set; } = true;
}

public class AgentStep
{
    public int StepNumber { get; set; }
    public string Thought { get; set; } = string.Empty;
    public string? Action { get; set; }
    public string? ActionInput { get; set; }
    public string? Observation { get; set; }
}

public class StreamingAgentResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // thought, action, observation, answer, citation

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("citation")]
    public Citation? Citation { get; set; }
}
