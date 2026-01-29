using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Models;
using CodeAgent.Api.Services.Agent;

namespace CodeAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ICosmosDbContext _cosmosDb;
    private readonly ILogger<AgentController> _logger;

    public AgentController(
        IAgentOrchestrator orchestrator,
        ICosmosDbContext cosmosDb,
        ILogger<AgentController> logger)
    {
        _orchestrator = orchestrator;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    /// <summary>
    /// Send a message to the code agent and get a response
    /// </summary>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message is required" });
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryId))
        {
            return BadRequest(new { error = "RepositoryId is required" });
        }

        _logger.LogInformation("Chat request for repository: {RepositoryId}", request.RepositoryId);

        // Get or create conversation context
        ConversationContext? context = null;
        if (!string.IsNullOrEmpty(request.ConversationId))
        {
            context = await _cosmosDb.GetConversationAsync(request.ConversationId);
        }

        if (context == null)
        {
            context = new ConversationContext
            {
                RepositoryId = request.RepositoryId
            };
        }

        // Add user message to context
        context.Messages.Add(new ChatMessage
        {
            Role = "user",
            Content = request.Message
        });

        // Process with agent
        var response = await _orchestrator.ProcessAsync(request.Message, request.RepositoryId, context);

        // Add assistant response to context
        context.Messages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = response.Message
        });

        // Save conversation
        await _cosmosDb.UpsertConversationAsync(context);
        response.ConversationId = context.Id;

        return Ok(response);
    }

    /// <summary>
    /// Send a message and receive a streaming response
    /// </summary>
    [HttpPost("chat/stream")]
    public async Task ChatStream([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || string.IsNullOrWhiteSpace(request.RepositoryId))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Message and RepositoryId are required");
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        _logger.LogInformation("Streaming chat request for repository: {RepositoryId}", request.RepositoryId);

        ConversationContext? context = null;
        if (!string.IsNullOrEmpty(request.ConversationId))
        {
            context = await _cosmosDb.GetConversationAsync(request.ConversationId);
        }

        if (context == null)
        {
            context = new ConversationContext { RepositoryId = request.RepositoryId };
        }

        context.Messages.Add(new ChatMessage { Role = "user", Content = request.Message });

        var fullResponse = new System.Text.StringBuilder();

        await foreach (var chunk in _orchestrator.ProcessStreamingAsync(
            request.Message, request.RepositoryId, context, HttpContext.RequestAborted))
        {
            var eventData = JsonSerializer.Serialize(chunk);
            await Response.WriteAsync($"data: {eventData}\n\n");
            await Response.Body.FlushAsync();

            if (chunk.Type == "answer")
            {
                fullResponse.Append(chunk.Content);
            }
        }

        // Save conversation
        context.Messages.Add(new ChatMessage { Role = "assistant", Content = fullResponse.ToString() });
        await _cosmosDb.UpsertConversationAsync(context);

        // Send done event
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "done", conversationId = context.Id })}\n\n");
        await Response.Body.FlushAsync();
    }

    /// <summary>
    /// Get conversation history
    /// </summary>
    [HttpGet("conversations/{conversationId}")]
    [ProducesResponseType(typeof(ConversationContext), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConversation(string conversationId)
    {
        var conversation = await _cosmosDb.GetConversationAsync(conversationId);
        if (conversation == null)
        {
            return NotFound(new { error = "Conversation not found" });
        }

        return Ok(conversation);
    }

    /// <summary>
    /// Clear conversation history
    /// </summary>
    [HttpDelete("conversations/{conversationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteConversation(string conversationId)
    {
        var conversation = await _cosmosDb.GetConversationAsync(conversationId);
        if (conversation != null)
        {
            conversation.Messages.Clear();
            await _cosmosDb.UpsertConversationAsync(conversation);
        }

        return NoContent();
    }
}
