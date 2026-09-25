// =============================================================================
// Aveline — Azure spine (curated)
//
// SCOPE: resource group. Create the group first:
//   az group create -n rg-aveline -l malaysiawest
//
// THIS TEMPLATE IS NOT YET DEPLOY-VALIDATED. It compiles (`az bicep build`) and
// is derived from a live ARM capture of `rg-aveline`, but it has not been applied
// to an empty resource group. Validate with:
//   az deployment group what-if -g <rg> -f main.bicep -p main.bicepparam
// before trusting it. See README.md for exactly what is and is not proven.
//
// API versions are not guesses: each is the version the live resources report in
// `deploy/live-arm.json`.
// =============================================================================

targetScope = 'resourceGroup'

@description('Region. Must be one permitted by the subscription region policy; malaysiawest is the only one that hosts the whole stack.')
param location string = resourceGroup().location

// --- naming ------------------------------------------------------------------
param logAnalyticsName string = 'log-aveline'
param registryName string = 'avelineacr'
param keyVaultName string = 'kv-aveline-mw'
param postgresName string = 'pg-aveline'
param postgresDatabaseName string = 'aveline'
param redisName string = 'redis-aveline'
param environmentName string = 'cae-aveline'
param apiAppName string = 'aveline-api'
param agentAppName string = 'aveline-agent'

// --- data tier ---------------------------------------------------------------
@secure()
@description('PostgreSQL administrator password. Never pass this on the command line; use a parameter file or a Key Vault reference.')
param postgresAdminPassword string

param postgresAdminLogin string = 'avelineadmin'

@description('Container images, tagged by git SHA by the deploy workflow.')
param apiImage string
param agentImage string

// --- non-secret application configuration ------------------------------------
param clerkAuthority string = 'https://clerk.aveline.gravora.dev'
param spaOrigins array = [
  'https://aveline.gravora.dev'
  'https://app.aveline.gravora.dev'
]
param firebaseProjectId string = 'aveline-e35a5'
param embeddingsBaseUrl string = 'https://generativelanguage.googleapis.com'
param embeddingsModel string = 'gemini-embedding-2'
param visionBaseUrl string = 'https://api.deepseek.com'
param visionModel string = 'deepseek-v4-flash'
param llmBaseUrl string = 'https://api.deepseek.com'
param llmModel string = 'deepseek-v4-flash'

// --- secrets -----------------------------------------------------------------
// Supplied at deploy time and written to Key Vault. An object (not an array)
// because `@secure()` is only valid on `object` and `string` in Bicep. Names must
// be <= 20 characters, which is the Container Apps secret-name limit.
@secure()
@description('Object of { secretName: value }. Written to Key Vault and referenced by the apps. Names must be <= 20 characters (the Container Apps secret-name limit).')
param secrets object

@secure()
@description('The Firebase service-account JSON as a string. Stored in the vault under its full name; the apps reference it through the shorter alias `firebase-json` because Container Apps secret names are capped at 20 characters.')
param firebaseServiceAccountJson string

// Key Vault references for the container apps. A variable, not an inline
// for-expression: Bicep only allows for-expressions in resource/module/variable/
// output declarations, not nested inside another expression. The URI is built
// from the vault NAME rather than `keyVault.properties.vaultUri`, because a
// variable may only use values resolvable at the start of the deployment.
var keyVaultSecretUriBase = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/'
var kvSecretRefs = [
  for item in items(secrets): {
    name: item.key
    keyVaultUrl: '${keyVaultSecretUriBase}${item.key}'
    identity: 'system'
  }
]

// =============================================================================
// 1. Logging
// =============================================================================
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// =============================================================================
// 2. Container registry
//    adminUserEnabled is FALSE: images are pulled with an Entra ID identity via
//    the AcrPull role assignment below, so there is no registry password to leak
//    or rotate.
// =============================================================================
resource registry 'Microsoft.ContainerRegistry/registries@2026-03-01-preview' = {
  name: registryName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

// =============================================================================
// 3. Key Vault
//    RBAC authorization, not access policies: a subscription Owner still gets
//    ForbiddenByRbac on a data-plane read until Key Vault Secrets Officer is
//    assigned. purgeProtection is deliberately OFF so the beta can be torn down.
// =============================================================================
resource keyVault 'Microsoft.KeyVault/vaults@2026-03-01-preview' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: false
    publicNetworkAccess: 'Enabled'
  }
}

