using System.Text.RegularExpressions;
using CodeAgent.Api.Models;

namespace CodeAgent.Api.Services.Rag;

public interface IDocumentChunker
{
    Task<List<CodeChunk>> ChunkRepositoryAsync(string repositoryPath, string repositoryId);
    List<CodeChunk> ChunkFile(string filePath, string content, string repositoryId);
}

public class DocumentChunker : IDocumentChunker
{
    private readonly ILogger<DocumentChunker> _logger;
    private readonly int _maxChunkSize;
    private readonly int _overlapSize;

    private static readonly Dictionary<string, string> ExtensionToLanguage = new()
    {
        { ".cs", "csharp" },
        { ".ts", "typescript" },
        { ".tsx", "typescript" },
        { ".js", "javascript" },
        { ".jsx", "javascript" },
        { ".py", "python" },
        { ".java", "java" },
        { ".go", "go" },
        { ".rs", "rust" },
        { ".cpp", "cpp" },
        { ".c", "c" },
        { ".h", "c" },
        { ".hpp", "cpp" },
        { ".rb", "ruby" },
        { ".php", "php" },
        { ".swift", "swift" },
        { ".kt", "kotlin" },
        { ".scala", "scala" },
        { ".sql", "sql" },
        { ".json", "json" },
        { ".yaml", "yaml" },
        { ".yml", "yaml" },
        { ".xml", "xml" },
        { ".md", "markdown" },
        { ".sh", "bash" },
        { ".ps1", "powershell" },
        { ".bicep", "bicep" },
        { ".tf", "terraform" }
    };

