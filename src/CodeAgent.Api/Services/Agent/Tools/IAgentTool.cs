using CodeAgent.Api.Infrastructure;

namespace CodeAgent.Api.Services.Agent.Tools;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    LlmToolDefinition GetToolDefinition();
    Task<string> ExecuteAsync(string input, string repositoryId);
}

public abstract class AgentToolBase : IAgentTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract LlmToolDefinition GetToolDefinition();
    public abstract Task<string> ExecuteAsync(string input, string repositoryId);

    protected static object CreateParameters(Dictionary<string, object> properties, List<string>? required = null)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required != null)
        {
            schema["required"] = required;
        }

        return schema;
    }
}
