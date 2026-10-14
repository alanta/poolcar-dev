// Per-deployment container apps. Deploy after database.bicep and its setup job.

param location string = resourceGroup().location

@description('Short deployment name, such as live or scratch.')
@minLength(1)
@maxLength(16)
param deploymentName string

@description('Image tag, the commit SHA.')
param imageTag string

@description('Entra ID app registration client ID for EasyAuth on Web.')
param easyAuthClientId string

@secure()
@description('Client secret of the EasyAuth app registration.')
param easyAuthClientSecret string

param registryName string = 'crpoolcar'
param postgresServerName string = 'psql-poolcar'

resource registry 'Microsoft.ContainerRegistry/registries@2025-11-01' existing = {
  name: registryName
}

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: 'cae-poolcar'
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: 'appi-poolcar'
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' existing = {
  name: postgresServerName
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: 'id-poolcar-pull'
}

resource reservationsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: 'id-poolcar-${deploymentName}'
}

var registryServer = registry.properties.loginServer
var appInsightsEnv = {
  name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
  value: appInsights.properties.ConnectionString
}

module fleet 'modules/container-app.bicep' = {
  params: {
    name: 'ca-${deploymentName}-fleet'
    location: location
    environmentId: environment.id
    registryServer: registryServer
    image: '${registryServer}/poolcar-fleet:${imageTag}'
    pullIdentityId: pullIdentity.id
    env: [
      appInsightsEnv
      { name: 'OTEL_SERVICE_NAME', value: '${deploymentName}-fleet' }
    ]
  }
}

module reservations 'modules/container-app.bicep' = {
  params: {
    name: 'ca-${deploymentName}-reservations'
    location: location
    environmentId: environment.id
    registryServer: registryServer
    image: '${registryServer}/poolcar-reservations:${imageTag}'
    pullIdentityId: pullIdentity.id
    identityIds: [reservationsIdentity.id]
    env: [
      appInsightsEnv
      { name: 'OTEL_SERVICE_NAME', value: '${deploymentName}-reservations' }
      {
        name: 'ConnectionStrings__reservations'
        value: 'Host=${postgres.properties.fullyQualifiedDomainName};Database=poolcar_${deploymentName};Username=${reservationsIdentity.name};Ssl Mode=Require'
      }
      { name: 'AZURE_CLIENT_ID', value: reservationsIdentity.properties.clientId }
      { name: 'Seed__Reservations', value: 'true' }
    ]
  }
}

module web 'modules/container-app.bicep' = {
  params: {
    name: 'ca-${deploymentName}-web'
    location: location
    environmentId: environment.id
    registryServer: registryServer
    image: '${registryServer}/poolcar-web:${imageTag}'
    pullIdentityId: pullIdentity.id
    externalIngress: true
    easyAuthClientId: easyAuthClientId
    easyAuthClientSecret: easyAuthClientSecret
    env: [
      appInsightsEnv
      { name: 'OTEL_SERVICE_NAME', value: '${deploymentName}-web' }
      { name: 'FleetApi__BaseAddress', value: 'https://${fleet.outputs.fqdn}' }
      { name: 'ReservationsApi__BaseAddress', value: 'https://${reservations.outputs.fqdn}' }
    ]
  }
}

output webFqdn string = web.outputs.fqdn
output redirectUri string = 'https://${web.outputs.fqdn}/.auth/login/aad/callback'
