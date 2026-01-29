# AI Code Repository Assistant

A full-stack agentic AI application for understanding and navigating codebases, built with .NET 8 and React/TypeScript.

## Features

- **Agentic AI Patterns**: Multi-tool orchestration with ReAct-style reasoning
- **RAG Pipeline**: Semantic code search using Azure Cosmos DB vector search
- **Grounding**: Source citations with file:line references
- **Azure Cloud-Native**: Designed for Azure deployment with Bicep IaC

## Architecture

```
┌─────────────────┐     ┌─────────────────────────────────────────┐
│  React Frontend │────▶│           .NET 8 Web API                │
│  (TypeScript)   │     │  ┌─────────────────────────────────┐   │
└─────────────────┘     │  │      Agent Orchestrator         │   │
                        │  │  (ReAct Loop with Tool Calls)   │   │
                        │  └──────────────┬──────────────────┘   │
                        │                 │                       │
                        │  ┌──────────────┴──────────────────┐   │
                        │  │            Tools                 │   │
                        │  │ • Code Search  • File Read       │   │
                        │  │ • Explain Code • Find References │   │
                        │  └──────────────┬──────────────────┘   │
                        │                 │                       │
                        │  ┌──────────────┴──────────────────┐   │
                        │  │         RAG Pipeline             │   │
                        │  │ • Document Chunker               │   │
                        │  │ • Embedding Service              │   │
                        │  │ • Vector Search Service          │   │
                        │  └─────────────────────────────────┘   │
                        └───────────────────┬─────────────────────┘
                                            │
                        ┌───────────────────┴───────────────────┐
                        │                                       │
              ┌─────────┴─────────┐               ┌─────────────┴─────────┐
              │  Azure Cosmos DB  │               │   Azure OpenAI        │
              │  (Vector Search)  │               │  (GPT-4 + Embeddings) │
              └───────────────────┘               └───────────────────────┘
```

## Tech Stack

### Backend
- .NET 8 Web API
- Azure.AI.OpenAI SDK
- Microsoft.Azure.Cosmos SDK
- Microsoft Semantic Kernel (optional)

### Frontend
- React 18 + TypeScript
- Vite
- TailwindCSS
- React Query

### Infrastructure
- Azure Bicep
- Docker Compose (local development)

## Getting Started

### Prerequisites

- .NET 8 SDK
- Node.js 20+
- Docker (for local Cosmos DB emulator)
- Azure CLI (for deployment)
- Azure subscription with OpenAI access

### Local Development

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd azure-ai-code-agent
   ```

2. **Configure environment**

   Update `src/CodeAgent.Api/appsettings.json` with your Azure OpenAI credentials:
   ```json
   {
     "AzureOpenAI": {
       "Endpoint": "https://your-resource.openai.azure.com/",
       "ApiKey": "your-api-key",
       "ChatDeployment": "gpt-4",
       "EmbeddingDeployment": "text-embedding-ada-002"
     }
   }
   ```

3. **Start local services with Docker Compose**
   ```bash
   docker-compose up -d cosmosdb
   ```

4. **Run the API**
   ```bash
   cd src/CodeAgent.Api
   dotnet run
   ```

5. **Run the frontend**
   ```bash
   cd src/code-agent-ui
   npm install
   npm run dev
   ```

6. **Open the application**
   - Frontend: http://localhost:5173
   - API Swagger: http://localhost:5000/swagger

### Indexing a Repository

Use the API to index a code repository:

```bash
curl -X POST http://localhost:5000/api/ingestion/repositories \
  -H "Content-Type: application/json" \
  -d '{
    "path": "/path/to/your/repository",
    "name": "My Project",
    "description": "Description of the project"
  }'
```

## Azure Deployment

1. **Login to Azure**
   ```bash
   az login
   ```

2. **Deploy infrastructure**
   ```bash
   cd infra
   ./deploy.sh
   ```

3. **Deploy the API**
   ```bash
   cd src/CodeAgent.Api
   dotnet publish -c Release
   az webapp deploy --resource-group codeagent-rg \
     --name <app-service-name> \
     --src-path bin/Release/net8.0/publish
   ```

## Project Structure

```
azure-ai-code-agent/
├── src/
│   ├── CodeAgent.Api/              # .NET 8 Web API
│   │   ├── Controllers/            # API endpoints
│   │   ├── Services/
│   │   │   ├── Agent/              # Agentic AI components
│   │   │   │   ├── Tools/          # Agent tools
│   │   │   │   └── AgentOrchestrator.cs
│   │   │   ├── Rag/                # RAG pipeline
│   │   │   └── Grounding/          # Citation service
│   │   ├── Models/                 # Data models
│   │   └── Infrastructure/         # Azure clients
│   │
│   └── code-agent-ui/              # React/TypeScript Frontend
│       └── src/
│           ├── components/         # UI components
│           ├── hooks/              # Custom hooks
│           └── services/           # API client
│
├── infra/                          # Azure Bicep templates
│   ├── main.bicep
│   └── modules/
│
├── tests/                          # Test projects
├── docker-compose.yml
└── README.md
```

## API Endpoints

### Agent
- `POST /api/agent/chat` - Send a message and get a response
- `POST /api/agent/chat/stream` - Send a message with streaming response
- `GET /api/agent/conversations/{id}` - Get conversation history
- `DELETE /api/agent/conversations/{id}` - Clear conversation

### Ingestion
- `GET /api/ingestion/repositories` - List indexed repositories
- `POST /api/ingestion/repositories` - Index a new repository
- `GET /api/ingestion/repositories/{id}` - Get repository details
- `DELETE /api/ingestion/repositories/{id}` - Delete repository index

## Running Tests

```bash
cd tests/CodeAgent.Api.Tests
dotnet test
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `AzureOpenAI__Endpoint` | Azure OpenAI endpoint URL |
| `AzureOpenAI__ApiKey` | Azure OpenAI API key |
| `AzureOpenAI__ChatDeployment` | GPT-4 deployment name |
| `AzureOpenAI__EmbeddingDeployment` | Embedding model deployment name |
| `CosmosDb__ConnectionString` | Cosmos DB connection string |
| `CosmosDb__DatabaseName` | Database name (default: CodeAgentDb) |
| `Frontend__Url` | Frontend URL for CORS |

## License

MIT
