// Per-deployment database access: the Reservations identity and the job that
// creates the database. Deployed and run before main.bicep.

import { timeZoneEnv } from 'modules/container-app.bicep'

param location string = resourceGroup().location

@description('Short deployment name, such as live or scratch.')
@minLength(1)
@maxLength(16)
param deploymentName string

@description('Image tag, the commit SHA.')
param imageTag string

@description('Drop and recreate the database.')
param resetData bool = false

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

resource dbAdminIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: 'id-poolcar-dbadmin'
}

resource reservationsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-poolcar-${deploymentName}'
  location: location
}

resource dbSetup 'Microsoft.App/jobs@2025-07-01' = {
  name: 'caj-${deploymentName}-dbsetup'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
      '${dbAdminIdentity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: 600
      replicaRetryLimit: 0
      registries: [
        {
          server: registry.properties.loginServer
          identity: pullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'dbsetup'
          image: '${registry.properties.loginServer}/poolcar-dbsetup:${imageTag}'
          env: [
            timeZoneEnv
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
            { name: 'OTEL_SERVICE_NAME', value: '${deploymentName}-dbsetup' }
            { name: 'DbSetup__Host', value: postgres.properties.fullyQualifiedDomainName }
            { name: 'DbSetup__AdminUser', value: dbAdminIdentity.name }
            { name: 'DbSetup__Database', value: 'poolcar_${deploymentName}' }
            { name: 'DbSetup__Principal', value: reservationsIdentity.name }
            { name: 'DbSetup__PrincipalObjectId', value: reservationsIdentity.properties.principalId }
            { name: 'DbSetup__Reset', value: string(resetData) }
            { name: 'AZURE_CLIENT_ID', value: dbAdminIdentity.properties.clientId }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
}

output jobName string = dbSetup.name
