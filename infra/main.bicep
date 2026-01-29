@description('The environment name (dev, staging, prod)')
param environmentName string = 'dev'

@description('Location for all resources')
param location string = resourceGroup().location

@description('Base name for all resources')
param baseName string = 'codeagent'

@description('Frontend URL for CORS configuration')
param frontendUrl string = 'http://localhost:5173'

var resourceToken = toLower(uniqueString(subscription().id, resourceGroup().id, environmentName))
var tags = {
  environment: environmentName
  project: 'code-agent'
  'azd-env-name': environmentName
}

// Cosmos DB
module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos-deployment'
  params: {
    accountName: '${baseName}-cosmos-${resourceToken}'
    location: location
    databaseName: 'CodeAgentDb'
    tags: tags
  }
}

// Azure OpenAI
module openai 'modules/openai.bicep' = {
  name: 'openai-deployment'
  params: {
    accountName: '${baseName}-openai-${resourceToken}'
    location: location
    tags: tags
  }
}

// App Service
module appservice 'modules/appservice.bicep' = {
  name: 'appservice-deployment'
  params: {
    appName: '${baseName}-api-${resourceToken}'
    location: location
    tags: tags
    sku: environmentName == 'prod' ? 'P1v3' : 'B1'
    openAiEndpoint: openai.outputs.endpoint
    openAiApiKey: openai.outputs.apiKey
    openAiChatDeployment: openai.outputs.chatDeploymentName
    openAiEmbeddingDeployment: openai.outputs.embeddingDeploymentName
    cosmosDbConnectionString: cosmos.outputs.connectionString
    cosmosDbDatabaseName: cosmos.outputs.databaseName
    frontendUrl: frontendUrl
  }
}

// Outputs for use by the application and CI/CD
output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = resourceGroup().name

output COSMOS_DB_ACCOUNT_NAME string = cosmos.outputs.accountName
output COSMOS_DB_DATABASE_NAME string = cosmos.outputs.databaseName
output COSMOS_DB_ENDPOINT string = cosmos.outputs.endpoint

output AZURE_OPENAI_ACCOUNT_NAME string = openai.outputs.accountName
output AZURE_OPENAI_ENDPOINT string = openai.outputs.endpoint
output AZURE_OPENAI_CHAT_DEPLOYMENT string = openai.outputs.chatDeploymentName
output AZURE_OPENAI_EMBEDDING_DEPLOYMENT string = openai.outputs.embeddingDeploymentName

output APP_SERVICE_NAME string = appservice.outputs.appServiceName
output APP_SERVICE_URL string = appservice.outputs.appServiceUrl
