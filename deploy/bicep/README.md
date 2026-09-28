# Aveline — Bicep spine

A **curated** template for the Azure spine, derived from a live ARM capture of `rg-aveline`
(`../live-arm.json`).

## Validation status — read this first

| Claim | Status |
|---|---|
| Compiles | **Yes** — `az bicep build` passes with zero errors and zero warnings |
| Every API version matches a live resource | **Yes** — taken from `../live-arm.json`, not from memory |
| `environmentMode: ConsumptionOnly` is emitted | **Yes** — verified in the compiled ARM |
| **Ever deployed to an empty resource group** | **No** |

It has **not** been applied end to end. Treat it as a reviewed starting point, not as proven
infrastructure. Validate before trusting it:

```bash
az group create -n rg-aveline -l malaysiawest     # the template is resource-group scoped
az deployment group what-if -g rg-aveline -f main.bicep -p main.bicepparam
```

## Why curated rather than exported

A mechanical `az group export` → `az bicep decompile` **cannot** produce a usable Bicep template for
this environment:

```text
Error max-resources: Too many resources. Number of resources is limited to 800.
```

The capture holds 1,354 resources, of which only **34 are meaningful**. 680 are
`Microsoft.OperationalInsights/workspaces/tables`, 596 are
`Microsoft.DBforPostgreSQL/flexibleServers/configurations`, and 39 are saved searches. They are
platform-managed children created implicitly by the workspace and the server, and they push the
decompiled file past Bicep's limit. This template models the ~15 real resources instead.

## Contents

```
main.bicep          the spine
main.bicepparam     example parameters; every secret read from the environment
```

`main.bicep` provisions: Log Analytics, Container Registry, Key Vault (+ secrets), PostgreSQL 16
Flexible Server (+ `azure.extensions` allow-list, database, firewall rule), Azure Managed Redis
(+ database), the Container Apps environment, both Container Apps, and the four role assignments.

## Choices that are deliberate, not defaults

Each of these was a defect during the 2026-09-25 deployment, so they are documented rather than
left to look like accidents:

- **`environmentMode: 'ConsumptionOnly'`.** The CLI default in `malaysiawest` produced an
  **Express** environment, which does not support managed identity for registry authentication. The
  apps then ran `mcr.microsoft.com/k8se/quickstart:latest` — the placeholder image — while their
  revisions reported `Healthy`. A green health check proved nothing about the application.
- **`evictionPolicy: 'NoEviction'`.** Azure's default is `VolatileLRU`, which evicts keys carrying a
  TTL. The distributed job lock and the idempotency mutex are TTL-bearing, so the default makes them
  evictable: a lost job lock means duplicate job runs, a lost idempotency key means a duplicate
  charge. Nothing is evicted here.
- **`clusteringPolicy: 'NoCluster'`.** The application is written for a single node. Clustered
  pub/sub is compatible, but this removes the whole class of `MOVED`-redirect behaviour.
- **`minReplicas: 1` on both apps.** The API is not a stateless handler: it registers 26 in-process
  `PeriodicTimer` jobs and holds the `Redis PSUBSCRIBE` subscriptions. Scaled to zero, the timers do
  not tick and agent→API events are dropped.
- **`adminUserEnabled: false` on the registry.** Images are pulled with an Entra ID identity via
  `AcrPull`, so there is no registry password to leak.
- **`enablePurgeProtection: false` on the vault.** Deliberate for a beta that will be torn down;
  purge protection is irreversible once enabled.
- **`SUBSCRIBE_EVENT_TYPES=message.received` on the agent.** Without it the agent logs
  *"event listener is idle"* and never receives `message.received` — while every health check still
  passes, because the agent is *reachable*.
- **`Media__Provider=cloudinary` plus `Media__SigningKey`, `Media__PublicBaseUrl`.** Production
  refuses the `database` provider unless the escape hatch is set, so all three are required for a
  clean boot.
- **Three connection-string formats**, because the two services use different clients:
  Npgsql (`postgres-connection-string`) for the API, `postgresql+asyncpg://` (`postgres-url`) and
  `rediss://` (`redis-url`) for the agent.

## Known gaps

- **Key Vault secrets are created by this template**, so `secrets` must be supplied on every
  deployment. Values are written into the vault; the container apps reference them by name rather
  than holding copies.
- **No custom domain or managed certificate.** `api.aveline.gravora.dev` is not provisioned here;
  its certificate has not issued.
- **No observability tier.** Prometheus, Grafana and postgres_exporter are not modelled.
- **`keyVault.properties.vaultUri` is avoided in variables** (BCP182: a variable may only use
  start-of-deployment values), so the vault URI is built from the name via
  `environment().suffixes.keyvaultDns`.