resource keyVaultSecrets 'Microsoft.KeyVault/vaults/secrets@2026-03-01-preview' = [
  for item in items(secrets): {
    name: item.key
    parent: keyVault
    properties: {
      value: item.value
    }
  }
]

// The Firebase service account is its own secret rather than a member of the
// `secrets` object, because its Key Vault name (23 characters) is longer than the
// Container Apps secret-name limit. The vault holds `firebase-service-account`;
// the apps reference it as `firebase-json`.
resource keyVaultFirebaseSecret 'Microsoft.KeyVault/vaults/secrets@2026-03-01-preview' = {
  name: 'firebase-service-account'
  parent: keyVault
  properties: {
    value: firebaseServiceAccountJson
  }
}

// =============================================================================
// 4. PostgreSQL Flexible Server
//    `azure.extensions` MUST be allow-listed server-side or `CREATE EXTENSION`
//    fails. The EF migrations run `CREATE EXTENSION IF NOT EXISTS vector` and
//    `... btree_gist`, and both are needed for embeddings and the GiST exclusion
//    constraint.
// =============================================================================
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2026-04-01-preview' = {
  name: postgresName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
      tenantId: tenant().tenantId
    }
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// Allow-listing pgvector and btree_gist. Note the values are lowercase.
resource postgresExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2026-04-01-preview' = {
  name: 'azure.extensions'
  parent: postgres
  properties: {
    value: 'vector,btree_gist'
    source: 'user-override'
  }
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2026-04-01-preview' = {
  name: postgresDatabaseName
  parent: postgres
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// Azure-services firewall rule. The Container Apps environment has no VNet (a
// VNet cannot be added to an environment after creation), so the app tier
// reaches PostgreSQL over its public endpoint restricted to Azure traffic.
resource postgresAzureServicesFirewall 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2026-04-01-preview' = {
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  parent: postgres
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// =============================================================================
// 5. Azure Managed Redis
//
//    Two deliberate choices, both deviations from the platform defaults:
//
//    evictionPolicy = 'NoEviction' — the Azure default is VolatileLRU, which
//    evicts keys that carry a TTL. The distributed job lock and the idempotency
//    mutex are TTL-bearing, so the default makes them evictable: a lost job lock
//    means duplicate job runs and a lost idempotency key means a duplicate charge.
//    That is a money-path correctness risk, so nothing is evicted here.
//
//    clusteringPolicy = 'NoCluster' — the application is written for a single
//    node. Clustered pub/sub is compatible, but NoCluster removes the whole class
//    of MOVED-redirect behaviour from the beta.
//
//    Classic `Microsoft.Cache/redis` is not an option: it is unavailable in every
//    region this subscription permits.
// =============================================================================
resource redis 'Microsoft.Cache/redisEnterprise@2026-05-01-preview' = {
  name: redisName
  location: location
  sku: { name: 'Balanced_B0' }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2026-05-01-preview' = {
  name: 'default'
  parent: redis
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'NoCluster'
    evictionPolicy: 'NoEviction'
    accessKeysAuthentication: 'Enabled'
  }
}

