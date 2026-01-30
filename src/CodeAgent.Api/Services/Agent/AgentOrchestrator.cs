using System.Text.Json;
using System.Runtime.CompilerServices;
using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Services.Agent.Tools;
using CodeAgent.Api.Services.Grounding;
using CodeAgent.Api.Services.Rag;
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
    private readonly ILlmClient _llmClient;
    private readonly ICitationService _citationService;
    private readonly ICosmosDbContext _cosmosDb;
    private readonly IVectorSearchService _searchService;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        ILlmClient llmClient,
        ICitationService citationService,
        ICosmosDbContext cosmosDb,
        IVectorSearchService searchService,
        ILogger<AgentOrchestrator> logger)
    {
        _llmClient = llmClient;
        _citationService = citationService;
        _cosmosDb = cosmosDb;
        _searchService = searchService;
        _logger = logger;
    }

    public async Task<ApiAgentResponse> ProcessAsync(
        string userMessage,
        string repositoryId,
        ApiConversationContext? context = null)
    {
        _logger.LogInformation("Processing message for repository: {RepositoryId}", repositoryId);

        // Step 1: Search for relevant code
        _logger.LogInformation("Searching for relevant code...");
        var searchResults = await _searchService.HybridSearchAsync(repositoryId, userMessage, null, 5);

        // Step 2: Build context from search results
        var codeContext = BuildCodeContext(searchResults);
        var citations = BuildCitations(searchResults);

        // Step 3: Build messages with context
        var messages = BuildRagMessages(userMessage, codeContext, context);

        // Step 4: Get response from LLM (no tools)
        var completion = await _llmClient.GetChatCompletionAsync(messages, null);
        var responseContent = completion.Content ?? "I couldn't generate a response.";

        return new ApiAgentResponse
        {
            ConversationId = context?.Id ?? Guid.NewGuid().ToString(),
            Message = responseContent,
            Citations = citations,
            ReasoningSteps = new List<ApiAgentStep>(),
            IsComplete = true
        };
    }

    private string BuildCodeContext(List<SearchResult> results)
    {
        if (results.Count == 0)
            return "No relevant code found in the repository.";

        var context = new List<string> { "Here is the relevant code from the repository:\n" };

        for (int i = 0; i < results.Count; i++)
        {
            var chunk = results[i].Chunk;
            context.Add($"[{i + 1}] File: {chunk.FilePath} (lines {chunk.StartLine}-{chunk.EndLine})");
            if (!string.IsNullOrEmpty(chunk.SymbolName))
            {
                context.Add($"    {chunk.ChunkType}: {chunk.SymbolName}");
            }
            context.Add($"```{chunk.Language}");
            context.Add(chunk.Content);
            context.Add("```\n");
        }

        return string.Join("\n", context);
    }

    private List<ApiCitation> BuildCitations(List<SearchResult> results)
    {
        return results.Select(r => new ApiCitation
        {
            Id = Guid.NewGuid().ToString(),
            FilePath = r.Chunk.FilePath,
            StartLine = r.Chunk.StartLine,
            EndLine = r.Chunk.EndLine,
            Content = r.Chunk.Content.Length > 200 ? r.Chunk.Content[..200] + "..." : r.Chunk.Content,
            SymbolName = r.Chunk.SymbolName,
            RelevanceScore = r.Score,
            SourceType = "code_search"
        }).ToList();
    }

    private List<LlmMessage> BuildRagMessages(string userMessage, string codeContext, ApiConversationContext? context)
    {
        var systemPrompt = """
            You are a helpful code assistant. Answer questions about the codebase based on the provided code context.

            Guidelines:
            - Base your answers on the actual code provided
            - Reference specific files and line numbers when relevant (e.g., [1], [2])
            - If the code context doesn't contain relevant information, say so
            - Be concise but thorough
            - Use code blocks when showing code snippets
            """;

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(systemPrompt)
        };

        // Add conversation history
        if (context?.Messages != null)
        {
            foreach (var msg in context.Messages.TakeLast(6))
            {
                switch (msg.Role.ToLowerInvariant())
                {
                    case "user":
                        messages.Add(LlmMessage.User(msg.Content));
                        break;
                    case "assistant":
                        messages.Add(LlmMessage.Assistant(msg.Content));
                        break;
                }
            }
        }

        // Add current message with context
        var fullMessage = $"{codeContext}\n\nQuestion: {userMessage}";
        messages.Add(LlmMessage.User(fullMessage));

        return messages;
    }

    public async IAsyncEnumerable<ApiStreamingAgentResponse> ProcessStreamingAsync(
        string userMessage,
        string repositoryId,
        ApiConversationContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing streaming message for repository: {RepositoryId}", repositoryId);

        // Step 1: Search for relevant code
        yield return new ApiStreamingAgentResponse { Type = "status", Content = "Searching for relevant code..." };

        var searchResults = await _searchService.HybridSearchAsync(repositoryId, userMessage, null, 5);
        var codeContext = BuildCodeContext(searchResults);
        var citations = BuildCitations(searchResults);

        // Step 2: Build messages and stream response
        var messages = BuildRagMessages(userMessage, codeContext, context);

        await foreach (var text in _llmClient.GetStreamingChatCompletionAsync(messages, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (!string.IsNullOrEmpty(text))
            {
                yield return new ApiStreamingAgentResponse
                {
                    Type = "answer",
                    Content = text
                };
            }
        }

        // Emit citations
        foreach (var citation in citations)
        {
            yield return new ApiStreamingAgentResponse
            {
                Type = "citation",
                Citation = citation
            };
        }
    }
}
