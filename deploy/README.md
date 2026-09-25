# Aveline — Azure deployment (live, 2026-09-25)

This directory records the **actual deployed infrastructure**. The plan in
[`docs/deployment.md`](../docs/deployment.md) was written on 2026-09-01 and several of its claims
were proven false during the deployment; see the correction block at the top of that document.

## What is live

Resource group **`rg-aveline`**, region **`malaysiawest`** — the only permitted region that can host
the whole stack under this subscription's region policy (`indonesiacentral`, `indiasouthcentral`,
`koreacentral`, `malaysiawest`, `uaenorth`; Static Web Apps exists in none of them).

| Resource | Name | Notes |
|---|---|---|
| Resource group | `rg-aveline` | `malaysiawest` |
| Container Apps environment | `cae-aveline` | **ConsumptionOnly**, no VNet |
| Container App | `aveline-api` | External ingress :8080, `minReplicas: 1` |
| Container App | `aveline-agent` | **Internal ingress only** :8000, `minReplicas: 1` |
| PostgreSQL Flexible Server | `pg-aveline` | v16.15, Burstable `Standard_B1ms`, 32 GB |
| PostgreSQL database | `aveline` | `vector` + `btree_gist` allow-listed via `azure.extensions` |
| Azure Managed Redis | `redis-aveline` | Balanced **B0**, **`NoCluster`**, **`NoEviction`**, TLS :10000 |
| Container Registry | `avelineacr` | Basic, **admin user disabled** (Entra ID pull) |
| Key Vault | `kv-aveline-mw` | RBAC authorization enabled |
| Log Analytics | `log-aveline` | 30-day retention |

Static frontend: **`https://aveline.gravora.dev`** (Vercel, project `aveline`, root directory
`frontend/web`).

Application images are tagged with the git SHA **and** `latest`:
`avelineacr.azurecr.io/aveline-api:215db29`, `avelineacr.azure.io/aveline-agent:9891b59`.

## `live-arm.json`

A point-in-time ARM capture of `rg-aveline`:

```bash
az group export -n rg-aveline --include-parameter-default-value > deploy/live-arm.json
```

**It contains no secret values.** Key Vault secrets are exported as resources carrying only
`properties.attributes`; the values are not emitted. Verified by scanning for credential-shaped
strings (`sk_live_`, `pk_live_`, `cloudinary://`, `Password=`, `AccountKey=`) — all zero matches.

The capture is 1,354 resources, of which only **34 are meaningful**:

- 680 are `Microsoft.OperationalInsights/workspaces/tables`
- 596 are `Microsoft.DBforPostgreSQL/flexibleServers/configurations`
- 39 are `workspaces/savedSearches`, 5 are ACR `scopeMaps`

The rest are the 7 top-level resources plus their children (databases, firewall rules, the 16 Key
Vault secrets, the managed certificate).

## Known limitation: this cannot be decompiled into a usable Bicep

```bash
az bicep decompile --file deploy/live-arm.json
```

This produces a **16,422-line** file that fails to build:

```text
Error max-resources: Too many resources. Number of resources is limited to 800.
```

So the raw export → decompile path **cannot** yield a deployable Bicep template for this
environment. The 1,276 noise resources (workspace tables and server parameters) push it past
Bicep's limit even though the real infrastructure is 34 resources.

Producing a deployable Bicep requires a **curated** template that models the ~15 real resources and
omits the platform-managed children, rather than a mechanical export. That work is not done here,
and no unvalidated curated template has been committed — an untested template presented as the
deliverable would be worse than accurately documenting the gap.

## How the environment was created

Everything was created with the Azure CLI from a workstation. The relevant sequence, abbreviated:

```bash
# providers, resource group, logging, registry, vault
az provider register --namespace Microsoft.App --wait          # (and 6 others)
az group create -n rg-aveline -l malaysiawest
az monitor log-analytics workspace create -g rg-aveline -n log-aveline -l malaysiawest
az acr create -g rg-aveline -n avelineacr --sku Basic --admin-enabled false
az keyvault create -g rg-aveline -n kv-aveline-mw --enable-rbac-authorization true

# data tier
az postgres flexible-server create -g rg-aveline -n pg-aveline -l malaysiawest \
  --tier Burstable --sku-name Standard_B1ms --version 16 --storage-size 32 --public-access 0.0.0.0
az postgres flexible-server parameter set -g rg-aveline --server-name pg-aveline \
  --name azure.extensions --value vector,btree_gist
az redisenterprise create -g rg-aveline --cluster-name redis-aveline -l malaysiawest \
  --sku Balanced_B0 --clustering-policy NoCluster --eviction-policy NoEviction \
  --access-keys-auth Enabled --public-network-access Enabled

# apps
az containerapp env create -n cae-aveline -g rg-aveline -l malaysiawest \
  --environment-mode ConsumptionOnly --logs-workspace-id <id> --logs-workspace-key <key>
```

Schema migrations are applied out of band, because `Program.cs` only migrates when
`IsDevelopment()`:

```bash
dotnet ef migrations bundle --project Aveline.Api --startup-project Aveline.Api -o efbundle
./efbundle --connection "<azure connection string>"
```

## Deliberate deviations from the plan

| Plan said | Deployed | Why |
|---|---|---|
| Static Web Apps for the SPA | **Vercel** | SWA exists in no permitted region |
| Azure Cache for Redis | **Azure Managed Redis** | classic Redis is unavailable in all 5 permitted regions |
| `minReplicas: 0` | **`minReplicas: 1`** | the API holds 26 `PeriodicTimer` jobs and the `PSUBSCRIBE`; asleep means jobs never tick and events are dropped |
| Redis default eviction | **`NoEviction`** | Azure's `VolatileLRU` default makes the TTL-bearing job lock and idempotency keys evictable |
| Bicep | **CLI + this ARM capture** | see the limitation above |

## Open items

- **No CI/CD deploy job exists.** `.github/workflows/ci.yml` has no `azure/login`, no ACR push and
  no Container Apps update. Deployments are manual.
- **Custom domain `api.aveline.gravora.dev`**: DNS (A record + `asuid` TXT) is correct, but the
  managed certificate has not issued. The API is served from its
  `*.azurecontainerapps.io` hostname, which is what the SPA calls.
- **Observability tier** (Prometheus, Grafana, postgres_exporter) is not deployed. Deferred
  deliberately; the compose definitions exist under `observability/`.
- **Mobile APK** still passes no `--dart-define`, so released APKs point at an emulator loopback
  address.
