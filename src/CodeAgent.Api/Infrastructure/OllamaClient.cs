using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAgent.Api.Infrastructure;

public class OllamaClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _embeddingModel;
    private readonly ILogger<OllamaClient> _logger;

    public OllamaClient(IConfiguration configuration, ILogger<OllamaClient> logger)
    {
        _logger = logger;

        var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = configuration["Ollama:Model"] ?? "qwen2.5-coder:7b";
        _embeddingModel = configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(5) // Local models can be slow
        };
    }

    public async Task<LlmChatCompletion> GetChatCompletionAsync(
        List<LlmMessage> messages,
        List<LlmToolDefinition>? tools = null)
    {
        var ollamaMessages = messages.Select(m => new OllamaMessage
        {
            Role = m.Role == "tool" ? "user" : m.Role,
            Content = m.Role == "tool" ? $"Tool result for {m.ToolCallId}: {m.Content}" : m.Content ?? ""
        }).ToList();

        var request = new OllamaChatRequest
        {
            Model = _model,
            Messages = ollamaMessages,
            Stream = false
        };

        // Add tools if provided (Ollama supports tool calling for some models)
        if (tools?.Count > 0)
        {
            request.Tools = tools.Select(t => new OllamaTool
            {
                Type = "function",
                Function = new OllamaFunction
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Parameters
                }
            }).ToList();
        }

        var response = await _httpClient.PostAsJsonAsync("/api/chat", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>();

        // Check for proper tool_calls in response
        if (result?.Message?.ToolCalls?.Count > 0)
        {
            var toolCalls = result.Message.ToolCalls.Select(tc => new LlmToolCall
            {
                Id = $"call_{tc.Function?.Name}_{Guid.NewGuid():N}",
                FunctionName = tc.Function?.Name ?? "",
                Arguments = JsonSerializer.Serialize(tc.Function?.Arguments ?? new Dictionary<string, object>())
            }).ToList();

            return new LlmChatCompletion { ToolCalls = toolCalls };
        }

        // Some models (like qwen2.5-coder) return tool calls as JSON in content
        var content = result?.Message?.Content;
        Console.WriteLine($"[OllamaClient] Tools: {tools?.Count ?? 0}, Content: {content?.Length ?? 0} chars");

        if (tools?.Count > 0 && !string.IsNullOrEmpty(content))
        {
            Console.WriteLine("[OllamaClient] Parsing tool call...");
            var toolCall = TryParseToolCallFromContent(content, tools);
            if (toolCall != null)
            {
                Console.WriteLine($"[OllamaClient] Found: {toolCall.FunctionName}");
                return new LlmChatCompletion { ToolCalls = new List<LlmToolCall> { toolCall } };
            }
            Console.WriteLine("[OllamaClient] No tool call extracted");
        }

        return new LlmChatCompletion { Content = content };
    }

    public async IAsyncEnumerable<string> GetStreamingChatCompletionAsync(
        List<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ollamaMessages = messages.Select(m => new OllamaMessage
        {
            Role = m.Role == "tool" ? "user" : m.Role,
            Content = m.Content ?? ""
        }).ToList();

        var request = new OllamaChatRequest
        {
            Model = _model,
            Messages = ollamaMessages,
            Stream = true
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = jsonContent
        };

        var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line);
            if (!string.IsNullOrEmpty(chunk?.Message?.Content))
            {
                yield return chunk.Message.Content;
            }
        }
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var request = new OllamaEmbeddingRequest
        {
            Model = _embeddingModel,
            Prompt = text
        };

        var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
        return result?.Embedding ?? Array.Empty<float>();
    }

    public async Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts)
    {
        var results = new List<float[]>();

        foreach (var text in texts)
        {
            var embedding = await GetEmbeddingAsync(text);
            results.Add(embedding);
        }

        return results;
    }

    private LlmToolCall? TryParseToolCallFromContent(string content, List<LlmToolDefinition> tools)
    {
        try
        {
            // Try to find JSON object in the content (may be embedded in ReAct-style output)
            var jsonContent = ExtractJsonFromContent(content);
            Console.WriteLine($"[OllamaClient] Extracted JSON: {jsonContent?[..Math.Min(jsonContent.Length, 100)] ?? "(null)"}");
            if (string.IsNullOrEmpty(jsonContent)) return null;

            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Check for {"name": "...", "arguments": {...}} format
            if (root.TryGetProperty("name", out var nameElement))
            {
                var toolName = nameElement.GetString();
                if (string.IsNullOrEmpty(toolName)) return null;

                Console.WriteLine($"[OllamaClient] Looking for tool: {toolName}");
                Console.WriteLine($"[OllamaClient] Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

                // Find matching tool (with fuzzy matching for common variations)
                var matchedTool = tools.FirstOrDefault(t =>
                    t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeToolName(t.Name) == NormalizeToolName(toolName));

                if (matchedTool == null)
                {
                    Console.WriteLine($"[OllamaClient] No matching tool found for: {toolName}");
                    return null;
                }

                Console.WriteLine($"[OllamaClient] Matched tool: {matchedTool.Name}");
                toolName = matchedTool.Name; // Use the actual tool name

                var arguments = "{}";
                if (root.TryGetProperty("arguments", out var argsElement))
                {
                    arguments = argsElement.GetRawText();
                }

                return new LlmToolCall
                {
                    Id = $"call_{toolName}_{Guid.NewGuid():N}",
                    FunctionName = toolName,
                    Arguments = arguments
                };
            }
        }
        catch (JsonException)
        {
            // Not valid JSON, return null
        }

        return null;
    }

    private string? ExtractJsonFromContent(string content)
    {
        // Find the first { and try to extract a balanced JSON object
        var startIndex = content.IndexOf('{');
        if (startIndex < 0) return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = startIndex; i < content.Length; i++)
        {
            var c = content[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content.Substring(startIndex, i - startIndex + 1);
                }
            }
        }

        return null;
    }

    private static string NormalizeToolName(string name)
    {
        // Normalize tool names: remove underscores, lowercase, sort words
        var words = name.ToLowerInvariant()
            .Split('_', '-', ' ')
            .Where(w => !string.IsNullOrEmpty(w))
            .OrderBy(w => w);
        return string.Join("", words);
    }
}

// Request/Response models
internal class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<OllamaMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaTool>? Tools { get; set; }
}

internal class OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

internal class OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OllamaFunction? Function { get; set; }
}

internal class OllamaFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public object? Parameters { get; set; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Arguments { get; set; }
}

internal class OllamaToolCall
{
    [JsonPropertyName("function")]
    public OllamaFunction? Function { get; set; }
}

internal class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

internal class OllamaEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";
}

internal class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }
}
