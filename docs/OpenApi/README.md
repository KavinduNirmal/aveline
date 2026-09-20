# OpenAPI — API Specification Files

This folder contains the OpenAPI (Swagger) specification files for the Aveline API.

## What belongs here

- **`aveline-api.yaml`** (or `.json`) — The exported OpenAPI 3.0 spec generated from
  the ASP.NET Core project. There is **no export flag**: the API serves the generated
  document at `http://localhost:5091/openapi/v1.json` in Development
  (`Program.cs` maps it with `AllowAnonymous`), so that URL is the export path. An
  earlier revision of this file documented a `-- --openapi-export` argument that was
  never implemented (defect D-6 of the Business KPIs plan).

  Note the normative contract for shipped client-facing routes is the
  hand-maintained [`docs/api/openapi.yaml`](../api/openapi.yaml); see
  [`docs/api/README.md` §1.3](../api/README.md) for which document wins when the two
  disagree.

- **Slice-specific specs** (optional, for clarity during development):
  - `customer-concierge-api.yaml`
  - `visual-intelligence-api.yaml`
  - `commerce-api.yaml`

## Usage

- Import into **Postman** or **Insomnia** for manual testing
- Import into **Swagger UI** for API documentation
- Share with team members to understand the API contract before implementation

## Rules

- Never hand-edit the exported spec — it is generated from the code
- Do commit the spec to version control — it serves as the API contract
- The agent service (`agnet-service/`) is internal and does not need a public spec
