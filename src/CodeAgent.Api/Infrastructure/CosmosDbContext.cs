using Microsoft.Azure.Cosmos;
using CodeAgent.Api.Models;

namespace CodeAgent.Api.Infrastructure;

public interface ICosmosDbContext
{
    Task<CodeChunk?> GetChunkAsync(string id, string repositoryId);
    Task<List<CodeChunk>> GetChunksByRepositoryAsync(string repositoryId);
    Task UpsertChunkAsync(CodeChunk chunk);
    Task UpsertChunksAsync(IEnumerable<CodeChunk> chunks);
    Task DeleteChunksByRepositoryAsync(string repositoryId);
    Task<List<CodeChunk>> VectorSearchAsync(string repositoryId, float[] queryEmbedding, int topK = 10);
    Task<Repository?> GetRepositoryAsync(string repositoryId);
    Task<List<Repository>> GetRepositoriesAsync();
    Task UpsertRepositoryAsync(Repository repository);
    Task<ConversationContext?> GetConversationAsync(string conversationId);
    Task UpsertConversationAsync(ConversationContext conversation);
}

public class CosmosDbContext : ICosmosDbContext
{
    private readonly Container _chunksContainer;
    private readonly Container _repositoriesContainer;
    private readonly Container _conversationsContainer;
    private readonly ILogger<CosmosDbContext> _logger;

    public CosmosDbContext(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CosmosDbContext> logger)
    {
        _logger = logger;
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "CodeAgentDb";
        var database = cosmosClient.GetDatabase(databaseName);

        _chunksContainer = database.GetContainer("chunks");
        _repositoriesContainer = database.GetContainer("repositories");
        _conversationsContainer = database.GetContainer("conversations");
    }

    public async Task<CodeChunk?> GetChunkAsync(string id, string repositoryId)
    {
        try
        {
            var response = await _chunksContainer.ReadItemAsync<CodeChunk>(id, new PartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<CodeChunk>> GetChunksByRepositoryAsync(string repositoryId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.repositoryId = @repositoryId")
            .WithParameter("@repositoryId", repositoryId);

        var chunks = new List<CodeChunk>();
        using var iterator = _chunksContainer.GetItemQueryIterator<CodeChunk>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            chunks.AddRange(response);
        }

        return chunks;
    }

    public async Task UpsertChunkAsync(CodeChunk chunk)
    {
        await _chunksContainer.UpsertItemAsync(chunk, new PartitionKey(chunk.RepositoryId));
    }

    public async Task UpsertChunksAsync(IEnumerable<CodeChunk> chunks)
    {
        var tasks = chunks.Select(chunk => UpsertChunkAsync(chunk));
        await Task.WhenAll(tasks);
    }

    public async Task DeleteChunksByRepositoryAsync(string repositoryId)
    {
        var chunks = await GetChunksByRepositoryAsync(repositoryId);
        var tasks = chunks.Select(chunk =>
            _chunksContainer.DeleteItemAsync<CodeChunk>(chunk.Id, new PartitionKey(repositoryId)));
        await Task.WhenAll(tasks);
    }

    public async Task<List<CodeChunk>> VectorSearchAsync(string repositoryId, float[] queryEmbedding, int topK = 10)
    {
        // Using Cosmos DB vector search with DiskANN index
        var queryText = @"
            SELECT TOP @topK c.id, c.repositoryId, c.filePath, c.fileName, c.language,
                   c.content, c.startLine, c.endLine, c.chunkType, c.symbolName,
                   c.metadata, c.createdAt,
                   VectorDistance(c.embedding, @embedding) AS score
            FROM c
            WHERE c.repositoryId = @repositoryId
            ORDER BY VectorDistance(c.embedding, @embedding)";

        var query = new QueryDefinition(queryText)
            .WithParameter("@topK", topK)
            .WithParameter("@repositoryId", repositoryId)
            .WithParameter("@embedding", queryEmbedding);

        var results = new List<CodeChunk>();
        using var iterator = _chunksContainer.GetItemQueryIterator<CodeChunk>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<Repository?> GetRepositoryAsync(string repositoryId)
    {
        try
        {
            var response = await _repositoriesContainer.ReadItemAsync<Repository>(repositoryId, new PartitionKey(repositoryId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<Repository>> GetRepositoriesAsync()
    {
        var query = new QueryDefinition("SELECT * FROM c");
        var repositories = new List<Repository>();
        using var iterator = _repositoriesContainer.GetItemQueryIterator<Repository>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            repositories.AddRange(response);
        }

        return repositories;
    }

    public async Task UpsertRepositoryAsync(Repository repository)
    {
        await _repositoriesContainer.UpsertItemAsync(repository, new PartitionKey(repository.Id));
    }

    public async Task<ConversationContext?> GetConversationAsync(string conversationId)
    {
        try
        {
            var response = await _conversationsContainer.ReadItemAsync<ConversationContext>(
                conversationId, new PartitionKey(conversationId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpsertConversationAsync(ConversationContext conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        await _conversationsContainer.UpsertItemAsync(conversation, new PartitionKey(conversation.Id));
    }
}
