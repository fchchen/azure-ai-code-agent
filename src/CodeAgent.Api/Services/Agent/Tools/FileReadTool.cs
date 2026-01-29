using System.Text.Json;
using OpenAI.Chat;
using CodeAgent.Api.Infrastructure;

namespace CodeAgent.Api.Services.Agent.Tools;

public class FileReadTool : AgentToolBase
{
    private readonly ICosmosDbContext _cosmosDb;
    private readonly ILogger<FileReadTool> _logger;

    public override string Name => "read_file";
    public override string Description => AgentPrompts.GetFileReadToolDescription();

    public FileReadTool(ICosmosDbContext cosmosDb, ILogger<FileReadTool> logger)
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
                    ["file_path"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "The file path relative to repository root"
                    },
                    ["start_line"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["description"] = "Optional: start line number to read from"
                    },
                    ["end_line"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["description"] = "Optional: end line number to read to"
                    }
                },
                new List<string> { "file_path" }
            )
        );
    }

    public override async Task<string> ExecuteAsync(string input, string repositoryId)
    {
        try
        {
            var args = JsonSerializer.Deserialize<FileReadInput>(input);
            if (args == null || string.IsNullOrEmpty(args.FilePath))
            {
                return "Error: 'file_path' parameter is required";
            }

            _logger.LogInformation("Reading file: {FilePath} from {RepositoryId}", args.FilePath, repositoryId);

            // Get all chunks for this file
            var allChunks = await _cosmosDb.GetChunksByRepositoryAsync(repositoryId);
            var fileChunks = allChunks
                .Where(c => c.FilePath.Equals(args.FilePath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.StartLine)
                .ToList();

            if (fileChunks.Count == 0)
            {
                // Try partial match
                fileChunks = allChunks
                    .Where(c => c.FilePath.Contains(args.FilePath, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.FilePath)
                    .ThenBy(c => c.StartLine)
                    .ToList();

                if (fileChunks.Count == 0)
                {
                    return $"File not found: {args.FilePath}. Make sure the path is correct.";
                }

                // If multiple files match, list them
                var distinctFiles = fileChunks.Select(c => c.FilePath).Distinct().ToList();
                if (distinctFiles.Count > 1)
                {
                    return $"Multiple files match '{args.FilePath}':\n" +
                           string.Join("\n", distinctFiles.Select(f => $"  - {f}"));
                }
            }

            // Reconstruct file content from chunks
            var content = new List<string>();
            var language = fileChunks.First().Language;
            var actualFilePath = fileChunks.First().FilePath;

            foreach (var chunk in fileChunks)
            {
                content.Add(chunk.Content);
            }

            var fullContent = string.Join("\n", content);
            var lines = fullContent.Split('\n');

            // Apply line range if specified
            if (args.StartLine.HasValue || args.EndLine.HasValue)
            {
                var start = (args.StartLine ?? 1) - 1;
                var end = (args.EndLine ?? lines.Length) - 1;

                start = Math.Max(0, start);
                end = Math.Min(lines.Length - 1, end);

                lines = lines.Skip(start).Take(end - start + 1).ToArray();

                return $"File: {actualFilePath} (lines {start + 1}-{end + 1})\n```{language}\n" +
                       FormatWithLineNumbers(lines, start + 1) + "\n```";
            }

            return $"File: {actualFilePath}\n```{language}\n" +
                   FormatWithLineNumbers(lines, 1) + "\n```";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file");
            return $"Error reading file: {ex.Message}";
        }
    }

    private string FormatWithLineNumbers(string[] lines, int startLine)
    {
        var maxLineNum = startLine + lines.Length - 1;
        var padding = maxLineNum.ToString().Length;

        var numbered = lines.Select((line, index) =>
            $"{(startLine + index).ToString().PadLeft(padding)} | {line}");

        return string.Join("\n", numbered);
    }

    private class FileReadInput
    {
        public string FilePath { get; set; } = string.Empty;
        public int? StartLine { get; set; }
        public int? EndLine { get; set; }
    }
}
