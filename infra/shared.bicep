// Resources shared by all PoolCar deployments in the resource group.

param location string = resourceGroup().location

@description('Globally unique name of the container registry.')
param registryName string = 'crpoolcar'

@description('Globally unique name of the PostgreSQL flexible server.')
param postgresServerName string = 'psql-poolcar'

var acrPullRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

resource logs 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: 'log-poolcar'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-poolcar'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: 'cae-poolcar'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-poolcar-pull'
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: acrPullRoleId
    principalId: pullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dbAdminIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-poolcar-dbadmin'
  location: location
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' = {
  name: postgresServerName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '17'
    storage: {
      storageSizeGB: 32
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: tenant().tenantId
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// The server accepts one change at a time, so the child resources are chained.
resource extensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2025-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: {
    value: 'BTREE_GIST'
    source: 'user-override'
  }
}

resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2025-08-01' = {
  parent: postgres
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
  dependsOn: [extensions]
}

module entraAdmin 'modules/postgres-admin.bicep' = {
  params: {
    serverName: postgres.name
    principalId: dbAdminIdentity.properties.principalId
    principalName: dbAdminIdentity.name
  }
  dependsOn: [allowAzureServices]
}

output registryLoginServer string = registry.properties.loginServer
output environmentId string = environment.id
output postgresFqdn string = postgres.properties.fullyQualifiedDomainName
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output pullIdentityId string = pullIdentity.id
output pullIdentityClientId string = pullIdentity.properties.clientId
output pullIdentityPrincipalId string = pullIdentity.properties.principalId
output dbAdminIdentityId string = dbAdminIdentity.id
output dbAdminIdentityClientId string = dbAdminIdentity.properties.clientId
output dbAdminIdentityPrincipalId string = dbAdminIdentity.properties.principalId
