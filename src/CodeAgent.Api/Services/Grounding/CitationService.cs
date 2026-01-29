using System.Text.RegularExpressions;
using CodeAgent.Api.Models;

namespace CodeAgent.Api.Services.Grounding;

public interface ICitationService
{
    GroundedContent ExtractCitations(string content, List<string> toolResults);
    List<Citation> ExtractCitationsFromToolResults(List<string> toolResults);
    string AddCitationMarkers(string content, List<Citation> citations);
}

public class CitationService : ICitationService
{
    private readonly ILogger<CitationService> _logger;

    // Pattern to match file:line references like [path/to/file.cs:42] or [path/to/file.cs:10-20]
    private static readonly Regex FileLinePattern = new(
        @"\[([^\]]+?):(\d+)(?:-(\d+))?\]",
        RegexOptions.Compiled);

    // Pattern to extract file info from tool results
    private static readonly Regex ToolResultFilePattern = new(
        @"---\s*\[([^\]:]+):(\d+)-(\d+)\].*?---\s*```\w*\s*([\s\S]*?)```",
        RegexOptions.Compiled);

    public CitationService(ILogger<CitationService> logger)
    {
        _logger = logger;
    }

    public GroundedContent ExtractCitations(string content, List<string> toolResults)
    {
        var citations = new List<Citation>();
        var citationMap = new Dictionary<string, int>();

        // Extract citations from tool results
        var toolCitations = ExtractCitationsFromToolResults(toolResults);
        citations.AddRange(toolCitations);

        // Look for file:line references in the content
        var matches = FileLinePattern.Matches(content);
        foreach (Match match in matches)
        {
            var filePath = match.Groups[1].Value;
            var startLine = int.Parse(match.Groups[2].Value);
            var endLine = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : startLine;

            var existingCitation = citations.FirstOrDefault(c =>
                c.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) &&
                c.StartLine == startLine);

            if (existingCitation == null)
            {
                var citation = new Citation
                {
                    FilePath = filePath,
                    StartLine = startLine,
                    EndLine = endLine,
                    SourceType = "reference"
                };
                citations.Add(citation);
            }
        }

        // Deduplicate and assign indices
        var uniqueCitations = citations
            .GroupBy(c => new { c.FilePath, c.StartLine, c.EndLine })
            .Select(g => g.First())
            .ToList();

        for (int i = 0; i < uniqueCitations.Count; i++)
        {
            var key = $"{uniqueCitations[i].FilePath}:{uniqueCitations[i].StartLine}";
            citationMap[key] = i + 1;
        }

        // Replace file:line references with citation markers
        var processedContent = AddCitationMarkers(content, uniqueCitations);

        return new GroundedContent
        {
            Content = processedContent,
            Citations = uniqueCitations,
            CitationMap = citationMap
        };
    }

    public List<Citation> ExtractCitationsFromToolResults(List<string> toolResults)
    {
        var citations = new List<Citation>();

        foreach (var result in toolResults)
        {
            var matches = ToolResultFilePattern.Matches(result);
            foreach (Match match in matches)
            {
                var citation = new Citation
                {
                    FilePath = match.Groups[1].Value,
                    StartLine = int.Parse(match.Groups[2].Value),
                    EndLine = int.Parse(match.Groups[3].Value),
                    Content = TruncateContent(match.Groups[4].Value, 500),
                    SourceType = "code_search"
                };

                // Try to extract symbol name from the result header
                var headerMatch = Regex.Match(result, @"\((\w+):\s*(\w+)\)");
                if (headerMatch.Success)
                {
                    citation.SymbolName = headerMatch.Groups[2].Value;
                }

                // Extract score if available
                var scoreMatch = Regex.Match(match.Value, @"\[Score:\s*([\d.]+)\]");
                if (scoreMatch.Success && double.TryParse(scoreMatch.Groups[1].Value, out var score))
                {
                    citation.RelevanceScore = score;
                }

                citations.Add(citation);
            }
        }

        // Sort by relevance score
        return citations.OrderByDescending(c => c.RelevanceScore).ToList();
    }

    public string AddCitationMarkers(string content, List<Citation> citations)
    {
        var result = content;

        // Build lookup dictionary
        var citationLookup = new Dictionary<string, int>();
        for (int i = 0; i < citations.Count; i++)
        {
            var c = citations[i];
            citationLookup[$"{c.FilePath}:{c.StartLine}"] = i + 1;
            citationLookup[$"{c.FilePath}:{c.StartLine}-{c.EndLine}"] = i + 1;
        }

        // Replace references with citation markers
        result = FileLinePattern.Replace(result, match =>
        {
            var filePath = match.Groups[1].Value;
            var startLine = match.Groups[2].Value;
            var endLine = match.Groups[3].Success ? match.Groups[3].Value : startLine;

            var key = $"{filePath}:{startLine}";
            if (citationLookup.TryGetValue(key, out var index))
            {
                return $"[{index}]";
            }

            key = $"{filePath}:{startLine}-{endLine}";
            if (citationLookup.TryGetValue(key, out index))
            {
                return $"[{index}]";
            }

            return match.Value; // Keep original if no match
        });

        return result;
    }

    private string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength)
            return content.Trim();

        return content[..maxLength].Trim() + "...";
    }
}
