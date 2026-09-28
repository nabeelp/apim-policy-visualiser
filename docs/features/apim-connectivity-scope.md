# Feature: Azure APIM Connectivity and Scope Discovery

## Traceability

| Canonical ID | Owner / Source Link | Relationship |
|--------------|---------------------|--------------|
| VIS-SEC-01 | [Vision](../PRD.md#8-security-and-privacy) | participates |
| VIS-SEC-02 | [Vision](../PRD.md#8-security-and-privacy) | participates |
| APIM-US-01 | This feature | owns |
| APIM-US-02 | This feature | owns |
| APIM-FR-01 | This feature | owns |
| APIM-FR-02 | This feature | owns |
| APIM-FR-03 | This feature | owns |

**PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** Azure APIM Connectivity and Scope Discovery

**ID Prefix:** APIM

**Summary:** Establishes the ASP.NET Core Web API host, authenticates to
Azure Resource Manager using `DefaultAzureCredential` (backed by the local
`az CLI` login context), resolves the configured target APIM instance, and
exposes an endpoint that lists the Global/Product/API/Operation hierarchy
so the UI can drive scope selection.

**Dependencies:** None (foundation feature).

**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|--------------|------------|----------|
| APIM-US-01 | platform engineer | configure the target APIM instance (subscription, resource group, service name — defaulted to `apim-zlyway6g7icoy`) | I can point the tool at any environment I have access to | Must |
| APIM-US-02 | platform engineer | browse the product/API/operation hierarchy of the configured APIM instance | I can pick the exact scope whose effective policy I want to inspect | Must |

```forge-requirement
{"id":"APIM-US-01","kind":"story","text":"As a platform engineer, I want to configure the target APIM instance (subscription, resource group, service name defaulted to apim-zlyway6g7icoy) so I can point the tool at any environment I have access to."}
```

```forge-requirement
{"id":"APIM-US-02","kind":"story","text":"As a platform engineer, I want to browse the product/API/operation hierarchy of the configured APIM instance so I can pick the exact scope whose effective policy I want to inspect."}
```

---

## 3. Functional Requirements

```forge-requirement
{"id":"APIM-FR-01","kind":"requirement","text":"The backend authenticates to Azure Resource Manager using Azure.Identity DefaultAzureCredential, honoring the current az CLI login context in local development, with no client secrets or connection strings containing credentials in configuration."}
```

```forge-requirement
{"id":"APIM-FR-02","kind":"requirement","text":"The backend resolves the target APIM service from configuration (subscriptionId, resourceGroupName, serviceName), defaulting serviceName to \"apim-zlyway6g7icoy\"; when subscriptionId/resourceGroupName are not explicitly configured, it resolves them via an Azure Resource Graph query scoped to subscriptions visible to the current credential, matching by service name, and returns a clear 4xx error describing the failure if the service cannot be found or the caller lacks read permission."}
```

```forge-requirement
{"id":"APIM-FR-03","kind":"requirement","text":"The backend exposes GET /api/scopes returning a Global scope entry plus the full list of products, and per-product/per-API operations, for the configured APIM instance, to drive dynamic scope selection in the UI."}
```

| ID | Priority |
|----|----------|
| APIM-FR-01 | Must |
| APIM-FR-02 | Must |
| APIM-FR-03 | Must |

---

## 4. UI / Interaction Design

No UI in this feature. The `GET /api/scopes` response is consumed by the
scope selector implemented in
[Policy Flow Visualization Web UI](policy-flow-visualization-ui.md).

---

## 5. Implementation Tasks

### Phase 1: Backend Foundation and Scope Discovery

```forge-task
{
  "id": "APIM-1",
  "title": "Scaffold ASP.NET Core Web API host with APIM configuration",
  "description": "Create the ApimPolicyVisualizer.Api ASP.NET Core Web API project targeting .NET 10, with a strongly-typed ApimOptions configuration section (subscriptionId, resourceGroupName, serviceName) bound from appsettings.json, serviceName defaulting to \"apim-zlyway6g7icoy\" when unset. Add a GET /healthz endpoint returning 200 with the resolved (non-secret) configuration summary. Create the xUnit test project and its first test verifying /healthz returns 200 and the default serviceName. Out of scope: Azure authentication and scope discovery (later tasks).",
  "ownerAgent": "apim-connectivity-engineer",
  "dependencies": [],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/ApimPolicyVisualizer.Api.csproj",
    "src/server/ApimPolicyVisualizer.Api/Program.cs",
    "src/server/ApimPolicyVisualizer.Api/appsettings.json",
    "src/server/ApimPolicyVisualizer.Api/Configuration/ApimOptions.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj",
    "tests/ApimPolicyVisualizer.Api.Tests/HealthEndpointTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~HealthEndpointTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/apim-connectivity-scope.md#APIM-FR-02"],
    "acceptanceCriteria": [
      "GET /healthz returns HTTP 200",
      "Default serviceName resolves to apim-zlyway6g7icoy when not configured",
      "dotnet test discovers and runs at least one HealthEndpointTests test and fails the build if zero tests are selected"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/PRD.md#6. Technical Architecture"]
  }
}
```

```forge-task
{
  "id": "APIM-2",
  "title": "Implement DefaultAzureCredential ARM token provider",
  "description": "Implement an ArmTokenProvider using Azure.Identity DefaultAzureCredential that acquires bearer tokens scoped to https://management.azure.com/.default, cached until near expiry. Validate against the local az CLI login context (integration test skipped/inconclusive when az CLI is not logged in, but unit-testable token-cache expiry logic must be covered without a live Azure call). Do not store any credential or token in configuration, logs, or persistent storage. Out of scope: resource discovery (next task).",
  "ownerAgent": "apim-connectivity-engineer",
  "dependencies": ["APIM-1"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Azure/ArmTokenProvider.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/ArmTokenProviderTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~ArmTokenProviderTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/apim-connectivity-scope.md#APIM-FR-01"],
    "acceptanceCriteria": [
      "Token cache reuses a cached token until it is within 5 minutes of expiry, then refreshes",
      "No credential, secret, or token value is written to logs (verified by test asserting log output does not contain the raw token)"
    ],
    "constraints": [],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-01"],
    "references": ["docs/PRD.md#8. Security and Privacy"]
  }
}
```

```forge-task
{
  "id": "APIM-3",
  "title": "Implement APIM scope discovery service and endpoint",
  "description": "Implement ScopeDiscoveryService using Azure.ResourceManager.ApiManagement to resolve the configured APIM service (auto-resolving subscriptionId/resourceGroupName via Azure Resource Graph when not explicitly configured, matching by serviceName) and list its products, and per-product/per-API operations. Expose GET /api/scopes returning a Global entry plus the discovered hierarchy as JSON, using the ArmTokenProvider from APIM-2 for authentication. Return HTTP 404 with a descriptive message when the service cannot be found, and HTTP 403 with a descriptive message on authorization failure. Out of scope: effective policy retrieval (Feature: Effective Policy Retrieval and Flow Graph Model).",
  "ownerAgent": "apim-connectivity-engineer",
  "dependencies": ["APIM-2"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Scopes/ScopeDiscoveryService.cs",
    "src/server/ApimPolicyVisualizer.Api/Scopes/ScopesController.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/ScopeDiscoveryServiceTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~ScopeDiscoveryServiceTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/apim-connectivity-scope.md#APIM-FR-02", "docs/features/apim-connectivity-scope.md#APIM-FR-03"],
    "acceptanceCriteria": [
      "GET /api/scopes returns a Global entry plus products, and operations per API, for a mocked APIM client",
      "Service-not-found returns HTTP 404 with a descriptive error body",
      "Authorization failure returns HTTP 403 with a descriptive error body"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/PRD.md#6.3 Key APIs / Interfaces"]
  }
}
```

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit Tests | ApimOptions defaults, ArmTokenProvider caching, ScopeDiscoveryService mapping | xUnit + NSubstitute mocking `Azure.ResourceManager.ApiManagement` and `TokenCredential` |
| Integration Tests | `/healthz` and `/api/scopes` endpoints | ASP.NET Core `WebApplicationFactory` with mocked Azure clients |

Key test scenarios:
1. Default `serviceName` resolves to `apim-zlyway6g7icoy` when unset.
2. Token provider reuses cached tokens and refreshes near expiry without
   ever logging the raw token.
3. `/api/scopes` returns Global + products + operations for a mocked
   hierarchy, and returns 404/403 on lookup/authorization failures.

---

## 7. Acceptance Criteria

1. The backend starts, resolves the configured APIM instance (or a clear
   error), and serves `/healthz`.
2. `GET /api/scopes` returns the full Global/Product/API/Operation
   hierarchy for the configured instance without requiring any stored
   credential.
3. All tasks' validation commands pass with at least one test executed
   each.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Should subscription/resource-group auto-resolution cache its result across requests? | Yes — resolved once at startup (or first request) and cached in memory for the process lifetime; explicit config values always take precedence and skip resolution. |
