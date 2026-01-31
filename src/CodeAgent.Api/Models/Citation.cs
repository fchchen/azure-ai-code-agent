using System.Text.Json.Serialization;

namespace CodeAgent.Api.Models;

public class Citation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("symbolName")]
    public string? SymbolName { get; set; }

    [JsonPropertyName("relevanceScore")]
    public double RelevanceScore { get; set; }

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "code_search"; // code_search, file_read, reference

    public string FormatReference()
    {
        if (StartLine == EndLine)
            return $"{FilePath}:{StartLine}";
        return $"{FilePath}:{StartLine}-{EndLine}";
    }
}

public class GroundedContent
{
    public string Content { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = new();
    public Dictionary<string, int> CitationMap { get; set; } = new(); // Maps [1], [2] etc to citation index
}
