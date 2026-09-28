# APIM Effective Policy Flow Visualizer

A local, read-only web application for exploring effective policies in Azure API
Management (APIM). Select a Global, Product, API, or Operation scope to retrieve
its effective policy XML and explore a hierarchical flow diagram instead of
manually tracing inherited policy definitions.

This is a **desktop-focused proof of concept**, not a production-hosted service
or a policy execution engine.

## Features

- Discover products, APIs, and operations in an APIM instance.
- Visualize inbound, backend, outbound, and on-error stages, including branches,
  retries, early responses, and error paths.
- Expand and collapse nested policy and fragment groups in place.
- Switch between **Flow map**, **Stage board**, and **Reading view**.
- Search the policy, navigate its outline, and inspect source XML, outcomes,
  diagnostics, and inferred variable dependencies.
- Analyze policy expressions using syntax only: expressions are never compiled
  or executed, and inferred relationships are not runtime guarantees.

The backend uses ASP.NET Core on .NET 10, Azure Identity, the APIM management SDK,
and Roslyn. The frontend uses React 19, TypeScript, Vite, React Flow, and ELK.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Node.js](https://nodejs.org/) 24.x with npm (recommended).
- [PowerShell 7+](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows)
  for the Windows startup script.
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) for local
  sign-in, plus an Azure identity with management-plane read access to the target
  APIM service and its policies.
- A desktop Chromium-based browser such as Edge or Chrome; the UI targets
  viewports at least 1280 pixels wide.

Azure access is needed to load real policies, but not to start the servers, run
unit tests, or use the synthetic demo below.

## Quick start

Run these commands in PowerShell from the repository root:

```powershell
az login

$env:Apim__SubscriptionId = "<subscription-id>"
$env:Apim__ResourceGroupName = "<resource-group>"
$env:Apim__ServiceName = "<apim-service-name>"

pwsh -File .\start.ps1
```

Open **http://localhost:5173**, select a scope, and load its policy flow. Press
**Ctrl+C** in the startup terminal to stop both servers.

[start.ps1](start.ps1) checks for the .NET 10 SDK, installs frontend dependencies
with `npm ci` when `node_modules` is absent, and starts:

| Component | Address |
| --- | --- |
| React development server | http://localhost:5173 |
| ASP.NET Core API | http://localhost:5021 |
| API health endpoint | http://localhost:5021/healthz |

Vite proxies `/api` and `/healthz` to the backend. A healthy response confirms
that the API is running, **not** that Azure authentication or APIM access works.

### Configuration and authentication

The environment variables above override the `Apim` section in
[appsettings.json](src/server/ApimPolicyVisualizer.Api/appsettings.json).
They contain resource identifiers, not credentials.

Set all three explicitly for predictable targeting. If the subscription or
resource group is omitted, the backend attempts Azure Resource Graph discovery
by service name across accessible subscriptions, narrowed by any supplied
identifiers. Set the service name even when using discovery; the code has a
development-specific fallback that is unlikely to identify your instance.

Authentication uses `DefaultAzureCredential`. The development launch profiles
set `AZURE_TOKEN_CREDENTIALS=dev`, excluding deployed-service credentials such as
an Azure VM's managed identity. Your Azure CLI login is one supported developer
credential; other signed-in developer tools can also participate in the chain.
Restart the backend after changing its environment or target service.

### Start the servers separately

For separate logs, use two terminals from the repository root. Configure the
APIM environment variables in the API terminal first.

```powershell
# Terminal 1: API
dotnet run --project .\src\server\ApimPolicyVisualizer.Api --launch-profile http
```

```powershell
# Terminal 2: frontend
npm --prefix .\src\web ci
npm --prefix .\src\web run dev -- --host localhost --port 5173 --strictPort
```

## Synthetic demo without Azure

The test fixture host serves the built frontend and synthetic policies through
the real parsing/model pipeline. It does not require Azure credentials.

```powershell
npm --prefix .\src\web ci
npm --prefix .\src\web run build
dotnet run --project .\tests\smoke\UiFixtureHost
```

Open **http://127.0.0.1:5022**. This is a test/demo host, not a deployment setup.
Stop it with **Ctrl+C**.

## Build and test

Run from the repository root. Frontend commands assume dependencies have been
installed with `npm --prefix .\src\web ci`.

