@description('Azure region for the Container App')
param location string

@description('Name of the azd environment')
param environmentName string

@secure()
@description('JWT signing key for token generation')
param jwtSigningKey string

@secure()
@description('Database connection string')
param dbConnectionString string

// Reference existing Container Apps Environment
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: 'thinkschool-env'
}

// Reference existing Container Registry
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: 'bhagyathinkschool2026'
}

// Create/update the Container App.
// Container is named 'api' (matching the azd service name) and starts with a neutral MCR
// placeholder image so azd generates a clean ACR repository path (no nested path).
resource quotesApi 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'quotes-api'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  tags: {
    'azd-service-name': 'api'
    'azd-env-name': environmentName
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: 'system'
        }
      ]
      secrets: [
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
        {
          name: 'db-connection'
          value: dbConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: 'bhagyathinkschool2026.azurecr.io/quotes-api/api-quotes-api-prod:bootstrap'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'Jwt__SigningKey'
              secretRef: 'jwt-signing-key'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'db-connection'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

// AcrPull role was created manually and already exists on the registry.
// Bicep does not manage it to avoid RoleAssignmentExists conflicts.

output SERVICE_API_URI string = 'https://${quotesApi.properties.configuration.ingress.fqdn}'
output SERVICE_API_RESOURCE_NAME string = quotesApi.name
