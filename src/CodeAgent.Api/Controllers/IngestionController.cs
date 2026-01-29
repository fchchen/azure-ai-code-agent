using Microsoft.AspNetCore.Mvc;
using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Models;
using CodeAgent.Api.Services.Rag;

namespace CodeAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly IDocumentChunker _documentChunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly ICosmosDbContext _cosmosDb;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        IDocumentChunker documentChunker,
        IEmbeddingService embeddingService,
        ICosmosDbContext cosmosDb,
        ILogger<IngestionController> logger)
    {
        _documentChunker = documentChunker;
        _embeddingService = embeddingService;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    /// <summary>
    /// List all indexed repositories
    /// </summary>
    [HttpGet("repositories")]
    [ProducesResponseType(typeof(List<Repository>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRepositories()
    {
        var repositories = await _cosmosDb.GetRepositoriesAsync();
        return Ok(repositories);
    }

    /// <summary>
    /// Get a specific repository
    /// </summary>
    [HttpGet("repositories/{repositoryId}")]
    [ProducesResponseType(typeof(Repository), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRepository(string repositoryId)
    {
        var repository = await _cosmosDb.GetRepositoryAsync(repositoryId);
        if (repository == null)
        {
            return NotFound(new { error = "Repository not found" });
        }

        return Ok(repository);
    }

    /// <summary>
    /// Index a code repository
    /// </summary>
    [HttpPost("repositories")]
    [ProducesResponseType(typeof(Repository), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IndexRepository([FromBody] IndexRepositoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest(new { error = "Path is required" });
        }

        if (!Directory.Exists(request.Path))
        {
            return BadRequest(new { error = $"Directory not found: {request.Path}" });
        }

        _logger.LogInformation("Indexing repository: {Path}", request.Path);

        var repositoryId = request.Id ?? Guid.NewGuid().ToString();
        var repositoryName = request.Name ?? Path.GetFileName(request.Path);

        // Delete existing chunks if re-indexing
        await _cosmosDb.DeleteChunksByRepositoryAsync(repositoryId);

        // Chunk the repository
        var chunks = await _documentChunker.ChunkRepositoryAsync(request.Path, repositoryId);
        _logger.LogInformation("Created {Count} chunks", chunks.Count);

        // Generate embeddings
        chunks = await _embeddingService.GenerateEmbeddingsAsync(chunks);
        _logger.LogInformation("Generated embeddings for {Count} chunks", chunks.Count);

        // Store chunks
        await _cosmosDb.UpsertChunksAsync(chunks);

        // Create/update repository record
        var languages = chunks
            .Select(c => c.Language)
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        var repository = new Repository
        {
            Id = repositoryId,
            Name = repositoryName,
            Path = request.Path,
            Description = request.Description,
            IndexedAt = DateTime.UtcNow,
            ChunkCount = chunks.Count,
            Languages = languages
        };

        await _cosmosDb.UpsertRepositoryAsync(repository);

        _logger.LogInformation("Repository indexed: {RepositoryId} with {ChunkCount} chunks",
            repositoryId, chunks.Count);

        return CreatedAtAction(nameof(GetRepository), new { repositoryId }, repository);
    }

    /// <summary>
    /// Delete a repository and its chunks
    /// </summary>
    [HttpDelete("repositories/{repositoryId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteRepository(string repositoryId)
    {
        await _cosmosDb.DeleteChunksByRepositoryAsync(repositoryId);

        var repository = await _cosmosDb.GetRepositoryAsync(repositoryId);
        if (repository != null)
        {
            // Mark as deleted by setting chunk count to 0
            repository.ChunkCount = 0;
            repository.IndexedAt = null;
            await _cosmosDb.UpsertRepositoryAsync(repository);
        }

        _logger.LogInformation("Repository deleted: {RepositoryId}", repositoryId);
        return NoContent();
    }

    /// <summary>
    /// Get indexing statistics
    /// </summary>
    [HttpGet("repositories/{repositoryId}/stats")]
    [ProducesResponseType(typeof(RepositoryStats), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRepositoryStats(string repositoryId)
    {
        var repository = await _cosmosDb.GetRepositoryAsync(repositoryId);
        if (repository == null)
        {
            return NotFound(new { error = "Repository not found" });
        }

        var chunks = await _cosmosDb.GetChunksByRepositoryAsync(repositoryId);

        var stats = new RepositoryStats
        {
            RepositoryId = repositoryId,
            TotalChunks = chunks.Count,
            ChunksByLanguage = chunks.GroupBy(c => c.Language)
                .ToDictionary(g => g.Key, g => g.Count()),
            ChunksByType = chunks.GroupBy(c => c.ChunkType)
                .ToDictionary(g => g.Key, g => g.Count()),
            UniqueFiles = chunks.Select(c => c.FilePath).Distinct().Count(),
            IndexedAt = repository.IndexedAt
        };

        return Ok(stats);
    }
}

public class IndexRepositoryRequest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class RepositoryStats
{
    public string RepositoryId { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public Dictionary<string, int> ChunksByLanguage { get; set; } = new();
    public Dictionary<string, int> ChunksByType { get; set; } = new();
    public int UniqueFiles { get; set; }
    public DateTime? IndexedAt { get; set; }
}
