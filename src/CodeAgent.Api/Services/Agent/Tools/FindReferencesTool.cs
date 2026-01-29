using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI.Chat;
using CodeAgent.Api.Infrastructure;

namespace CodeAgent.Api.Services.Agent.Tools;

public class FindReferencesTool : AgentToolBase
{
    private readonly ICosmosDbContext _cosmosDb;
    private readonly ILogger<FindReferencesTool> _logger;

    public override string Name => "find_references";
    public override string Description => AgentPrompts.GetFindReferencesToolDescription();

    public FindReferencesTool(ICosmosDbContext cosmosDb, ILogger<FindReferencesTool> logger)
    {
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public override ChatTool GetToolDefinition()
    {
        return ChatTool.CreateFunctionTool(
            Name,
            Description,
            CreateParameters(
                new Dictionary<string, object>
                {
                    ["symbol"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "The symbol name to search for (function, class, or variable name)"
                    },
                    ["type"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["enum"] = new[] { "function", "class", "variable", "any" },
                        ["description"] = "Optional: type of symbol to search for"
                    },
                    ["include_definitions"] = new Dictionary<string, object>
                    {
                        ["type"] = "boolean",
                        ["description"] = "Include definition locations (default: true)"
                    }
                },
                new List<string> { "symbol" }
            )
        );
    }

    public override async Task<string> ExecuteAsync(string input, string repositoryId)
    {
        try
        {
            var args = JsonSerializer.Deserialize<FindReferencesInput>(input);
            if (args == null || string.IsNullOrEmpty(args.Symbol))
            {
                return "Error: 'symbol' parameter is required";
            }

            _logger.LogInformation("Finding references for: {Symbol} in {RepositoryId}", args.Symbol, repositoryId);

            var allChunks = await _cosmosDb.GetChunksByRepositoryAsync(repositoryId);
            var references = new List<ReferenceResult>();

            // Build regex patterns based on symbol type
            var patterns = BuildSearchPatterns(args.Symbol, args.Type);

            foreach (var chunk in allChunks)
            {
                var lines = chunk.Content.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    foreach (var (pattern, refType) in patterns)
                    {
                        if (pattern.IsMatch(line))
                        {
                            // Skip definition if not requested
                            if (!args.IncludeDefinitions && refType == "definition")
                                continue;

                            references.Add(new ReferenceResult
                            {
                                FilePath = chunk.FilePath,
                                LineNumber = chunk.StartLine + i,
                                Line = line.Trim(),
                                ReferenceType = refType,
                                Language = chunk.Language
                            });
                        }
                    }
                }
            }

            if (references.Count == 0)
            {
                return $"No references found for '{args.Symbol}'.";
            }

            // Group by reference type
            var grouped = references
                .GroupBy(r => r.ReferenceType)
                .OrderBy(g => g.Key == "definition" ? 0 : 1);

            var output = new List<string> { $"Found {references.Count} references to '{args.Symbol}':\n" };

            foreach (var group in grouped)
            {
                output.Add($"### {char.ToUpper(group.Key[0]) + group.Key[1..]}s ({group.Count()})");

                foreach (var reference in group.Take(20)) // Limit to 20 per type
                {
                    output.Add($"  [{reference.FilePath}:{reference.LineNumber}]");
                    output.Add($"    {reference.Line}");
                }

                if (group.Count() > 20)
                {
                    output.Add($"  ... and {group.Count() - 20} more");
                }
                output.Add("");
            }

            return string.Join("\n", output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding references");
            return $"Error finding references: {ex.Message}";
        }
    }

    private List<(Regex pattern, string type)> BuildSearchPatterns(string symbol, string? symbolType)
    {
        var patterns = new List<(Regex, string)>();
        var escapedSymbol = Regex.Escape(symbol);

        // Word boundary patterns for the symbol
        var wordPattern = $@"\b{escapedSymbol}\b";

        if (symbolType == null || symbolType == "any")
        {
            // Generic usage pattern
            patterns.Add((new Regex(wordPattern, RegexOptions.Compiled), "usage"));

            // Definition patterns for common languages
            patterns.Add((new Regex($@"(class|interface|struct|enum)\s+{escapedSymbol}\b", RegexOptions.Compiled), "definition"));
            patterns.Add((new Regex($@"(function|def|fn|func)\s+{escapedSymbol}\s*\(", RegexOptions.Compiled), "definition"));
            patterns.Add((new Regex($@"(const|let|var|val)\s+{escapedSymbol}\s*[=:]", RegexOptions.Compiled), "definition"));
            patterns.Add((new Regex($@"(public|private|protected|internal).*\s+{escapedSymbol}\s*\(", RegexOptions.Compiled), "definition"));
        }
        else
        {
            switch (symbolType)
            {
                case "class":
                    patterns.Add((new Regex($@"(class|interface|struct|enum)\s+{escapedSymbol}\b", RegexOptions.Compiled), "definition"));
                    patterns.Add((new Regex($@"(new\s+{escapedSymbol}|:\s*{escapedSymbol}|extends\s+{escapedSymbol}|implements\s+{escapedSymbol})", RegexOptions.Compiled), "usage"));
                    patterns.Add((new Regex($@"{escapedSymbol}\s*\.", RegexOptions.Compiled), "usage"));
                    break;

                case "function":
                    patterns.Add((new Regex($@"(function|def|fn|func)\s+{escapedSymbol}\s*\(", RegexOptions.Compiled), "definition"));
                    patterns.Add((new Regex($@"(public|private|protected|internal).*\s+{escapedSymbol}\s*\(", RegexOptions.Compiled), "definition"));
                    patterns.Add((new Regex($@"{escapedSymbol}\s*\(", RegexOptions.Compiled), "call"));
                    break;

                case "variable":
                    patterns.Add((new Regex($@"(const|let|var|val|readonly)\s+{escapedSymbol}\s*[=:]", RegexOptions.Compiled), "definition"));
                    patterns.Add((new Regex(wordPattern, RegexOptions.Compiled), "usage"));
                    break;
            }
        }

        return patterns;
    }

    private class FindReferencesInput
    {
        public string Symbol { get; set; } = string.Empty;
        public string? Type { get; set; }
        public bool IncludeDefinitions { get; set; } = true;
    }

    private class ReferenceResult
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Line { get; set; } = string.Empty;
        public string ReferenceType { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
    }
}
