#!/bin/bash
set -e

# Configuration
RESOURCE_GROUP="${RESOURCE_GROUP:-codeagent-rg}"
LOCATION="${LOCATION:-canadacentral}"
ENVIRONMENT="${ENVIRONMENT:-dev}"
BASE_NAME="${BASE_NAME:-codeagent}"
FRONTEND_URL="${FRONTEND_URL:-http://localhost:5173}"

echo "=========================================="
echo "Code Agent Infrastructure Deployment"
echo "=========================================="
echo "Resource Group: $RESOURCE_GROUP"
echo "Location: $LOCATION"
echo "Environment: $ENVIRONMENT"
echo "=========================================="

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo "Error: Azure CLI is not installed. Please install it first."
    exit 1
fi

# Check if logged in
if ! az account show &> /dev/null; then
    echo "Please log in to Azure..."
    az login
fi

# Create resource group if it doesn't exist
echo "Creating resource group..."
az group create \
    --name "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --output none

# Deploy infrastructure
echo "Deploying infrastructure..."
az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file main.bicep \
    --parameters \
        environmentName="$ENVIRONMENT" \
        location="$LOCATION" \
        baseName="$BASE_NAME" \
        frontendUrl="$FRONTEND_URL" \
    --output none

# Extract outputs via az CLI query (no jq dependency)
APP_SERVICE_URL=$(az deployment group show --resource-group "$RESOURCE_GROUP" --name main --query 'properties.outputs.APP_SERVICE_URL.value' -o tsv)
COSMOS_DB_ENDPOINT=$(az deployment group show --resource-group "$RESOURCE_GROUP" --name main --query 'properties.outputs.COSMOS_DB_ENDPOINT.value' -o tsv)
COSMOS_DB_ACCOUNT_NAME=$(az deployment group show --resource-group "$RESOURCE_GROUP" --name main --query 'properties.outputs.COSMOS_DB_ACCOUNT_NAME.value' -o tsv)
APP_SERVICE_NAME=$(az deployment group show --resource-group "$RESOURCE_GROUP" --name main --query 'properties.outputs.APP_SERVICE_NAME.value' -o tsv)

echo ""
echo "=========================================="
echo "Deployment Complete!"
echo "=========================================="
echo "API URL: $APP_SERVICE_URL"
echo "Cosmos DB: $COSMOS_DB_ENDPOINT"
echo ""
echo "To deploy the API code, run:"
echo "  cd src/CodeAgent.Api && dotnet publish -c Release"
echo "  az webapp deploy --resource-group $RESOURCE_GROUP --name $APP_SERVICE_NAME --src-path bin/Release/net8.0/publish"
echo ""
echo "To update the frontend API URL:"
echo "  export VITE_API_URL=$APP_SERVICE_URL"
echo "=========================================="

# Save outputs to file for CI/CD
cat > .env.azure << EOF
AZURE_RESOURCE_GROUP=$RESOURCE_GROUP
APP_SERVICE_URL=$APP_SERVICE_URL
APP_SERVICE_NAME=$APP_SERVICE_NAME
COSMOS_DB_ENDPOINT=$COSMOS_DB_ENDPOINT
COSMOS_DB_ACCOUNT_NAME=$COSMOS_DB_ACCOUNT_NAME
EOF

echo "Environment variables saved to .env.azure"
