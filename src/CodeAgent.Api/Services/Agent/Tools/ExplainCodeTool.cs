using System.Text.Json;
using CodeAgent.Api.Infrastructure;

namespace CodeAgent.Api.Services.Agent.Tools;

public class ExplainCodeTool : AgentToolBase
{
    private readonly ILlmClient _llmClient;
    private readonly ILogger<ExplainCodeTool> _logger;

    public override string Name => "explain_code";
    public override string Description => AgentPrompts.GetExplainCodeToolDescription();

    public ExplainCodeTool(ILlmClient llmClient, ILogger<ExplainCodeTool> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public override LlmToolDefinition GetToolDefinition()
    {
        return new LlmToolDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = CreateParameters(
                new Dictionary<string, object>
                {
                    ["code"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "The code snippet to explain"
                    },
                    ["context"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Optional context about the code (file path, class name, etc.)"
                    },
                    ["detail_level"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["enum"] = new[] { "brief", "detailed", "comprehensive" },
                        ["description"] = "Level of detail for the explanation"
                    }
                },
                new List<string> { "code" }
            )
        };
    }

    public override async Task<string> ExecuteAsync(string input, string repositoryId)
    {
        try
        {
            var args = JsonSerializer.Deserialize<ExplainCodeInput>(input);
            if (args == null || string.IsNullOrEmpty(args.Code))
            {
                return "Error: 'code' parameter is required";
            }

            _logger.LogInformation("Explaining code snippet");

            var prompt = BuildExplanationPrompt(args);

            var messages = new List<LlmMessage>
            {
                LlmMessage.System("You are a code explanation assistant. Provide clear, accurate explanations of code snippets."),
                LlmMessage.User(prompt)
            };

            var completion = await _llmClient.GetChatCompletionAsync(messages);

            return $"## Code Explanation\n\n{completion.Content}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error explaining code");
            return $"Error explaining code: {ex.Message}";
        }
    }

    private string BuildExplanationPrompt(ExplainCodeInput input)
    {
        var prompt = new List<string>();

        prompt.Add("Please explain the following code:");

        if (!string.IsNullOrEmpty(input.Context))
        {
            prompt.Add($"\nContext: {input.Context}");
        }

        prompt.Add($"\n```\n{input.Code}\n```");

        var detailLevel = input.DetailLevel ?? "detailed";
        switch (detailLevel)
        {
            case "brief":
                prompt.Add("\nProvide a brief, high-level explanation (2-3 sentences).");
                break;
            case "comprehensive":
                prompt.Add("\nProvide a comprehensive explanation including:");
                prompt.Add("1. Overall purpose and functionality");
                prompt.Add("2. Line-by-line breakdown of key sections");
                prompt.Add("3. Design patterns or techniques used");
                prompt.Add("4. Potential edge cases or considerations");
                prompt.Add("5. How this code might interact with other parts of a system");
                break;
            default: // detailed
                prompt.Add("\nProvide a detailed explanation including:");
                prompt.Add("1. What the code does");
                prompt.Add("2. Key concepts or patterns used");
                prompt.Add("3. Any notable implementation details");
                break;
        }

        return string.Join("\n", prompt);
    }

    private class ExplainCodeInput
    {
        public string Code { get; set; } = string.Empty;
        public string? Context { get; set; }
        public string? DetailLevel { get; set; }
    }
}
