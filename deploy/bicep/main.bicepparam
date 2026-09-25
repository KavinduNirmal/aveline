// =============================================================================
// Example parameters for main.bicep.
//
//   az deployment group what-if -g rg-aveline -f main.bicep -p main.bicepparam
//
// Every secret is read from the environment with `readEnvironmentVariable`, so
// no credential is ever written to this file. Export the variables before
// running the command, or let the deploy workflow populate them from GitHub
// secrets.
//
// Container Apps secret names must be <= 20 characters. The Key Vault secret
// names are the same strings, and the apps reference them by name.
// =============================================================================

using './main.bicep'

param location = 'malaysiawest'

param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')

param apiImage = readEnvironmentVariable('API_IMAGE')
param agentImage = readEnvironmentVariable('AGENT_IMAGE')

param firebaseServiceAccountJson = readEnvironmentVariable('FIREBASE_SERVICE_ACCOUNT_JSON')

param secrets = {
  // --- connection strings -----------------------------------------------------
  // Npgsql form, for the .NET API.
  'postgres-connection-string': readEnvironmentVariable('POSTGRES_CONNECTION_STRING')
  // SQLAlchemy form (postgresql+asyncpg://), for the Python agent.
  'postgres-url': readEnvironmentVariable('POSTGRES_URL')
  // StackExchange.Redis form, for the .NET API.
  'redis-connection-string': readEnvironmentVariable('REDIS_CONNECTION_STRING')
  // redis-py form (rediss://), for the Python agent.
  'redis-url': readEnvironmentVariable('REDIS_URL')

  // --- production boot guards -------------------------------------------------
  // The API REFUSES to start in Production without both of these.
  'metrics-scrape-token': readEnvironmentVariable('METRICS_SCRAPE_TOKEN')
  'telemetry-ip-hash-salt': readEnvironmentVariable('TELEMETRY_IP_HASH_SALT')

  // --- media ------------------------------------------------------------------
  // MediaOptionsValidator requires all three when Media:Provider=cloudinary.
  'media-signing-key': readEnvironmentVariable('MEDIA_SIGNING_KEY')
  'cloudinary-url': readEnvironmentVariable('CLOUDINARY_URL')

  // --- identity and crypto ----------------------------------------------------
  'clerk-secret-key': readEnvironmentVariable('CLERK_SECRET_KEY')
  'credentials-encryption-key': readEnvironmentVariable('CREDENTIALS_ENCRYPTION_KEY')
  'privacy-link-signing-key': readEnvironmentVariable('PRIVACY_LINK_SIGNING_KEY')
  // Shared API<->agent token; the same value on both apps.
  'agent-internal-token': readEnvironmentVariable('AGENT_INTERNAL_TOKEN')

  // --- model providers --------------------------------------------------------
  'llm-api-key': readEnvironmentVariable('LLM_API_KEY')
  'embeddings-api-key': readEnvironmentVariable('EMBEDDINGS_API_KEY')
  'vision-api-key': readEnvironmentVariable('VISION_API_KEY')
}
