using System.Text.Json;
using System.Runtime.CompilerServices;
using OpenAI.Chat;
using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Services.Agent.Tools;
using CodeAgent.Api.Services.Grounding;
using ApiChatMessage = CodeAgent.Api.Models.ChatMessage;
using ApiAgentResponse = CodeAgent.Api.Models.AgentResponse;
using ApiAgentStep = CodeAgent.Api.Models.AgentStep;
using ApiStreamingAgentResponse = CodeAgent.Api.Models.StreamingAgentResponse;
using ApiConversationContext = CodeAgent.Api.Models.ConversationContext;
using ApiCitation = CodeAgent.Api.Models.Citation;

namespace CodeAgent.Api.Services.Agent;

public interface IAgentOrchestrator
{
    Task<ApiAgentResponse> ProcessAsync(string userMessage, string repositoryId, ApiConversationContext? context = null);
    IAsyncEnumerable<ApiStreamingAgentResponse> ProcessStreamingAsync(string userMessage, string repositoryId, ApiConversationContext? context = null, CancellationToken cancellationToken = default);
}

public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IAzureOpenAiClient _openAiClient;
    private readonly ICitationService _citationService;
    private readonly ICosmosDbContext _cosmosDb;
    private readonly IEnumerable<IAgentTool> _tools;
    private readonly ILogger<AgentOrchestrator> _logger;
    private const int MaxIterations = 10;

    public AgentOrchestrator(
        IAzureOpenAiClient openAiClient,
        ICitationService citationService,
        ICosmosDbContext cosmosDb,
        IEnumerable<IAgentTool> tools,
        ILogger<AgentOrchestrator> logger)
    {
        _openAiClient = openAiClient;
        _citationService = citationService;
        _cosmosDb = cosmosDb;
        _tools = tools;
        _logger = logger;
    }

    public async Task<ApiAgentResponse> ProcessAsync(
        string userMessage,
        string repositoryId,
        ApiConversationContext? context = null)
    {
        _logger.LogInformation("Processing message for repository: {RepositoryId}", repositoryId);

        var messages = BuildConversationMessages(userMessage, context);
        var toolDefinitions = _tools.Select(t => t.GetToolDefinition()).ToList();
        var reasoningSteps = new List<ApiAgentStep>();
        var allToolResults = new List<string>();

        int iteration = 0;
        while (iteration < MaxIterations)
        {
            iteration++;
            _logger.LogDebug("Agent iteration {Iteration}", iteration);

            var completion = await _openAiClient.GetChatCompletionWithToolsAsync(messages, toolDefinitions);

            // Check if the model wants to use tools
            if (completion.FinishReason == ChatFinishReason.ToolCalls && completion.ToolCalls?.Count > 0)
            {
                // Add assistant message with tool calls
                messages.Add(ChatMessage.CreateAssistantMessage(completion));

                // Process each tool call
                foreach (var toolCall in completion.ToolCalls)
                {
                    var tool = _tools.FirstOrDefault(t => t.Name == toolCall.FunctionName);
                    if (tool == null)
                    {
                        _logger.LogWarning("Unknown tool requested: {ToolName}", toolCall.FunctionName);
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, $"Error: Unknown tool '{toolCall.FunctionName}'"));
                        continue;
                    }

                    _logger.LogInformation("Executing tool: {ToolName}", toolCall.FunctionName);

                    var step = new ApiAgentStep
                    {
                        StepNumber = reasoningSteps.Count + 1,
                        Thought = $"Using {toolCall.FunctionName} to gather information",
                        Action = toolCall.FunctionName,
                        ActionInput = toolCall.FunctionArguments.ToString()
                    };

                    try
                    {
                        var result = await tool.ExecuteAsync(toolCall.FunctionArguments.ToString(), repositoryId);
                        step.Observation = result;
                        allToolResults.Add(result);
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, result));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool execution failed: {ToolName}", toolCall.FunctionName);
                        var errorResult = $"Error executing tool: {ex.Message}";
                        step.Observation = errorResult;
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, errorResult));
                    }

                    reasoningSteps.Add(step);
                }
            }
            else
            {
                // Model has finished with a final response
                var finalContent = completion.Content?.Count > 0 ? completion.Content[0].Text : string.Empty;

                // Extract citations from tool results and final response
                var groundedContent = _citationService.ExtractCitations(finalContent ?? string.Empty, allToolResults);

                return new ApiAgentResponse
                {
                    ConversationId = context?.Id ?? Guid.NewGuid().ToString(),
                    Message = groundedContent.Content,
                    Citations = groundedContent.Citations,
                    ReasoningSteps = reasoningSteps,
                    IsComplete = true
                };
            }
        }

        // Max iterations reached
        _logger.LogWarning("Max iterations reached for repository: {RepositoryId}", repositoryId);
        return new ApiAgentResponse
        {
            ConversationId = context?.Id ?? Guid.NewGuid().ToString(),
            Message = "I apologize, but I wasn't able to complete the analysis within the allowed steps. Please try rephrasing your question or breaking it into smaller parts.",
            ReasoningSteps = reasoningSteps,
            IsComplete = false
        };
    }

    public async IAsyncEnumerable<ApiStreamingAgentResponse> ProcessStreamingAsync(
        string userMessage,
        string repositoryId,
        ApiConversationContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing streaming message for repository: {RepositoryId}", repositoryId);

        var messages = BuildConversationMessages(userMessage, context);
        var toolDefinitions = _tools.Select(t => t.GetToolDefinition()).ToList();
        var allToolResults = new List<string>();
        var toolExecutionResults = new List<(string toolCallId, string result)>();

        int iteration = 0;
        while (iteration < MaxIterations && !cancellationToken.IsCancellationRequested)
        {
            iteration++;

            var completion = await _openAiClient.GetChatCompletionWithToolsAsync(messages, toolDefinitions);

            if (completion.FinishReason == ChatFinishReason.ToolCalls && completion.ToolCalls?.Count > 0)
            {
                messages.Add(ChatMessage.CreateAssistantMessage(completion));
                toolExecutionResults.Clear();

                foreach (var toolCall in completion.ToolCalls)
                {
                    var tool = _tools.FirstOrDefault(t => t.Name == toolCall.FunctionName);
                    if (tool == null)
                    {
                        toolExecutionResults.Add((toolCall.Id, $"Error: Unknown tool '{toolCall.FunctionName}'"));
                        continue;
                    }

                    // Emit action event
                    yield return new ApiStreamingAgentResponse
                    {
                        Type = "action",
                        Content = JsonSerializer.Serialize(new { tool = toolCall.FunctionName, input = toolCall.FunctionArguments.ToString() })
                    };

                    string result;
                    try
                    {
                        result = await tool.ExecuteAsync(toolCall.FunctionArguments.ToString(), repositoryId);
                        allToolResults.Add(result);
                    }
                    catch (Exception ex)
                    {
                        result = $"Error executing tool: {ex.Message}";
                    }

                    toolExecutionResults.Add((toolCall.Id, result));

                    // Emit observation event
                    yield return new ApiStreamingAgentResponse
                    {
                        Type = "observation",
                        Content = result.Length > 500 ? result[..500] + "..." : result
                    };
                }

                // Add all tool results to messages
                foreach (var (toolCallId, result) in toolExecutionResults)
                {
                    messages.Add(ChatMessage.CreateToolMessage(toolCallId, result));
                }
            }
            else
            {
                // Stream the final response
                await foreach (var update in _openAiClient.GetStreamingChatCompletionAsync(messages, null))
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    foreach (var contentPart in update.ContentUpdate)
                    {
                        if (!string.IsNullOrEmpty(contentPart.Text))
                        {
                            yield return new ApiStreamingAgentResponse
                            {
                                Type = "answer",
                                Content = contentPart.Text
                            };
                        }
                    }
                }

                // Emit citations
                var citations = _citationService.ExtractCitationsFromToolResults(allToolResults);

                foreach (var citation in citations.Take(10))
                {
                    yield return new ApiStreamingAgentResponse
                    {
                        Type = "citation",
                        Citation = citation
                    };
                }

                yield break;
            }
        }
    }

    private List<ChatMessage> BuildConversationMessages(string userMessage, ApiConversationContext? context)
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(AgentPrompts.SystemPrompt + "\n\n" + AgentPrompts.ReActPrompt)
        };

        // Add conversation history
        if (context?.Messages != null)
        {
            foreach (var msg in context.Messages.TakeLast(10)) // Keep last 10 messages for context
            {
                switch (msg.Role.ToLowerInvariant())
                {
                    case "user":
                        messages.Add(ChatMessage.CreateUserMessage(msg.Content));
                        break;
                    case "assistant":
                        messages.Add(ChatMessage.CreateAssistantMessage(msg.Content));
                        break;
                }
            }
        }

        // Add current user message
        messages.Add(ChatMessage.CreateUserMessage(userMessage));

        return messages;
    }
}
