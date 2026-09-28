# PRD: APIM Effective Policy Flow Visualizer

## 1. Overview

**Product Name:** APIM Effective Policy Flow Visualizer

**Summary:** A proof-of-concept web tool that connects to an Azure API
Management (APIM) instance using the current user's Azure credentials,
lets the user dynamically pick a policy scope (Global, Product, API, or
Operation), calls APIM's "effective policy" calculation for that scope,
and renders the resulting policy XML as an interactive flow diagram
showing sequential steps, branching (`choose`/`when`/`otherwise`), retry
loops, and error paths. It exists to make it fast to understand how
inherited and overridden policies actually compose at runtime, instead of
reading raw XML by hand.

**Target Platform:** Desktop web browser (Chromium-based: Edge/Chrome),
backend and frontend run locally for this POC (no cloud hosting or IaC
required). Authentication uses the developer's own Azure identity via the
local `az CLI` login context — no service principals or stored secrets.

**Key Constraints:**
- No mobile/responsive support required — desktop viewports only (see
  `VIS-CONSTRAINT-01`).
- Read-only: this tool never writes, edits, or publishes APIM policies.
- No persistence layer: retrieved policy XML and derived diagrams exist
  only for the lifetime of the HTTP request/response and the browser tab.

**Historical Sources:** [`IDEA.md`](../IDEA.md) (repository root, preserved
as-is; not an execution source);
[`docs/research/visualizer-enhancements.md`](research/visualizer-enhancements.md)
(research recommendations for a hierarchical, semantics-aware policy
visualisation; not an execution source in its own right — see
[Semantic Policy Flow Visualization](features/semantic-policy-flow-visualization.md)
for the executable scoping of that material).

---

## 2. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-09-24 | Copilot CLI (headless authoring) | Initial PRD authored from `IDEA.md`; no prior canonical docs existed. |
| 1.1 | 2026-09-25 | Copilot CLI (headless authoring) | Registered Feature 4: Policy Flow Visualizer Enhancements, scoped from `docs/research/visualizer-enhancements.md`; no existing feature definitions, task IDs, or requirements were altered. |
| 1.2 | 2026-09-25 | Copilot (feature authoring) | Retired unimplemented Feature 4 (Policy Flow Visualizer Enhancements, `ENH-*`) and replaced it with Feature 4: Semantic Policy Flow Visualization (`SPF-*`), a full overhaul of the flow model and diagram. Amended `VIS-SEC-03` and `POL-FR-05` to permit syntax-only (Roslyn) parsing of policy expressions while still forbidding compilation, evaluation and execution. |

---

## 3. Goals and Non-Goals

```forge-requirement
{"id":"VIS-US-01","kind":"story","text":"As a platform engineer, I want to view the effective policy for any APIM scope as a flow diagram, so that I can understand and debug how global, product, API, and operation policies compose."}
```

### 3.1 Goals
- Let a user dynamically select a policy scope (Global / Product / API /
  Operation) within a configurable APIM instance.
- Calculate and retrieve the effective (post-inheritance) policy XML for
  that scope.
- Translate that XML into an accurate, readable flow diagram: ordered
  steps per `inbound`/`backend`/`outbound`/`on-error` section, explicit
  branching for `choose`/`when`/`otherwise`, and explicit error/retry
  paths.
- Authenticate using only the current user's Azure identity
  (`DefaultAzureCredential`, backed by `az CLI` login locally).
- Ship a visually polished, desktop-only web UI suitable for demos.

### 3.2 Non-Goals
- Mobile or responsive-breakpoint support.
- Editing, publishing, versioning, or testing policies against live
  traffic.
- Multi-tenant auth, user management, or service-principal-based
  authentication.
