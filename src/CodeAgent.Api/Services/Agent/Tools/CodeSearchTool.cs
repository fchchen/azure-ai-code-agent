using System.Text.Json;
using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Services.Rag;

namespace CodeAgent.Api.Services.Agent.Tools;

public class CodeSearchTool : AgentToolBase
{
    private readonly IVectorSearchService _searchService;
    private readonly ILogger<CodeSearchTool> _logger;

    public override string Name => "code_search";
    public override string Description => AgentPrompts.GetCodeSearchToolDescription();

    public CodeSearchTool(IVectorSearchService searchService, ILogger<CodeSearchTool> logger)
    {
        _searchService = searchService;
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
                    ["query"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Natural language search query describing what code to find"
                    },
                    ["language"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Optional: filter by programming language (e.g., 'csharp', 'typescript', 'python')"
                    },
                    ["chunk_type"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Optional: filter by code type ('class', 'method', 'function')"
                    }
                },
                new List<string> { "query" }
            )
        };
    }

    public override async Task<string> ExecuteAsync(string input, string repositoryId)
    {
        try
        {
            var args = JsonSerializer.Deserialize<CodeSearchInput>(input);
            if (args == null || string.IsNullOrEmpty(args.Query))
            {
                return "Error: 'query' parameter is required";
            }

            _logger.LogInformation("Code search: {Query} in {RepositoryId}", args.Query, repositoryId);

            var filter = new SearchFilter
            {
                Language = args.Language,
                ChunkType = args.ChunkType
            };

            var results = await _searchService.HybridSearchAsync(repositoryId, args.Query, filter, 5);

            if (results.Count == 0)
            {
                return "No code found matching the query. Try different search terms.";
            }

            var output = new List<string> { $"Found {results.Count} relevant code sections:\n" };

            foreach (var result in results)
            {
                var chunk = result.Chunk;
                var header = $"--- [{chunk.FilePath}:{chunk.StartLine}-{chunk.EndLine}] ";
                if (!string.IsNullOrEmpty(chunk.SymbolName))
                {
                    header += $"({chunk.ChunkType}: {chunk.SymbolName}) ";
                }
                header += $"[Score: {result.Score:F2}] ---";

                output.Add(header);
                output.Add($"```{chunk.Language}");
                output.Add(chunk.Content);
                output.Add("```\n");
            }

            return string.Join("\n", output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing code search");
            return $"Error performing search: {ex.Message}";
        }
    }

    private class CodeSearchInput
    {
        public string Query { get; set; } = string.Empty;
        public string? Language { get; set; }
        public string? ChunkType { get; set; }
    }
}
