targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource conventions')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@secure()
@description('JWT signing key — set via: azd env set JWT_SIGNING_KEY "value"')
param jwtSigningKey string = ''

@secure()
@description('Database connection string — set via: azd env set DB_CONNECTION_STRING "value"')
param dbConnectionString string = ''

// Reuse the existing resource group - no new group created
resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' existing = {
  name: 'thinkschool-rg'
}

module apiService './app/api.bicep' = {
  name: 'quotes-api-setup'
  scope: rg
  params: {
    location: location
    environmentName: environmentName
    jwtSigningKey: jwtSigningKey
    dbConnectionString: dbConnectionString
  }
}

// azd uses AZURE_CONTAINER_REGISTRY_ENDPOINT to know where to push the built image
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = 'bhagyathinkschool2026.azurecr.io'
// Tell azd which resource group to search for the 'api' service Container App
output AZURE_RESOURCE_GROUP string = 'thinkschool-rg'
output SERVICE_API_RESOURCE_GROUP string = 'thinkschool-rg'
// azd displays this as the live URL after successful deployment
output SERVICE_API_URI string = apiService.outputs.SERVICE_API_URI
output SERVICE_API_RESOURCE_NAME string = apiService.outputs.SERVICE_API_RESOURCE_NAME
