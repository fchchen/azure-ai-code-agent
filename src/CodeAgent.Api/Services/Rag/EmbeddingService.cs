using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Models;

namespace CodeAgent.Api.Services.Rag;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<List<CodeChunk>> GenerateEmbeddingsAsync(List<CodeChunk> chunks);
}

public class EmbeddingService : IEmbeddingService
{
    private readonly ILlmClient _llmClient;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(ILlmClient llmClient, ILogger<EmbeddingService> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // Prepare text for embedding - include relevant context
        var preparedText = PrepareTextForEmbedding(text);
        return await _llmClient.GetEmbeddingAsync(preparedText);
    }

    public async Task<List<CodeChunk>> GenerateEmbeddingsAsync(List<CodeChunk> chunks)
    {
        _logger.LogInformation("Generating embeddings for {Count} chunks", chunks.Count);

        // Prepare texts with metadata context for better embeddings
        var texts = chunks.Select(chunk => PrepareChunkForEmbedding(chunk)).ToList();

        // Generate embeddings in batches
        var embeddings = await _llmClient.GetEmbeddingsAsync(texts);

        // Assign embeddings to chunks
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].Embedding = embeddings[i];
        }

        _logger.LogInformation("Generated embeddings for {Count} chunks", chunks.Count);
        return chunks;
    }

    private string PrepareTextForEmbedding(string text)
    {
        // Truncate if too long (embedding model has token limit)
        const int maxLength = 8000;
        if (text.Length > maxLength)
        {
            text = text[..maxLength];
        }
        return text;
    }

    private string PrepareChunkForEmbedding(CodeChunk chunk)
    {
        // Create a rich text representation for better semantic search
        var parts = new List<string>();

        // Add file context
        parts.Add($"File: {chunk.FilePath}");

        // Add symbol information if available
        if (!string.IsNullOrEmpty(chunk.SymbolName))
        {
            parts.Add($"{chunk.ChunkType}: {chunk.SymbolName}");
        }

        // Add language
        parts.Add($"Language: {chunk.Language}");

        // Add namespace/class context if available
        if (!string.IsNullOrEmpty(chunk.Metadata.Namespace))
        {
            parts.Add($"Namespace: {chunk.Metadata.Namespace}");
        }
        if (!string.IsNullOrEmpty(chunk.Metadata.ParentClass))
        {
            parts.Add($"Class: {chunk.Metadata.ParentClass}");
        }

        // Add the actual code content
        parts.Add($"Code:\n{chunk.Content}");

        var fullText = string.Join("\n", parts);
        return PrepareTextForEmbedding(fullText);
    }
}
