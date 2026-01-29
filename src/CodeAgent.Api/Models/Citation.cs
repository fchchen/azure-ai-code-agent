namespace CodeAgent.Api.Models;

public class Citation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? SymbolName { get; set; }
    public double RelevanceScore { get; set; }
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
