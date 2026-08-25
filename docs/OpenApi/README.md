# OpenAPI — API Specification Files

This folder contains the OpenAPI (Swagger) specification files for the Aveline API.

## What belongs here

- **`aveline-api.yaml`** (or `.json`) — The exported OpenAPI 3.0 spec generated from
  the ASP.NET Core project. Export using:
  ```bash
  dotnet run --project Aveline.Api -- --openapi-export
  ```
  Or download from `http://localhost:5000/openapi/v1.json` when the API is running.

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
