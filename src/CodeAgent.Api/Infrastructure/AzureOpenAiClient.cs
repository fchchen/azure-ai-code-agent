using System.ClientModel;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace CodeAgent.Api.Infrastructure;

public interface IAzureOpenAiClient
{
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages, List<ChatTool>? tools = null);
    Task<ChatCompletion> GetChatCompletionWithToolsAsync(List<ChatMessage> messages, List<ChatTool> tools);
    Task<float[]> GetEmbeddingAsync(string text);
    Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts);
    IAsyncEnumerable<StreamingChatCompletionUpdate> GetStreamingChatCompletionAsync(List<ChatMessage> messages, List<ChatTool>? tools = null);
}

public class AzureOpenAiClientWrapper : IAzureOpenAiClient
{
    private readonly AzureOpenAIClient _client;
    private readonly string _chatDeployment;
    private readonly string _embeddingDeployment;
    private readonly ILogger<AzureOpenAiClientWrapper> _logger;

    public AzureOpenAiClientWrapper(IConfiguration configuration, ILogger<AzureOpenAiClientWrapper> logger)
    {
        _logger = logger;

        var endpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured");
        var apiKey = configuration["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is not configured");

        _chatDeployment = configuration["AzureOpenAI:ChatDeployment"] ?? "gpt-4";
        _embeddingDeployment = configuration["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-ada-002";

        _client = new AzureOpenAIClient(new Uri(endpoint), new System.ClientModel.ApiKeyCredential(apiKey));
    }

    public async Task<string> GetChatCompletionAsync(
        List<ChatMessage> messages,
        List<ChatTool>? tools = null)
    {
        var chatClient = _client.GetChatClient(_chatDeployment);

        var options = new ChatCompletionOptions
        {
            Temperature = 0.7f,
            MaxOutputTokenCount = 4096
        };

        if (tools != null)
        {
            foreach (var tool in tools)
            {
                options.Tools.Add(tool);
            }
        }

        var response = await chatClient.CompleteChatAsync(messages, options);
        return response.Value.Content[0].Text ?? string.Empty;
    }

    public async Task<ChatCompletion> GetChatCompletionWithToolsAsync(
        List<ChatMessage> messages,
        List<ChatTool> tools)
    {
        var chatClient = _client.GetChatClient(_chatDeployment);

        var options = new ChatCompletionOptions
        {
            Temperature = 0.7f,
            MaxOutputTokenCount = 4096
        };

        foreach (var tool in tools)
        {
            options.Tools.Add(tool);
        }

        var response = await chatClient.CompleteChatAsync(messages, options);
        return response.Value;
    }

    public async IAsyncEnumerable<StreamingChatCompletionUpdate> GetStreamingChatCompletionAsync(
        List<ChatMessage> messages,
        List<ChatTool>? tools = null)
    {
        var chatClient = _client.GetChatClient(_chatDeployment);

        var options = new ChatCompletionOptions
        {
            Temperature = 0.7f,
            MaxOutputTokenCount = 4096
        };

        if (tools != null)
        {
            foreach (var tool in tools)
            {
                options.Tools.Add(tool);
            }
        }

        var updates = chatClient.CompleteChatStreamingAsync(messages, options);

        await foreach (var update in updates)
        {
            yield return update;
        }
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var embeddingClient = _client.GetEmbeddingClient(_embeddingDeployment);
        var response = await embeddingClient.GenerateEmbeddingAsync(text);
        return response.Value.ToFloats().ToArray();
    }

    public async Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts)
    {
        var textList = texts.ToList();
        var results = new List<float[]>();

        var embeddingClient = _client.GetEmbeddingClient(_embeddingDeployment);

        // Process in batches of 16 (Azure OpenAI limit)
        const int batchSize = 16;
        for (int i = 0; i < textList.Count; i += batchSize)
        {
            var batch = textList.Skip(i).Take(batchSize).ToList();
            var response = await embeddingClient.GenerateEmbeddingsAsync(batch);

            foreach (var embedding in response.Value)
            {
                results.Add(embedding.ToFloats().ToArray());
            }
        }

        return results;
    }
}
