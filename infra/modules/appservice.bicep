@description('The name of the App Service')
param appName string

@description('Location for resources')
param location string = resourceGroup().location

@description('Tags to apply to resources')
param tags object = {}

@description('SKU for the App Service Plan')
param sku string = 'B1'

@description('Azure OpenAI endpoint')
param openAiEndpoint string

@description('Azure OpenAI API key')
@secure()
param openAiApiKey string

@description('Azure OpenAI chat deployment name')
param openAiChatDeployment string

@description('Azure OpenAI embedding deployment name')
param openAiEmbeddingDeployment string

@description('Cosmos DB connection string')
@secure()
param cosmosDbConnectionString string

@description('Cosmos DB database name')
param cosmosDbDatabaseName string

@description('Frontend URL for CORS')
param frontendUrl string = 'http://localhost:5173'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${appName}-plan'
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    reserved: true // Linux
  }
}

resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  tags: tags
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: sku != 'F1' && sku != 'D1'
      http20Enabled: true
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureOpenAI__Endpoint'
          value: openAiEndpoint
        }
        {
          name: 'AzureOpenAI__ApiKey'
          value: openAiApiKey
        }
        {
          name: 'AzureOpenAI__ChatDeployment'
          value: openAiChatDeployment
        }
        {
          name: 'AzureOpenAI__EmbeddingDeployment'
          value: openAiEmbeddingDeployment
        }
        {
          name: 'CosmosDb__ConnectionString'
          value: cosmosDbConnectionString
        }
        {
          name: 'CosmosDb__DatabaseName'
          value: cosmosDbDatabaseName
        }
        {
          name: 'Frontend__Url'
          value: frontendUrl
        }
      ]
      cors: {
        allowedOrigins: [
          frontendUrl
          'http://localhost:5173'
        ]
        supportCredentials: true
      }
    }
  }
}

resource appServiceSlotStaging 'Microsoft.Web/sites/slots@2023-12-01' = {
  parent: appService
  name: 'staging'
  location: location
  tags: tags
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      autoSwapSlotName: 'production'
    }
  }
}

output appServiceName string = appService.name
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output appServicePlanName string = appServicePlan.name