- Production-grade hosting, scaling, monitoring, or high availability.
- Evaluating or executing policy expressions (`@(...)` C# expressions) —
  these are shown as opaque text only.
- Support for API gateways other than Azure APIM.

---

## 4. Personas

| Persona | Description | Key Needs |
|---------|-------------|-----------|
| Platform Engineer / API Owner | Manages or debugs APIM configuration across global, product, API, and operation scopes. | Quickly see what policy will actually execute at a given scope after inheritance, without hand-tracing XML across scopes. |

---

## 5. Research Findings

- APIM policy inheritance is calculated per-scope by APIM itself; the ARM
  "get policy" REST operation exposes an `effective=true` query parameter
  (with `format=xml`; `rawxml` is not well-formed when policy expressions
  contain quotes) at the Global, Product, API, and Operation policy
  endpoints, returning the fully inherited/overridden XML for that scope.
  The typed `Azure.ResourceManager.ApiManagement` SDK (as of 1.3.1) does
  not expose a strongly-typed "effective" flag, so this POC calls the
  authenticated ARM REST endpoint directly for that specific operation
  while using the typed SDK for hierarchy discovery (listing
  products/APIs/operations).
- Current stable technology versions verified via web search (September
  2026): .NET 10 (LTS, supported to Nov 2028), `Azure.Identity` 1.17.2,
  `Azure.ResourceManager.ApiManagement` 1.3.1, React 19.2.0, Vite 6.1.0,
  Node.js 24.x (Active LTS), `@xyflow/react` 12.11.6.

---

## 6. Technical Architecture

### 6.1 Technology Stack
- **Backend:** ASP.NET Core Web API, .NET 10 (LTS), C#.
- **Azure auth:** `Azure.Identity` 1.17.2 `DefaultAzureCredential` (resolves
  the local `az CLI` login context in development; no secrets in config).
- **Azure hierarchy discovery:** `Azure.ResourceManager.ApiManagement`
  1.3.1 (list products, APIs, operations).
- **Effective policy retrieval:** authenticated `HttpClient` call to the
  ARM REST "get policy" endpoint with `format=xml&effective=true` at
  the selected scope, using a bearer token from the same credential.
- **Backend tests:** xUnit + NSubstitute.
- **Frontend:** React 19.2.0 + TypeScript, built with Vite 6.1.0.
- **Flow diagram rendering:** `@xyflow/react` 12.11.6.
- **Frontend tests:** Vitest + React Testing Library + `axe-core` for
  automated accessibility checks.
- **Runtime:** Node.js 24.x (Active LTS) for frontend tooling.

### 6.2 Project Structure
```
src/
  server/ApimPolicyVisualizer.Api/   # ASP.NET Core Web API
  web/                                # React + Vite frontend
tests/
  ApimPolicyVisualizer.Api.Tests/     # xUnit backend tests
  smoke/live-smoke.mjs                # live Azure APIM smoke test (az login)
docs/
  PRD.md
  features/*.md
  reviews/*.json                      # human-review evidence
```

### 6.3 Key APIs / Interfaces
- `GET /api/scopes` — returns the Global scope plus the product/API/
  operation hierarchy for the configured APIM instance (owned by
  `docs/features/apim-connectivity-scope.md`).
- `GET /api/policy/effective-flow?scope=...` — returns the effective
  policy flow graph (nodes/edges/sections) as JSON for a given scope (owned
  by `docs/features/effective-policy-flow-model.md`).

---

## 7. Non-Functional Requirements

```forge-requirement
{"id":"VIS-NFR-01","kind":"requirement","text":"The effective-policy fetch-to-render round trip completes in under 5 seconds for policy XML up to 200 KB on a typical local development machine."}
```

| ID | Requirement | Priority |
|----|-------------|----------|
| VIS-NFR-01 | Fetch-to-render round trip under 5s for policies up to 200 KB | Should |

---

## 8. Security and Privacy

```forge-requirement
{"id":"VIS-CONSTRAINT-01","kind":"constraint","text":"No mobile/responsive layout is required; the UI targets desktop browser viewports (>=1280px) only."}
```

```forge-requirement
{"id":"VIS-SEC-01","kind":"constraint","text":"The application must never store Azure credentials or secrets in configuration, code, or logs; all Azure access uses DefaultAzureCredential resolved from the local environment (az CLI, environment, managed identity) at request time."}
```

```forge-requirement
{"id":"VIS-SEC-02","kind":"constraint","text":"Retrieved policy XML and derived flow graphs must not be persisted to disk, a database, or any cache with a lifetime beyond the current HTTP request/response and browser session; the tool has no persistence layer."}
```

```forge-requirement
{"id":"VIS-SEC-03","kind":"constraint","text":"The backend must never compile, evaluate or execute policy expression bodies (content inside @(...)/@{...} or <set-variable>/<choose> conditions); syntax-only parsing for static analysis is permitted, every fact derived from it is labeled inferred, and expression text is otherwise treated as display data."}
```

| ID | Requirement | Priority |
|----|-------------|----------|
| VIS-SEC-01 | No stored credentials/secrets; `DefaultAzureCredential` only | Must |
| VIS-SEC-02 | No persistence of policy content beyond request/session lifetime | Must |
| VIS-SEC-03 | No compilation/execution/evaluation of policy expressions (syntax-only parsing allowed) | Must |

Data handling note: this POC handles no end-user personal data. The only
sensitive material in scope is APIM policy XML itself, which can reference
backend URLs and named values; it is treated as sensitive-in-transit and
never logged in full or persisted (see `VIS-SEC-02`).

---

## 9. Accessibility

```forge-requirement
{"id":"VIS-A11Y-01","kind":"requirement","text":"The web UI's scope-selection and diagram views must pass an automated axe-core accessibility scan with zero serious or critical violations."}
```

| ID | Requirement | Priority |
|----|-------------|----------|
| VIS-A11Y-01 | Zero serious/critical axe-core violations on scope selector and diagram views | Should |

Full manual WCAG 2.1 AA audit is out of scope for this POC (see Open
Question 5); automated baseline scanning is the accepted bar.

---

## 10. System States / Lifecycle

1. **Idle** — app loaded, no scope selected.
2. **Scope selection** — user browses Global/Product/API/Operation
   hierarchy.
3. **Loading effective policy** — backend calculates/fetches effective
   policy XML and parses it into a flow graph.
4. **Rendered** — flow diagram displayed for the selected scope.
5. **Error** — permission denied, scope not found, or parse failure is
   surfaced with a clear message; user can return to scope selection.

---

## 11. Analytics / Success Metrics

| Metric | Target | Measurement Method |
|--------|--------|--------------------|
| Effective-policy round trip time | < 5s for policies ≤ 200 KB | Manual timing during acceptance testing (see `VIS-NFR-01`) |
| Diagram accuracy | 100% of sequential steps and branches present in source XML are represented in the diagram | Manual comparison against sample policies during acceptance testing |

---

## 12. Dependencies and Risks

### 12.1 Dependencies
- An existing Azure APIM instance reachable by the developer's Azure
  identity, defaulted to `apim-zlyway6g7icoy` (configurable).
- Local `az CLI` login (`az login`) providing the credential context for
  `DefaultAzureCredential` during development/testing.
- `Azure.Identity`, `Azure.ResourceManager.ApiManagement` NuGet packages;
  `@xyflow/react` npm package.

### 12.2 Risks
- **Ambiguous subscription/resource-group resolution**: if the configured
  service name exists in multiple subscriptions visible to the user,
  automatic resolution could pick the wrong one. Mitigated by allowing
  explicit `subscriptionId`/`resourceGroupName` overrides in configuration
  (see `docs/features/apim-connectivity-scope.md`).
- **Complex/unsupported policy XML constructs**: policies using rarely
  used or custom policy elements may not have a dedicated visual
  representation. Mitigated by rendering unrecognized elements as generic
  labeled step nodes rather than failing the whole diagram.
- **ARM REST surface changes**: the "effective policy" REST operation used
  here is a direct ARM call rather than a typed SDK method, so a future
  API version change could require an update. Mitigated by pinning the ARM
  API version explicitly in the effective-policy client.

---

## 13. Future Considerations

- Exporting the rendered diagram (PNG/SVG) or the flow graph JSON.
- Side-by-side comparison of effective policy across two scopes.
- Support for API Management workspaces as an additional scope level.

---

## 14. Features

| # | Feature | File | Dependencies | Priority |
|---|---------|------|---------------|----------|
| 1 | Azure APIM Connectivity and Scope Discovery | [docs/features/apim-connectivity-scope.md](features/apim-connectivity-scope.md) | None | Must |
| 2 | Effective Policy Retrieval and Flow Graph Model | [docs/features/effective-policy-flow-model.md](features/effective-policy-flow-model.md) | Azure APIM Connectivity and Scope Discovery | Must |
| 3 | Policy Flow Visualization Web UI | [docs/features/policy-flow-visualization-ui.md](features/policy-flow-visualization-ui.md) | Azure APIM Connectivity and Scope Discovery, Effective Policy Retrieval and Flow Graph Model | Must |
| 4 | Semantic Policy Flow Visualization | [docs/features/semantic-policy-flow-visualization.md](features/semantic-policy-flow-visualization.md) | Effective Policy Retrieval and Flow Graph Model, Policy Flow Visualization Web UI | Must |

### Feature Dependency Graph

```
Feature 1: Azure APIM Connectivity and Scope Discovery (foundation)
└── Feature 2: Effective Policy Retrieval and Flow Graph Model (requires Feature 1)
    └── Feature 3: Policy Flow Visualization Web UI (requires Feature 1 + Feature 2)
    └── Feature 4: Semantic Policy Flow Visualization (requires Feature 2 + Feature 3; replaces their parser and diagram implementation)
```

---

## 15. Glossary

| Term | Definition |
|------|------------|
| APIM | Azure API Management, Microsoft's managed API gateway service. |
| Effective policy | The final policy XML in force at a given scope after resolving inheritance and overrides from Global → Product → API → Operation. |
| Scope | The level at which a policy is calculated: Global, Product, API, or Operation. |
| Policy fragment | A reusable named-value/policy snippet inserted via `<include-fragment>`; APIM inlines these when calculating effective policy. |
| Flow graph | The node/edge JSON model this tool derives from policy XML to drive diagram rendering. |

---

## 16. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Which subscription/resource group hosts `apim-zlyway6g7icoy` (or any configured instance)? | Resolved automatically at startup via an Azure Resource Graph query scoped to subscriptions visible under the current `az CLI` identity, matching by service name; explicit `subscriptionId`/`resourceGroupName` config values override auto-resolution when set. |
| 2 | Should the tool support more than one APIM instance at a time? | No — single configurable instance per running session, matching the IDEA's "configurable existing APIM instance, defaulted to ... apim-zlyway6g7icoy". |
| 3 | Is any cloud hosting or infrastructure-as-code required for this POC? | No — the POC runs locally only (`dotnet run` + `npm run dev`); no IaC or deployment pipeline is in scope. |
| 4 | Does the flow parser need to resolve `<include-fragment>` policy fragments itself? | No — APIM's effective-policy calculation already inlines fragments and inheritance before returning XML, so the parser only needs to handle the returned, already-resolved XML. |
| 5 | What level of accessibility conformance is required? | Automated `axe-core` baseline (zero serious/critical violations) rather than a full manual WCAG 2.1 AA audit, consistent with the POC's explicit "no mobile support" simplification. |
| 6 | Does the web UI need its own login/auth layer separate from the backend's Azure auth? | No — this POC is single-user/localhost; the backend authenticates to Azure, and the browser-to-backend hop has no separate auth layer. |
