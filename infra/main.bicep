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
    databaseName: 'DevDb'
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
    sku: environmentName == 'prod' ? 'P1v3' : 'F1'
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

output APP_SERVICE_NAME string = appservice.outputs.appServiceName
output APP_SERVICE_URL string = appservice.outputs.appServiceUrl
