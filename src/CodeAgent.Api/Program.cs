using Microsoft.Azure.Cosmos;
using CodeAgent.Api.Infrastructure;
using CodeAgent.Api.Services.Agent;
using CodeAgent.Api.Services.Agent.Tools;
using CodeAgent.Api.Services.Grounding;
using CodeAgent.Api.Services.Rag;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Code Agent API",
        Version = "v1",
        Description = "An agentic AI assistant for code repositories"
    });
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                builder.Configuration["Frontend:Url"] ?? "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Register Azure Cosmos DB
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration["CosmosDb:ConnectionString"]
        ?? throw new InvalidOperationException("CosmosDb:ConnectionString is not configured");

    var clientOptions = new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    };

    return new CosmosClient(connectionString, clientOptions);
});

// Register infrastructure services
builder.Services.AddSingleton<ICosmosDbContext, CosmosDbContext>();
builder.Services.AddSingleton<ILlmClient, GeminiClient>();

// Register RAG services
builder.Services.AddScoped<IDocumentChunker, DocumentChunker>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IVectorSearchService, VectorSearchService>();

// Register grounding services
builder.Services.AddScoped<ICitationService, CitationService>();

// Register agent tools
builder.Services.AddScoped<IAgentTool, CodeSearchTool>();
builder.Services.AddScoped<IAgentTool, FileReadTool>();
builder.Services.AddScoped<IAgentTool, ExplainCodeTool>();
builder.Services.AddScoped<IAgentTool, FindReferencesTool>();

// Register agent orchestrator
builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// Initialize Cosmos DB containers
await InitializeCosmosDbAsync(app);

app.Run();

async Task InitializeCosmosDbAsync(WebApplication app)
{
    try
    {
        var cosmosClient = app.Services.GetRequiredService<CosmosClient>();
        var configuration = app.Services.GetRequiredService<IConfiguration>();
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "CodeAgentDb";

        // Try to get or create database (without specifying throughput for free tier)
        var databaseResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseName);
        var database = databaseResponse.Database;

        // Try to create containers - these may fail on free tier if throughput limit reached
        // The app will still work with existing containers
        try
        {
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties("chunks", "/repositoryId"));
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties("repositories", "/id"));
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties("conversations", "/id"));
        }
        catch (CosmosException ce) when (ce.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Free tier throughput limit - containers may already exist or need manual creation
            app.Logger.LogInformation("Containers may need to be created manually in Azure Portal for free tier");
        }

        app.Logger.LogInformation("Cosmos DB connection verified");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to initialize Cosmos DB. Will retry on first request.");
    }
}
