using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Models;

namespace CodeAgent.Api.Services.Rag;

public interface IVectorSearchService
{
    Task<List<SearchResult>> SearchAsync(string repositoryId, string query, int topK = 10);
    Task<List<SearchResult>> HybridSearchAsync(string repositoryId, string query, SearchFilter? filter = null, int topK = 10);
}

public class SearchResult
{
    public CodeChunk Chunk { get; set; } = new();
    public double Score { get; set; }
    public string MatchType { get; set; } = "vector"; // vector, keyword, hybrid
}

public class SearchFilter
{
    public string? Language { get; set; }
    public string? ChunkType { get; set; }
    public string? FileName { get; set; }
    public List<string>? FilePaths { get; set; }
}

public class VectorSearchService : IVectorSearchService
{
    private readonly ICosmosDbContext _cosmosDb;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        ICosmosDbContext cosmosDb,
        IEmbeddingService embeddingService,
        ILogger<VectorSearchService> logger)
    {
        _cosmosDb = cosmosDb;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<List<SearchResult>> SearchAsync(string repositoryId, string query, int topK = 10)
    {
        _logger.LogInformation("Vector search for: {Query} in repository {RepositoryId}", query, repositoryId);

        // Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

        // Perform vector search
        var chunks = await _cosmosDb.VectorSearchAsync(repositoryId, queryEmbedding, topK);

        // Convert to search results with scores
        var results = chunks.Select((chunk, index) => new SearchResult
        {
            Chunk = chunk,
            Score = 1.0 - (index * 0.05), // Approximate score based on ranking
            MatchType = "vector"
        }).ToList();

        _logger.LogInformation("Found {Count} results for query: {Query}", results.Count, query);
        return results;
    }

    public async Task<List<SearchResult>> HybridSearchAsync(
        string repositoryId,
        string query,
        SearchFilter? filter = null,
        int topK = 10)
    {
        _logger.LogInformation("Hybrid search for: {Query} in repository {RepositoryId}", query, repositoryId);

        // Perform vector search
        var vectorResults = await SearchAsync(repositoryId, query, topK * 2);

        // Perform keyword search (simple contains match)
        var keywordResults = await KeywordSearchAsync(repositoryId, query, topK);

        // Merge and re-rank results
        var mergedResults = MergeResults(vectorResults, keywordResults, topK);

        // Apply filters if provided
        if (filter != null)
        {
            mergedResults = ApplyFilter(mergedResults, filter);
        }

        return mergedResults.Take(topK).ToList();
    }

    private async Task<List<SearchResult>> KeywordSearchAsync(string repositoryId, string query, int topK)
    {
        var allChunks = await _cosmosDb.GetChunksByRepositoryAsync(repositoryId);

        var keywords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var results = allChunks
            .Select(chunk =>
            {
                var content = chunk.Content.ToLowerInvariant();
                var symbolName = chunk.SymbolName?.ToLowerInvariant() ?? "";

                // Calculate keyword match score
                var matchCount = keywords.Count(k =>
                    content.Contains(k) || symbolName.Contains(k));
                var score = matchCount / (double)keywords.Length;

                return new SearchResult
                {
                    Chunk = chunk,
                    Score = score,
                    MatchType = "keyword"
                };
            })
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return results;
    }

    private List<SearchResult> MergeResults(
        List<SearchResult> vectorResults,
        List<SearchResult> keywordResults,
        int topK)
    {
        var merged = new Dictionary<string, SearchResult>();

        // Add vector results with weight 0.7
        foreach (var result in vectorResults)
        {
            var key = result.Chunk.Id;
            merged[key] = new SearchResult
            {
                Chunk = result.Chunk,
                Score = result.Score * 0.7,
                MatchType = "hybrid"
            };
        }

        // Add keyword results with weight 0.3
        foreach (var result in keywordResults)
        {
            var key = result.Chunk.Id;
            if (merged.ContainsKey(key))
            {
                merged[key].Score += result.Score * 0.3;
            }
            else
            {
                merged[key] = new SearchResult
                {
                    Chunk = result.Chunk,
                    Score = result.Score * 0.3,
                    MatchType = "hybrid"
                };
            }
        }

        return merged.Values
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    private List<SearchResult> ApplyFilter(List<SearchResult> results, SearchFilter filter)
    {
        var filtered = results.AsEnumerable();

        if (!string.IsNullOrEmpty(filter.Language))
        {
            filtered = filtered.Where(r =>
                r.Chunk.Language.Equals(filter.Language, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(filter.ChunkType))
        {
            filtered = filtered.Where(r =>
                r.Chunk.ChunkType.Equals(filter.ChunkType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(filter.FileName))
        {
            filtered = filtered.Where(r =>
                r.Chunk.FileName.Contains(filter.FileName, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.FilePaths?.Count > 0)
        {
            filtered = filtered.Where(r =>
                filter.FilePaths.Any(fp =>
                    r.Chunk.FilePath.Contains(fp, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered.ToList();
    }
}
