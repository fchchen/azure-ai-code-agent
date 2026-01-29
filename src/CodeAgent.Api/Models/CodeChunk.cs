using System.Text.Json.Serialization;

namespace CodeAgent.Api.Models;

public class CodeChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("repositoryId")]
    public string RepositoryId { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; set; }

    [JsonPropertyName("chunkType")]
    public string ChunkType { get; set; } = "code"; // code, class, method, function, comment

    [JsonPropertyName("symbolName")]
    public string? SymbolName { get; set; } // class/method/function name if applicable

    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }

    [JsonPropertyName("metadata")]
    public ChunkMetadata Metadata { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ChunkMetadata
{
    [JsonPropertyName("parentClass")]
    public string? ParentClass { get; set; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("imports")]
    public List<string> Imports { get; set; } = new();

    [JsonPropertyName("references")]
    public List<string> References { get; set; } = new();

    [JsonPropertyName("complexity")]
    public int? Complexity { get; set; }
}

public class Repository
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("indexedAt")]
    public DateTime? IndexedAt { get; set; }

    [JsonPropertyName("chunkCount")]
    public int ChunkCount { get; set; }

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();
}
