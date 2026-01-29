@description('The name of the Azure OpenAI resource')
param accountName string

@description('Location for the OpenAI resource')
param location string = resourceGroup().location

@description('Tags to apply to resources')
param tags object = {}

@description('SKU for the OpenAI resource')
param sku string = 'S0'

resource openAiAccount 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' = {
  name: accountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: sku
  }
  properties: {
    customSubDomainName: accountName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

resource gpt4Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-04-01-preview' = {
  parent: openAiAccount
  name: 'gpt-4'
  sku: {
    name: 'Standard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4'
      version: '0613'
    }
    raiPolicyName: 'Microsoft.Default'
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-04-01-preview' = {
  parent: openAiAccount
  name: 'text-embedding-ada-002'
  sku: {
    name: 'Standard'
    capacity: 50
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-ada-002'
      version: '2'
    }
  }
  dependsOn: [gpt4Deployment] // Deploy sequentially to avoid rate limits
}

output accountName string = openAiAccount.name
output endpoint string = openAiAccount.properties.endpoint
output chatDeploymentName string = gpt4Deployment.name
output embeddingDeploymentName string = embeddingDeployment.name
output apiKey string = openAiAccount.listKeys().key1
