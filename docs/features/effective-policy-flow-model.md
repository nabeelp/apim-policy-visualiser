# Feature: Effective Policy Retrieval and Flow Graph Model

## Traceability

| Canonical ID | Owner / Source Link | Relationship |
|--------------|---------------------|--------------|
| VIS-SEC-02 | [Vision](../PRD.md#8-security-and-privacy) | participates |
| VIS-SEC-03 | [Vision](../PRD.md#8-security-and-privacy) | participates |
| VIS-NFR-01 | [Vision](../PRD.md#7-non-functional-requirements) | participates |
| APIM-FR-01 | [Azure APIM Connectivity and Scope Discovery](apim-connectivity-scope.md#APIM-FR-01) | consumes |
| APIM-FR-03 | [Azure APIM Connectivity and Scope Discovery](apim-connectivity-scope.md#APIM-FR-03) | consumes |
| POL-US-01 | This feature | owns |
| POL-US-02 | This feature | owns |
| POL-FR-01 | This feature | owns |
| POL-FR-02 | This feature | owns |
| POL-FR-03 | This feature | owns |
| POL-FR-04 | This feature | owns |
| POL-FR-05 | This feature | owns |

**PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** Effective Policy Retrieval and Flow Graph Model

**ID Prefix:** POL

**Summary:** Calculates and retrieves the effective (post-inheritance)
policy XML for a scope selected via
[Azure APIM Connectivity and Scope Discovery](apim-connectivity-scope.md),
then parses that XML into a structured, renderable flow graph (ordered
steps per section, branches, retry/error paths) exposed as JSON over HTTP.

**Dependencies:** Azure APIM Connectivity and Scope Discovery

**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|--------------|------------|----------|
| POL-US-01 | platform engineer | calculate the effective policy XML at a selected scope | I can see exactly what policy will execute at runtime after inheritance | Must |
| POL-US-02 | platform engineer | have the raw policy XML translated into a structured flow graph (steps, branches, error paths) | I can visualize control flow instead of reading raw XML by hand | Must |

```forge-requirement
{"id":"POL-US-01","kind":"story","text":"As a platform engineer, I want to calculate the effective policy XML at a selected scope so I can see exactly what policy will execute at runtime after inheritance."}
```

```forge-requirement
{"id":"POL-US-02","kind":"story","text":"As a platform engineer, I want the raw policy XML translated into a structured flow graph (steps, branches, error paths) so I can visualize control flow instead of reading raw XML by hand."}
```

---

## 3. Functional Requirements

```forge-requirement
{"id":"POL-FR-01","kind":"requirement","text":"The backend retrieves the effective policy XML for a given scope (global, product, api, or operation) via an authenticated ARM REST call using format=xml&effective=true against the appropriate policy endpoint, reusing the ArmTokenProvider credential from Azure APIM Connectivity and Scope Discovery, and pins an explicit ARM api-version."}
```

```forge-requirement
{"id":"POL-FR-02","kind":"requirement","text":"The parser converts the inbound, backend, outbound, and on-error policy sections into an ordered directed graph of step nodes with unique IDs, each tagged with its owning section, preserving document order within each section."}
```

```forge-requirement
{"id":"POL-FR-03","kind":"requirement","text":"The parser represents <choose>/<when>/<otherwise> constructs as branch nodes with labeled conditional edges (condition text as the label), and <retry> and on-error blocks as explicit loop-back or error-path edges, preserving nesting depth in the graph structure."}
```

```forge-requirement
{"id":"POL-FR-04","kind":"requirement","text":"The backend exposes GET /api/policy/effective-flow?scope={scopeId} returning the combined flow graph as JSON (nodes, edges, and section metadata) for the requested scope, returning HTTP 404/403 with descriptive errors mirroring the underlying retrieval failure."}
```

```forge-requirement
{"id":"POL-FR-05","kind":"requirement","text":"The parser must never compile, execute or evaluate policy expression bodies (C# code inside @(...)/@{...} or condition attributes); it captures such text verbatim on the corresponding node/edge and may parse it syntax-only for static analysis, labeling every derived fact as inferred."}
```

| ID | Priority |
|----|----------|
| POL-FR-01 | Must |
| POL-FR-02 | Must |
| POL-FR-03 | Must |
| POL-FR-04 | Must |
| POL-FR-05 | Must |

---

## 4. UI / Interaction Design

No UI in this feature. The `GET /api/policy/effective-flow` response is
consumed by the diagram renderer in
[Policy Flow Visualization Web UI](policy-flow-visualization-ui.md).

---

## 5. Implementation Tasks

### Phase 1: Effective Policy Pipeline

```forge-task
{
  "id": "POL-1",
  "title": "Implement effective policy XML retrieval client",
  "description": "Implement EffectivePolicyClient that, given a scope descriptor (global/product/api/operation with the relevant resource IDs from Feature: Azure APIM Connectivity and Scope Discovery), issues an authenticated HttpClient GET against the ARM policy endpoint with format=xml&effective=true and an explicitly pinned api-version, using the ArmTokenProvider for the bearer token, returning the raw XML string. Map ARM 404/403 responses to typed NotFound/Forbidden results with descriptive messages. Out of scope: XML parsing into a graph (next tasks).",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["APIM-2", "APIM-3"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/EffectivePolicyClient.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/EffectivePolicyClientTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~EffectivePolicyClientTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/effective-policy-flow-model.md#POL-FR-01"],
    "acceptanceCriteria": [
      "Request URL for each scope kind includes format=xml&effective=true and the pinned api-version",
      "ARM 404 maps to a typed NotFound result with a descriptive message",
      "ARM 403 maps to a typed Forbidden result with a descriptive message"
    ],
    "constraints": [],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-02"],
    "references": ["docs/PRD.md#5. Research Findings"]
  }
}
```

```forge-task
{
  "id": "POL-2",
  "title": "Parse linear policy sections into ordered step graph",
  "description": "Implement PolicyFlowParser that parses the inbound, backend, outbound, and on-error sections of effective policy XML into ordered step nodes, one node per top-level policy statement, tagged with its owning section and preserving document order. Non-control-flow elements (e.g. set-header, rate-limit, set-variable, base) become simple step nodes labeled with the element name and key attributes. Do not implement choose/when/otherwise/retry branching yet (next task); unrecognized elements still become a generic labeled step node rather than failing parsing. Never evaluate expression text (VIS-SEC-03); capture it verbatim as node display data.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["POL-1"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/PolicyFlowParser.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicyFlowParserTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~PolicyFlowParserTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/effective-policy-flow-model.md#POL-FR-02"],
    "acceptanceCriteria": [
      "A sample policy with set-header, rate-limit, and base elements across all four sections produces one ordered step node per element, tagged with its section, in source document order",
      "An unrecognized policy element produces a generic labeled step node instead of a parse failure",
      "Expression text inside @(...) is captured verbatim on the node and never evaluated"
    ],
    "constraints": [],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-03"],
    "references": ["docs/features/effective-policy-flow-model.md#3. Functional Requirements"]
  }
}
```

```forge-task
{
  "id": "POL-3",
  "title": "Parse branching, retry, and error-path control flow",
  "description": "Extend the flow graph with PolicyControlFlowParser, which post-processes the step nodes from POL-2 to represent <choose>/<when>/<otherwise> as branch nodes with labeled conditional edges (condition attribute text as the edge label, verbatim and unevaluated), and <retry> plus on-error content as explicit loop-back/error-path edges, preserving nesting depth for nested choose/retry blocks. Do not change the linear step parsing behavior established in POL-2.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["POL-2"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/PolicyControlFlowParser.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicyControlFlowParserTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~PolicyControlFlowParserTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/effective-policy-flow-model.md#POL-FR-03", "docs/features/effective-policy-flow-model.md#POL-FR-05"],
    "acceptanceCriteria": [
      "A choose/when/otherwise sample produces one branch node with one labeled conditional edge per when clause plus an otherwise edge",
      "A nested choose inside a when clause preserves correct nesting depth in the resulting graph",
      "A retry block and an on-error block each produce an explicit loop-back/error-path edge distinguishable from normal sequential edges",
      "Condition attribute text is captured verbatim on edges and never evaluated"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/features/effective-policy-flow-model.md#3. Functional Requirements"]
  }
}
```

```forge-task
{
  "id": "POL-4",
  "title": "Expose effective policy flow graph HTTP endpoint",
  "description": "Implement PolicyFlowController exposing GET /api/policy/effective-flow?scope={scopeId}, wiring EffectivePolicyClient (POL-1) and PolicyFlowParser + PolicyControlFlowParser (POL-2, POL-3) together: resolve the scope via the Feature: Azure APIM Connectivity and Scope Discovery scope catalog, fetch the effective XML, parse it, and return the combined graph (nodes, edges, section metadata) as JSON. Return HTTP 400 for an unknown scopeId, and propagate 404/403 from the underlying client. Out of scope: any frontend rendering.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["POL-3", "APIM-3"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/PolicyFlowController.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicyFlowControllerTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~PolicyFlowControllerTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/effective-policy-flow-model.md#POL-FR-04"],
    "acceptanceCriteria": [
      "GET /api/policy/effective-flow?scope=<valid> returns 200 with a JSON graph containing nodes, edges, and section metadata for a mocked pipeline",
      "An unknown scopeId returns HTTP 400 with a descriptive message",
      "A 404/403 from EffectivePolicyClient propagates as the same status with its descriptive message"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/PRD.md#6.3 Key APIs / Interfaces"]
  }
}
```

### Phase 2: Live Integration

```forge-task
{
  "id": "POL-5",
  "title": "Live smoke test against the configured Azure APIM instance",
  "description": "Add tests/smoke/live-smoke.mjs, a Node script that reuses a healthy backend at SMOKE_BASE_URL (default http://localhost:5021) or starts one with the http launch profile and stops it afterwards, then calls GET /api/scopes and GET /api/policy/effective-flow for one Global, Product, API and Operation scope against the real APIM instance. It fails unless every call returns HTTP 200, every graph has at least one node, and no edge label contains XML entity text, and prints credential/configuration hints on failure. Requires `az login` with read access to the APIM service and Apim__ServiceName (defaults per APIM-FR-02); on Azure VMs the http profile's AZURE_TOKEN_CREDENTIALS=dev keeps DefaultAzureCredential on the CLI login. Out of scope: CI execution without credentials.",
  "ownerAgent": "qa-test-engineer",
  "dependencies": ["POL-4", "APIM-3"],
  "expectedOutputs": [
    "tests/smoke/live-smoke.mjs"
  ],
  "validationCommands": [
    "node tests/smoke/live-smoke.mjs"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/effective-policy-flow-model.md#POL-FR-01",
      "docs/features/effective-policy-flow-model.md#POL-FR-04",
      "docs/features/apim-connectivity-scope.md#APIM-FR-01",
      "docs/features/apim-connectivity-scope.md#APIM-FR-03",
      "docs/features/effective-policy-flow-model.md#POL-FR-05"
    ],
    "acceptanceCriteria": [
      "Against live Azure APIM, GET /api/scopes and GET /api/policy/effective-flow for one Global, Product, API and Operation scope each return HTTP 200 with at least one node",
      "No returned edge label contains XML entity text (&quot;, &gt;, &lt;, &amp;, &apos;)",
      "A misconfigured service or credential makes the script exit non-zero with the backend's error detail",
      "A backend started by the script is stopped when the script exits"
    ],
    "constraints": ["Never evaluate policy expression text; only compare labels as strings"],
    "constraintRefs": [],
    "references": ["docs/PRD.md#6.3 Key APIs / Interfaces"]
  }
}
```

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit Tests | EffectivePolicyClient URL/error mapping, PolicyFlowParser/PolicyControlFlowParser graph construction | xUnit with sample policy XML fixtures |
| Integration Tests | `/api/policy/effective-flow` end-to-end with mocked ARM responses | ASP.NET Core `WebApplicationFactory` |
| Live Smoke | `/api/scopes` and `/api/policy/effective-flow` against the real APIM instance (Task POL-5) | `node tests/smoke/live-smoke.mjs` with `az login` |

Key test scenarios:
1. Linear sections produce correctly ordered, section-tagged step nodes.
2. Nested `choose`/`when`/`otherwise`, `retry`, and `on-error` produce
   correctly labeled branch/loop/error edges without evaluating any
   expression text.
3. The endpoint surfaces 400/403/404 correctly and returns a complete
   graph for a valid scope.
4. Live ARM accepts the pinned api-version and policy format, and the
   runtime credential can read the service; mocked handlers cannot prove
   any of these.

---

## 7. Acceptance Criteria

1. For any valid scope, `GET /api/policy/effective-flow` returns a JSON
   graph whose step count and branch structure match the source effective
   policy XML.
2. No policy expression content is ever executed or evaluated by the
   backend.
3. All tasks' validation commands pass with at least one test executed
   each.
4. The live smoke test (POL-5) passes against the configured APIM instance.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Which ARM api-version should be pinned for the effective-policy REST call? | `2024-05-01` (GA). The originally assumed `2022-08-01-preview` is not a registered `Microsoft.ApiManagement/service` api-version and fails with HTTP 400 `NoRegisteredProviderFound`; `2024-05-01` was verified live to resolve `<base />` when `effective=true`. |
