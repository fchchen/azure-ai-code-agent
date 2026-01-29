using System.Runtime.CompilerServices;
using System.Text.Json;
using Mscc.GenerativeAI;

namespace CodeAgent.Api.Infrastructure;

public class GeminiClient : ILlmClient
{
    private readonly IGenerativeAI _googleAi;
    private readonly string _model;
    private readonly string _embeddingModel;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(IConfiguration configuration, ILogger<GeminiClient> logger)
    {
        _logger = logger;

        var apiKey = configuration["Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini:ApiKey is not configured");

        _model = configuration["Gemini:Model"] ?? "gemini-1.5-flash";
        _embeddingModel = configuration["Gemini:EmbeddingModel"] ?? "text-embedding-004";

        _googleAi = new GoogleAI(apiKey);
    }

    public async Task<LlmChatCompletion> GetChatCompletionAsync(
        List<LlmMessage> messages,
        List<LlmToolDefinition>? tools = null)
    {
        var model = _googleAi.GenerativeModel(model: _model);

        // Extract system prompt and build chat history
        string? systemInstruction = null;
        var chatMessages = new List<ContentResponse>();

        foreach (var msg in messages)
        {
            if (msg.Role == "system")
            {
                systemInstruction = msg.Content;
            }
            else if (msg.Role == "user")
            {
                chatMessages.Add(new ContentResponse { Role = "user", Text = msg.Content });
            }
            else if (msg.Role == "assistant" && !string.IsNullOrEmpty(msg.Content))
            {
                chatMessages.Add(new ContentResponse { Role = "model", Text = msg.Content });
            }
            else if (msg.Role == "tool")
            {
                // Add tool result as user message
                chatMessages.Add(new ContentResponse { Role = "user", Text = $"Tool result: {msg.Content}" });
            }
        }

        // Build the prompt from messages
        var lastUserMessage = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        var promptParts = new List<string>();

        if (!string.IsNullOrEmpty(systemInstruction))
        {
            promptParts.Add(systemInstruction);
        }

        // Add conversation context
        foreach (var msg in chatMessages.Take(chatMessages.Count - 1))
        {
            promptParts.Add($"{msg.Role}: {msg.Text}");
        }

        promptParts.Add(lastUserMessage);
        var fullPrompt = string.Join("\n\n", promptParts);

        // Configure tools if provided
        if (tools?.Count > 0)
        {
            var toolConfigs = tools.Select(t => new FunctionDeclaration
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = new Schema
                {
                    Type = ParameterType.Object,
                    Properties = ConvertParameters(t.Parameters)
                }
            }).ToList();

            var geminiTools = new Tool { FunctionDeclarations = toolConfigs };

            var requestWithTools = new GenerateContentRequest
            {
                Contents = new List<Content>
                {
                    new Content(fullPrompt)
                },
                Tools = new List<Tool> { geminiTools }
            };

            var responseWithTools = await model.GenerateContent(requestWithTools);

            // Check for function calls
            var functionCall = responseWithTools.Candidates?.FirstOrDefault()
                ?.Content?.Parts?.FirstOrDefault(p => p.FunctionCall != null)?.FunctionCall;

            if (functionCall != null)
            {
                var toolCall = new LlmToolCall
                {
                    Id = $"call_{functionCall.Name}",
                    FunctionName = functionCall.Name,
                    Arguments = JsonSerializer.Serialize(functionCall.Args ?? new Dictionary<string, object>())
                };

                return new LlmChatCompletion { ToolCalls = new List<LlmToolCall> { toolCall } };
            }

            return new LlmChatCompletion { Content = responseWithTools.Text };
        }

        // Simple request without tools
        var response = await model.GenerateContent(fullPrompt);
        return new LlmChatCompletion { Content = response.Text };
    }

    public async IAsyncEnumerable<string> GetStreamingChatCompletionAsync(
        List<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = _googleAi.GenerativeModel(model: _model);

        // Build prompt from messages
        var promptParts = new List<string>();
        foreach (var msg in messages)
        {
            if (msg.Role == "system")
            {
                promptParts.Add(msg.Content ?? "");
            }
            else if (msg.Role == "user")
            {
                promptParts.Add($"User: {msg.Content}");
            }
            else if (msg.Role == "assistant")
            {
                promptParts.Add($"Assistant: {msg.Content}");
            }
        }

        var fullPrompt = string.Join("\n\n", promptParts);

        var request = new GenerateContentRequest
        {
            Contents = new List<Content> { new Content(fullPrompt) }
        };

        var stream = model.GenerateContentStream(request);

        await foreach (var response in stream)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var text = response.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var model = _googleAi.GenerativeModel(model: _embeddingModel);

        var response = await model.EmbedContent(text);
        return response.Embedding?.Values?.ToArray() ?? Array.Empty<float>();
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

    private Dictionary<string, Schema>? ConvertParameters(object parameters)
    {
        if (parameters is not Dictionary<string, object> dict)
            return null;

        if (!dict.TryGetValue("properties", out var props) || props is not Dictionary<string, object> properties)
            return null;

        var result = new Dictionary<string, Schema>();

        foreach (var (name, value) in properties)
        {
            if (value is Dictionary<string, object> propDef)
            {
                var schema = new Schema();

                if (propDef.TryGetValue("type", out var typeVal))
                {
                    schema.Type = typeVal?.ToString() switch
                    {
                        "string" => ParameterType.String,
                        "integer" => ParameterType.Integer,
                        "number" => ParameterType.Number,
                        "boolean" => ParameterType.Boolean,
                        "array" => ParameterType.Array,
                        _ => ParameterType.String
                    };
                }

                if (propDef.TryGetValue("description", out var descVal))
                {
                    schema.Description = descVal?.ToString();
                }

                result[name] = schema;
            }
        }

        return result;
    }
}

internal class ContentResponse
{
    public string Role { get; set; } = "";
    public string? Text { get; set; }
}