// =============================================================================
// 6. Container Apps environment
//    environmentMode 'ConsumptionOnly' is explicit. The CLI default in this
//    region produced an **Express** environment, which does not support managed
//    identity for registry authentication; the apps silently fell back to
//    mcr.microsoft.com/k8se/quickstart:latest and reported Healthy while running
//    no application code at all.
// =============================================================================
resource containerEnv 'Microsoft.App/managedEnvironments@2026-01-01' = {
  name: environmentName
  location: location
  properties: {
    // BCP037 below is a Bicep type-definition gap, not a real error: the
    // MAINSTREAM API accepts environmentMode and Bicep emits it into the compiled
    // ARM (verified). It is kept deliberately -- the CLI default in this region
    // creates an **Express** environment, which does not support managed identity
    // for registry auth, and the apps then run the quickstart placeholder image
    // while still reporting Healthy.
    #disable-next-line BCP037
    environmentMode: 'ConsumptionOnly'
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// =============================================================================
// 7. Container Apps
//
//    minReplicas = 1 on BOTH, deliberately. The API is not a stateless request
//    handler: it registers 26 in-process PeriodicTimer jobs (5 minutes to 1 day)
//    and holds the Redis PSUBSCRIBE subscriptions for the ADR-014 event bus.
//    Scaled to zero, the timers do not tick and agent->API events published while
//    it sleeps are lost. Staying warm costs cents for the beta window.
// =============================================================================
var apiFqdnSuffix = containerEnv.properties.defaultDomain
var agentInternalFqdn = '${agentAppName}.internal.${apiFqdnSuffix}'

resource apiApp 'Microsoft.App/containerApps@2026-01-01' = {
  name: apiAppName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: '${registryName}.azurecr.io'
          identity: 'system'
        }
      ]
      secrets: concat(
        kvSecretRefs,
        [ { name: 'firebase-json', keyVaultUrl: '${keyVaultSecretUriBase}firebase-service-account', identity: 'system' } ]
      )
    }
    template: {
      containers: [
        {
          name: apiAppName
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1.0Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ConnectionStrings__DefaultConnection', secretRef: 'postgres-connection-string' }
            { name: 'Redis__ConnectionString', secretRef: 'redis-connection-string' }
            { name: 'Metrics__ScrapeToken', secretRef: 'metrics-scrape-token' }
            { name: 'Telemetry__IpHashSalt', secretRef: 'telemetry-ip-hash-salt' }
            // Media: Production REFUSES the database provider unless the escape
            // hatch is set, so cloudinary is required for a clean boot.
            { name: 'Media__Provider', value: 'cloudinary' }
            { name: 'Media__ReadFromCloudinary', value: 'true' }
            { name: 'Media__DualWrite', value: 'false' }
            { name: 'Media__SigningKey', secretRef: 'media-signing-key' }
            { name: 'Media__PublicBaseUrl', value: 'https://${apiAppName}.${apiFqdnSuffix}' }
            { name: 'Privacy__LinkSigningKey', secretRef: 'privacy-link-signing-key' }
            { name: 'Clerk__Authority', value: clerkAuthority }
            { name: 'Clerk__SecretKey', secretRef: 'clerk-secret-key' }
            { name: 'Credentials__EncryptionKey', secretRef: 'credentials-encryption-key' }
            { name: 'CLOUDINARY_URL', secretRef: 'cloudinary-url' }
            { name: 'AgentService__BaseUrl', value: 'https://${agentInternalFqdn}' }
            { name: 'AgentService__InternalToken', secretRef: 'agent-internal-token' }
            { name: 'Embeddings__ApiKey', secretRef: 'embeddings-api-key' }
            { name: 'Embeddings__BaseUrl', value: embeddingsBaseUrl }
            { name: 'Embeddings__Model', value: embeddingsModel }
            { name: 'Vision__ApiKey', secretRef: 'vision-api-key' }
            { name: 'Vision__BaseUrl', value: visionBaseUrl }
            { name: 'Vision__Model', value: visionModel }
            { name: 'Firebase__ProjectId', value: firebaseProjectId }
            // Firebase is lazy: a missing file only fails on the first push, not
            // at boot, so this is easy to leave silently broken.
            { name: 'Firebase__CredentialsPath', value: '/mnt/secrets/firebase-json' }
            { name: 'Payments__Provider', value: 'manual' }
            // An empty CORS allow-list THROWS at startup (CorsConfiguration).
            { name: 'Cors__AllowedOrigins__0', value: spaOrigins[0] }
            { name: 'Cors__AllowedOrigins__1', value: spaOrigins[1] }
            { name: 'Eventing__SubscribeEventTypes__0', value: 'message.created' }
            { name: 'Eventing__SubscribeEventTypes__1', value: 'message.updated' }
            { name: 'Eventing__SubscribeEventTypes__2', value: 'conversation.created' }
            { name: 'Eventing__SubscribeEventTypes__3', value: 'agent.status' }
          ]
          volumeMounts: [
            { volumeName: 'secrets', mountPath: '/mnt/secrets' }
          ]
        }
      ]
      volumes: [
        { name: 'secrets', storageType: 'Secret' }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource agentApp 'Microsoft.App/containerApps@2026-01-01' = {
  name: agentAppName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      // Internal only: the agent is never exposed to clients (ADR-009).
      ingress: {
        external: false
        targetPort: 8000
        transport: 'auto'
      }
      registries: [
        {
          server: '${registryName}.azurecr.io'
          identity: 'system'
        }
      ]
      secrets: [
        { name: 'postgres-url', keyVaultUrl: '${keyVaultSecretUriBase}postgres-url', identity: 'system' }
        { name: 'redis-url', keyVaultUrl: '${keyVaultSecretUriBase}redis-url', identity: 'system' }
        { name: 'agent-internal-token', keyVaultUrl: '${keyVaultSecretUriBase}agent-internal-token', identity: 'system' }
        { name: 'llm-api-key', keyVaultUrl: '${keyVaultSecretUriBase}llm-api-key', identity: 'system' }
        { name: 'embeddings-api-key', keyVaultUrl: '${keyVaultSecretUriBase}embeddings-api-key', identity: 'system' }
        { name: 'vision-api-key', keyVaultUrl: '${keyVaultSecretUriBase}vision-api-key', identity: 'system' }
      ]
    }
    template: {
      containers: [
        {
          name: agentAppName
          image: agentImage
          resources: { cpu: json('0.5'), memory: '1.0Gi' }
          env: [
            // Three different formats for the same credentials, because the two
            // services speak different clients:
            //   API   -> Npgsql      Host=...;Password=...   (postgres-connection-string)
            //   agent -> SQLAlchemy  postgresql+asyncpg://   (postgres-url)
            //   agent -> redis-py    rediss://               (redis-url)
            { name: 'DATABASE_URL', secretRef: 'postgres-url' }
            { name: 'REDIS_URL', secretRef: 'redis-url' }
            { name: 'INTERNAL_API_TOKEN', secretRef: 'agent-internal-token' }
            { name: 'API_BASE_URL', value: 'https://${apiAppName}.${apiFqdnSuffix}' }
            // Without this the agent logs "event listener is idle" and never
            // receives message.received - and every health check still passes.
            { name: 'SUBSCRIBE_EVENT_TYPES', value: 'message.received' }
            { name: 'AGENT_LLM_ENABLED', value: 'true' }
            { name: 'LLM_PROVIDER', value: 'deepseek' }
            { name: 'LLM_API_KEY', secretRef: 'llm-api-key' }
            { name: 'LLM_BASE_URL', value: llmBaseUrl }
            { name: 'LLM_MODEL', value: llmModel }
            { name: 'EMBEDDINGS_API_KEY', secretRef: 'embeddings-api-key' }
            { name: 'EMBEDDINGS_BASE_URL', value: embeddingsBaseUrl }
            { name: 'EMBEDDINGS_MODEL', value: embeddingsModel }
            { name: 'VISION_API_KEY', secretRef: 'vision-api-key' }
            { name: 'VISION_BASE_URL', value: visionBaseUrl }
            { name: 'VISION_MODEL', value: visionModel }
            { name: 'OTEL_SERVICE_NAME', value: 'aveline-agent-service' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// =============================================================================
// 8. Role assignments
//    Each app pulls its image with its own identity (AcrPull) and reads its
//    secrets from Key Vault (Key Vault Secrets User). Key Vault uses RBAC, so
//    these are the only thing standing between a revision and a failed start.
// =============================================================================
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource apiAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, apiApp.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource apiKvSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiApp.id, kvSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource agentAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, agentApp.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: agentApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource agentKvSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, agentApp.id, kvSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: agentApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// =============================================================================
// Outputs
// =============================================================================
output apiFqdn string = '${apiAppName}.${apiFqdnSuffix}'
output agentInternalFqdn string = agentInternalFqdn
output registryLoginServer string = registry.properties.loginServer
output keyVaultUri string = keyVault.properties.vaultUri
output postgresFqdn string = postgres.properties.fullyQualifiedDomainName
output redisHostName string = redis.properties.hostName
output apiPrincipalId string = apiApp.identity.principalId
output agentPrincipalId string = agentApp.identity.principalId
