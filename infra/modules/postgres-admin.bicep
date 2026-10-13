// Separate module because the administrator name must be the principal ID,
// which is only known after the identity has been created.

param serverName string
param principalId string
param principalName string

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' existing = {
  name: serverName
}

resource admin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2025-08-01' = {
  parent: server
  name: principalId
  properties: {
    principalName: principalName
    principalType: 'ServicePrincipal'
    tenantId: tenant().tenantId
  }
}