```powershell
# Backend build and unit/integration tests
dotnet build .\src\server\ApimPolicyVisualizer.Api
dotnet test .\tests\ApimPolicyVisualizer.Api.Tests

# Frontend type-check, production bundle, and tests (non-watch mode)
npm --prefix .\src\web run build
npm --prefix .\src\web test -- --run
```

For a synthetic HTTP smoke test, keep the fixture host running and use a second
terminal:

```powershell
node .\tests\smoke\ui-fixture-smoke.mjs
```

For browser performance and keyboard checks, build the frontend first and have
Microsoft Edge installed:

```powershell
npm --prefix .\tests\smoke ci
node .\tests\smoke\semantic-flow-browser-smoke.mjs
```

The browser smoke test starts or reuses the synthetic host, never contacts Azure,
and updates [the performance report](docs/reviews/semantic-flow-performance.json).

### Live Azure smoke test

With Azure sign-in and APIM configuration in place:

```powershell
$env:SMOKE_REFERENCE_SCOPE = "apis/<reference-api-id>"
node .\tests\smoke\live-smoke.mjs
```

This starts or reuses the API at `http://localhost:5021`. It requires a non-empty
policy model at each of the four scope kinds and a reference policy with all four
stages, fragment groups, an explicit response, and a backend retry loop. Without
an override, the reference scope is `apis/universal-llm-api`. It is an acceptance
test for a suitably configured instance, not a generic connectivity check; a
simpler APIM instance can fail it even when access works.

## API

| Endpoint | Purpose |
| --- | --- |
| `GET /healthz` | Local health and configured APIM identifiers. |
| `GET /api/scopes` | Available Global, Product, API, and Operation scopes. |
| `GET /api/policy/effective-flow?scope=global` | Effective policy as a source-mapped, hierarchical `schemaVersion: 2` model. |

Use scope IDs returned by `/api/scopes`, such as `products/{product}`,
`apis/{api}`, or `apis/{api}/operations/{operation}`. Failures include problem
details for invalid scopes (400), denied access (403), missing resources or
policies (404), and policy parsing errors (422).

## Troubleshooting

- **Azure sign-in or access failure:** run `az login` for the intended tenant and
  verify that the selected developer identity can read the target APIM service.
  When running outside the supplied launch profiles, check which credential
  `DefaultAzureCredential` selects.
- **Service not found or unexpected service:** set all three `Apim__...`
  variables explicitly and restart the API. Discovery depends on the current
  identity's visible resources.
- **Frontend cannot reach the API:** check `/healthz` on port 5021. The Vite proxy
  expects that port.
- **Port already in use:** stop the conflicting local process or coordinate
  changes to the launch profile and Vite proxy. The startup script requires
  port 5173 to be available.
- **422 parsing error:** inspect the returned problem detail. Unsupported or
  uncertain semantics may also appear as diagnostics in a successfully loaded
  model.

## Boundaries and privacy

- The application reads APIM configuration; it does not edit or publish policies
  or send test traffic through the gateway.
- Diagrams represent static analysis, not an observed request trace. Policy
  expressions are inspected syntactically, never evaluated.
- There is no policy persistence layer. Retrieved XML is included in the model
  sent to the browser for inspection; treat it as potentially sensitive and do
  not commit real policies, credentials, or secrets.
- The app uses the backend process's Azure identity and is intended for local
  use. Do not expose it as a shared or public service without adding appropriate
  authentication and deployment safeguards.

## Repository guide

| Path | Contents |
| --- | --- |
| [src/server/ApimPolicyVisualizer.Api](src/server/ApimPolicyVisualizer.Api) | Azure connectivity, scope discovery, policy parsing, and HTTP API. |
| [src/web](src/web) | React UI, hierarchical graph projection, layout, and frontend tests. |
| [tests/ApimPolicyVisualizer.Api.Tests](tests/ApimPolicyVisualizer.Api.Tests) | Backend tests and synthetic XML fixtures. |
| [tests/smoke](tests/smoke) | Synthetic host, browser checks, and live Azure smoke test. |
| [docs/PRD.md](docs/PRD.md) | Product requirements and architecture. |
| [docs/features](docs/features) | Detailed feature specifications. |
| [docs/PROGRESS.md](docs/PROGRESS.md) | Implementation progress and acceptance-review status. |