    private static readonly HashSet<string> ExcludedDirectories = new()
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea",
        "dist", "build", "out", "target", "__pycache__", ".venv",
        "venv", "packages", "vendor", ".next", ".nuxt"
    };

    private static readonly HashSet<string> ExcludedFiles = new()
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        ".gitignore", ".dockerignore", "*.min.js", "*.min.css"
    };

    public DocumentChunker(ILogger<DocumentChunker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _maxChunkSize = configuration.GetValue("Chunking:MaxChunkSize", 1500);
        _overlapSize = configuration.GetValue("Chunking:OverlapSize", 200);
    }

    public async Task<List<CodeChunk>> ChunkRepositoryAsync(string repositoryPath, string repositoryId)
    {
        var chunks = new List<CodeChunk>();
        var files = GetCodeFiles(repositoryPath);

        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var relativePath = Path.GetRelativePath(repositoryPath, file);
                var fileChunks = ChunkFile(relativePath, content, repositoryId);
                chunks.AddRange(fileChunks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to chunk file: {File}", file);
            }
        }

        _logger.LogInformation("Created {Count} chunks from repository {RepositoryId}", chunks.Count, repositoryId);
        return chunks;
    }

    public List<CodeChunk> ChunkFile(string filePath, string content, string repositoryId)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var language = ExtensionToLanguage.GetValueOrDefault(extension, "text");
        var fileName = Path.GetFileName(filePath);

        // Try semantic chunking first for supported languages
        var chunks = language switch
        {
            "csharp" => ChunkCSharp(content, filePath, repositoryId, language),
            "typescript" or "javascript" => ChunkTypeScript(content, filePath, repositoryId, language),
            "python" => ChunkPython(content, filePath, repositoryId, language),
            _ => ChunkBySize(content, filePath, repositoryId, language)
        };

        // Set common properties
        foreach (var chunk in chunks)
        {
            chunk.FileName = fileName;
        }

        return chunks;
    }

    private List<CodeChunk> ChunkCSharp(string content, string filePath, string repositoryId, string language)
    {
        var chunks = new List<CodeChunk>();
        var lines = content.Split('\n');

        // Regex patterns for C# constructs
        var classPattern = new Regex(@"^\s*(public|private|internal|protected)?\s*(static|abstract|sealed)?\s*(partial)?\s*class\s+(\w+)", RegexOptions.Multiline);
        var methodPattern = new Regex(@"^\s*(public|private|internal|protected)?\s*(static|async|virtual|override|abstract)?\s*[\w<>\[\],\s]+\s+(\w+)\s*\(", RegexOptions.Multiline);
        var namespacePattern = new Regex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);

        string? currentNamespace = null;
        string? currentClass = null;

        var namespaceMatch = namespacePattern.Match(content);
        if (namespaceMatch.Success)
        {
            currentNamespace = namespaceMatch.Groups[1].Value;
        }

        // Find class boundaries
        var classMatches = classPattern.Matches(content);
        if (classMatches.Count == 0)
        {
            return ChunkBySize(content, filePath, repositoryId, language);
        }

        foreach (Match classMatch in classMatches)
        {
            currentClass = classMatch.Groups[4].Value;
            var classStartLine = content[..classMatch.Index].Count(c => c == '\n') + 1;

            // Find methods within this class region
            var classContent = ExtractClassContent(content, classMatch.Index);
            var methodMatches = methodPattern.Matches(classContent);

            if (methodMatches.Count == 0)
            {
                // Chunk the whole class
                var chunk = CreateChunk(classContent, filePath, repositoryId, language,
                    classStartLine, "class", currentClass);
                chunk.Metadata.Namespace = currentNamespace;
                chunks.Add(chunk);
            }
            else
            {
                // Chunk by methods
                foreach (Match methodMatch in methodMatches)
                {
                    var methodName = methodMatch.Groups[3].Value;
                    var methodContent = ExtractMethodContent(classContent, methodMatch.Index);
                    var methodStartLine = classStartLine + classContent[..methodMatch.Index].Count(c => c == '\n');

                    var chunk = CreateChunk(methodContent, filePath, repositoryId, language,
                        methodStartLine, "method", methodName);
                    chunk.Metadata.ParentClass = currentClass;
                    chunk.Metadata.Namespace = currentNamespace;
                    chunks.Add(chunk);
                }
            }
        }

        return chunks.Count > 0 ? chunks : ChunkBySize(content, filePath, repositoryId, language);
    }

    private List<CodeChunk> ChunkTypeScript(string content, string filePath, string repositoryId, string language)
    {
        var chunks = new List<CodeChunk>();

        // Patterns for TypeScript/JavaScript
        var functionPattern = new Regex(@"^\s*(export\s+)?(async\s+)?function\s+(\w+)|^\s*(export\s+)?(const|let|var)\s+(\w+)\s*=\s*(async\s*)?\(|^\s*(export\s+)?class\s+(\w+)", RegexOptions.Multiline);

        var matches = functionPattern.Matches(content);
        if (matches.Count == 0)
        {
            return ChunkBySize(content, filePath, repositoryId, language);
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var startIndex = match.Index;
            var endIndex = i < matches.Count - 1 ? matches[i + 1].Index : content.Length;

            var symbolName = match.Groups[3].Value;
            if (string.IsNullOrEmpty(symbolName)) symbolName = match.Groups[6].Value;
            if (string.IsNullOrEmpty(symbolName)) symbolName = match.Groups[9].Value;

            var chunkContent = content[startIndex..endIndex].TrimEnd();
            var startLine = content[..startIndex].Count(c => c == '\n') + 1;

            var chunkType = match.Groups[9].Success ? "class" : "function";

            chunks.Add(CreateChunk(chunkContent, filePath, repositoryId, language, startLine, chunkType, symbolName));
        }

        return chunks;
    }

    private List<CodeChunk> ChunkPython(string content, string filePath, string repositoryId, string language)
    {
        var chunks = new List<CodeChunk>();

        // Patterns for Python
        var pattern = new Regex(@"^(class\s+(\w+)|def\s+(\w+))", RegexOptions.Multiline);

        var matches = pattern.Matches(content);
        if (matches.Count == 0)
        {
            return ChunkBySize(content, filePath, repositoryId, language);
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var startIndex = match.Index;
            var endIndex = i < matches.Count - 1 ? matches[i + 1].Index : content.Length;

            var symbolName = match.Groups[2].Value;
            if (string.IsNullOrEmpty(symbolName)) symbolName = match.Groups[3].Value;

            var chunkContent = content[startIndex..endIndex].TrimEnd();
            var startLine = content[..startIndex].Count(c => c == '\n') + 1;

            var chunkType = match.Groups[2].Success ? "class" : "function";

            chunks.Add(CreateChunk(chunkContent, filePath, repositoryId, language, startLine, chunkType, symbolName));
        }

        return chunks;
    }

    private List<CodeChunk> ChunkBySize(string content, string filePath, string repositoryId, string language)
    {
        var chunks = new List<CodeChunk>();
        var lines = content.Split('\n');

        int currentLine = 0;
        while (currentLine < lines.Length)
        {
            var chunkLines = new List<string>();
            var chunkLength = 0;
            var startLine = currentLine + 1;

            while (currentLine < lines.Length && chunkLength < _maxChunkSize)
            {
                chunkLines.Add(lines[currentLine]);
                chunkLength += lines[currentLine].Length + 1;
                currentLine++;
            }

            if (chunkLines.Count > 0)
            {
                var chunkContent = string.Join('\n', chunkLines);
                chunks.Add(CreateChunk(chunkContent, filePath, repositoryId, language, startLine, "code", null));
            }

            // Apply overlap
            if (currentLine < lines.Length)
            {
                var overlapLines = Math.Min(_overlapSize / 50, chunkLines.Count / 2);
                currentLine -= overlapLines;
            }
        }

        return chunks;
    }

    private CodeChunk CreateChunk(string content, string filePath, string repositoryId, string language,
        int startLine, string chunkType, string? symbolName)
    {
        var lineCount = content.Count(c => c == '\n') + 1;

        return new CodeChunk
        {
            RepositoryId = repositoryId,
            FilePath = filePath,
            Language = language,
            Content = content,
            StartLine = startLine,
            EndLine = startLine + lineCount - 1,
            ChunkType = chunkType,
            SymbolName = symbolName
        };
    }

    private string ExtractClassContent(string content, int classStartIndex)
    {
        var braceCount = 0;
        var inClass = false;
        var endIndex = classStartIndex;

        for (int i = classStartIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                braceCount++;
                inClass = true;
            }
            else if (content[i] == '}')
            {
                braceCount--;
                if (inClass && braceCount == 0)
                {
                    endIndex = i + 1;
                    break;
                }
            }
        }

        return content[classStartIndex..endIndex];
    }

    private string ExtractMethodContent(string content, int methodStartIndex)
    {
        var braceCount = 0;
        var inMethod = false;
        var endIndex = methodStartIndex;

        for (int i = methodStartIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                braceCount++;
                inMethod = true;
            }
            else if (content[i] == '}')
            {
                braceCount--;
                if (inMethod && braceCount == 0)
                {
                    endIndex = i + 1;
                    break;
                }
            }
        }

        return content[methodStartIndex..endIndex];
    }

    private IEnumerable<string> GetCodeFiles(string repositoryPath)
    {
        var files = new List<string>();

        foreach (var file in Directory.EnumerateFiles(repositoryPath, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryPath, file);
            var pathParts = relativePath.Split(Path.DirectorySeparatorChar);

            // Skip excluded directories
            if (pathParts.Any(part => ExcludedDirectories.Contains(part)))
                continue;

            // Skip excluded files
            var fileName = Path.GetFileName(file);
            if (ExcludedFiles.Contains(fileName))
                continue;

            // Only include known code file extensions
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (ExtensionToLanguage.ContainsKey(extension))
            {
                files.Add(file);
            }
        }

        return files;
    }
}
