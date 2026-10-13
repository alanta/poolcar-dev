// A PoolCar container app, pulling its image with the shared pull identity.

param name string
param location string
param environmentId string
param registryServer string
param image string
param pullIdentityId string

@description('Identities besides the pull identity.')
param identityIds string[] = []

param env object[]
param externalIngress bool = false
param minReplicas int

@description('Entra ID client ID for EasyAuth; leave empty to disable EasyAuth.')
param easyAuthClientId string = ''

@secure()
param easyAuthClientSecret string = ''

// Dutch local time
@export()
var timeZoneEnv = { name: 'TZ', value: 'Europe/Amsterdam' }

var port = 8080
var easyAuthSecretName = 'microsoft-provider-authentication-secret'
var easyAuth = !empty(easyAuthClientId)

resource app 'Microsoft.App/containerApps@2025-07-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: toObject(union([pullIdentityId], identityIds), id => id, id => {})
  }
  properties: {
    environmentId: environmentId
    configuration: {
      ingress: {
        external: externalIngress
        targetPort: port
      }
      registries: [
        {
          server: registryServer
          identity: pullIdentityId
        }
      ]
      secrets: easyAuth ? [{ name: easyAuthSecretName, value: easyAuthClientSecret }] : []
    }
    template: {
      containers: [
        {
          name: 'app'
          image: image
          env: concat([timeZoneEnv], env)
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/alive', port: port }
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: port }
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
      }
    }
  }
}

resource auth 'Microsoft.App/containerApps/authConfigs@2025-07-01' = if (easyAuth) {
  parent: app
  name: 'current'
  properties: {
    platform: {
      enabled: true
    }
    globalValidation: {
      unauthenticatedClientAction: 'AllowAnonymous'
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: easyAuthClientId
          clientSecretSettingName: easyAuthSecretName
          openIdIssuer: '${environment().authentication.loginEndpoint}organizations/v2.0'
        }
        validation: {
          allowedAudiences: [easyAuthClientId, 'api://${easyAuthClientId}']
        }
      }
    }
    login: {
      tokenStore: {
        enabled: false
      }
    }
  }
}

output fqdn string = app.properties.configuration.ingress.fqdn
