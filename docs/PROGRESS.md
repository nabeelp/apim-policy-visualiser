# Project Progress

## Current State
**Phase**: SEMANTIC-POLICY-FLOW-VISUALIZATION acceptance
**Status**: Paused (human review SPF-18 required; SPF-1–SPF-17 implemented and validated)
**Last Updated**: 2026-09-26
**Run ID**: 9ff8f541-dedf-43e8-9543-ad9d6c4aa800
**Harness**: copilot
**Execution Mode**: auto

## Completed Tasks
- [x] Phase AZURE-APIM-CONNECTIVITY-AND-SCOPE-DISCOVERY-1, Task APIM-1: Scaffold ASP.NET Core Web API host with APIM configuration (@apim-connectivity-engineer)
  - Files: src/server/ApimPolicyVisualizer.Api/ApimPolicyVisualizer.Api.csproj, src/server/ApimPolicyVisualizer.Api/Program.cs, src/server/ApimPolicyVisualizer.Api/appsettings.json, src/server/ApimPolicyVisualizer.Api/Configuration/ApimOptions.cs, tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj, tests/ApimPolicyVisualizer.Api.Tests/HealthEndpointTests.cs, .gitignore, src/server/ApimPolicyVisualizer.Api/Properties/launchSettings.json, src/server/ApimPolicyVisualizer.Api/appsettings.Development.json, tests/ApimPolicyVisualizer.Api.Tests/test.runsettings
- [x] Phase AZURE-APIM-CONNECTIVITY-AND-SCOPE-DISCOVERY-1, Task APIM-2: Implement DefaultAzureCredential ARM token provider (@apim-connectivity-engineer)
  - Files: src/server/ApimPolicyVisualizer.Api/Azure/ArmTokenProvider.cs, tests/ApimPolicyVisualizer.Api.Tests/ArmTokenProviderTests.cs, src/server/ApimPolicyVisualizer.Api/ApimPolicyVisualizer.Api.csproj, src/server/ApimPolicyVisualizer.Api/Program.cs, tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj
- [x] Phase AZURE-APIM-CONNECTIVITY-AND-SCOPE-DISCOVERY-1, Task APIM-3: Implement APIM scope discovery service and endpoint (@apim-connectivity-engineer)
  - Files: src/server/ApimPolicyVisualizer.Api/Scopes/ScopeDiscoveryService.cs, src/server/ApimPolicyVisualizer.Api/Scopes/ScopesController.cs, tests/ApimPolicyVisualizer.Api.Tests/ScopeDiscoveryServiceTests.cs, src/server/ApimPolicyVisualizer.Api/ApimPolicyVisualizer.Api.csproj, src/server/ApimPolicyVisualizer.Api/Configuration/ApimOptions.cs, src/server/ApimPolicyVisualizer.Api/Program.cs, src/server/ApimPolicyVisualizer.Api/Azure/ArmResult.cs, src/server/ApimPolicyVisualizer.Api/Azure/ArmTokenProviderCredential.cs, src/server/ApimPolicyVisualizer.Api/Scopes/ArmApimCatalogClient.cs, src/server/ApimPolicyVisualizer.Api/Scopes/ResourceGraphApimServiceLocator.cs
- [x] Phase EFFECTIVE-POLICY-RETRIEVAL-AND-FLOW-GRAPH-MODEL-1, Task POL-1: Implement effective policy XML retrieval client (@policy-parsing-engineer)
  - Files: src/server/ApimPolicyVisualizer.Api/Policy/EffectivePolicyClient.cs, tests/ApimPolicyVisualizer.Api.Tests/EffectivePolicyClientTests.cs, src/server/ApimPolicyVisualizer.Api/Program.cs
- [x] Phase EFFECTIVE-POLICY-RETRIEVAL-AND-FLOW-GRAPH-MODEL-1, Task POL-2: Parse linear policy sections into ordered step graph (@policy-parsing-engineer)
  - Files: src/server/ApimPolicyVisualizer.Api/Policy/PolicyFlowParser.cs, tests/ApimPolicyVisualizer.Api.Tests/PolicyFlowParserTests.cs, src/server/ApimPolicyVisualizer.Api/Program.cs
- [x] Phase EFFECTIVE-POLICY-RETRIEVAL-AND-FLOW-GRAPH-MODEL-1, Task POL-3: Parse branching, retry, and error-path control flow (@policy-parsing-engineer)
  - Files: src/server/ApimPolicyVisualizer.Api/Policy/PolicyControlFlowParser.cs, tests/ApimPolicyVisualizer.Api.Tests/PolicyControlFlowParserTests.cs, src/server/ApimPolicyVisualizer.Api/Policy/PolicyFlowParser.cs, src/server/ApimPolicyVisualizer.Api/Program.cs
- [x] Phase EFFECTIVE-POLICY-RETRIEVAL-AND-FLOW-GRAPH-MODEL-1, Task POL-4: Expose effective policy flow graph HTTP endpoint (@policy-parsing-engineer)
  - Files: src/server/ApimPolicyVisualizer.Api/Policy/PolicyFlowController.cs, tests/ApimPolicyVisualizer.Api.Tests/PolicyFlowControllerTests.cs
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI-1, Task UI-1: Scaffold React + Vite frontend with API client and layout shell (@frontend-visualization-engineer)
  - Files: src/web/package.json, src/web/vite.config.ts, src/web/src/main.tsx, src/web/src/App.tsx, src/web/src/api/client.ts, src/web/src/App.test.tsx, src/web/index.html, src/web/package-lock.json, src/web/src/styles.css, src/web/src/test/setup.ts, src/web/src/vite-env.d.ts, src/web/tsconfig.json
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI-1, Task UI-2: Implement cascading scope selector (@frontend-visualization-engineer)
  - Files: src/web/src/components/ScopeSelector.tsx, src/web/src/components/ScopeSelector.test.tsx
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI-2, Task UI-3: Render effective policy flow diagram (@frontend-visualization-engineer)
  - Files: src/web/src/components/PolicyFlowDiagram.tsx, src/web/src/components/PolicyFlowDiagram.test.tsx, src/web/package-lock.json, src/web/package.json
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI-2, Task UI-4: Apply desktop visual theme and automated accessibility checks (@frontend-visualization-engineer)
  - Files: src/web/src/styles/theme.css, src/web/src/components/PolicyFlowDiagram.a11y.test.tsx, src/web/package-lock.json, src/web/package.json, src/web/src/components/PolicyFlowDiagram.tsx, src/web/src/components/ScopeSelector.tsx, src/web/src/main.tsx
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI-2, Task UI-6: Wire scope selector and flow diagram into the App shell (@frontend-visualization-engineer)
  - Files: src/web/src/App.tsx, src/web/src/App.integration.test.tsx
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI acceptance: Complete and harden UI-1/UI-2/UI-3/UI-4/UI-6 (@frontend-visualization-engineer)
  - Manual sidebar collapse, focused return from loading/errors/diagram, cascading selection/reset, backend problem details, cancelled/stale request protection.
  - Preserve parser preorder within sections (nested `order` values are block-local); distinguish four section colors and sequential/conditional/retry/error edge styles.
  - Initial and control-triggered fit both support the full 350-node acceptance graph; mouse pan/zoom enabled, pinch zoom disabled.
  - Modified files: `src/web/src/App.tsx`, `src/web/src/api/client.ts`, `src/web/src/components/ScopeSelector.tsx`, `src/web/src/components/PolicyFlowDiagram.tsx`, `src/web/src/styles/theme.css`, and their existing tests. Added `src/web/src/api/client.test.ts`.
  - Frontend: 22 tests across 6 files passed; TypeScript and Vite production build passed. After clarifying the mocked large-response test (not a browser performance test), all 9 diagram tests passed again.
  - Backend contract regression: all 13 `PolicyFlowControllerTests` passed.
  - Canonical PRD validator: 27 tasks, zero errors/warnings. Editor diagnostics and CRLF-aware diff whitespace checks passed.
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI acceptance: Verify built UI against the real-parser synthetic host
  - `node tests\smoke\ui-fixture-smoke.mjs` passed: four scope levels, graph integrity, all edge kinds, 403/404/422 backend details and built UI hosting.
  - Chromium at 1280x900 and 1440x900: exact 204800-byte synthetic XML, 350 nodes and 350 edges, selection-to-render including two animation frames: **373.2 ms**, **192.1 ms**, **235.1 ms**. Every node fit inside the viewport on initial render and after Zoom In -> Fit View.
  - Checked real mouse pan, four computed node colors, four distinct computed edge styles, branch parent-before-child order, cascade clearing, collapse/expand and focused error recovery for all three statuses.
  - Full browser axe scans (including contrast) found zero violations in both selection and four-section diagram views; no horizontal overflow at supported widths.
  - This measures local parsing/HTTP/rendering, not a 200 KB Azure ARM round trip. No live policy was created or changed to manufacture a performance fixture.
- [x] Phase POLICY-FLOW-VISUALIZATION-WEB-UI acceptance: Validate the live Azure scope-to-graph journey
  - `node tests\smoke\live-smoke.mjs`: Global, Product, API and Operation all returned non-empty graphs.
  - Live Chromium browser at 1280x900: Global `global` (1 node), API `apis/agentra-evaluation-documents` (20 nodes), Operation `apis/agentra-evaluation-documents/operations/analyze-invoice` (20 nodes).
  - Automated browser axe scans of selection and diagram views: zero violations, including color contrast.
  - This is automated live evidence, not a human UI-5 approval.

- [x] Feature 4 re-authored: retired unimplemented Policy Flow Visualizer Enhancements (`ENH-*`) and replaced it with [Semantic Policy Flow Visualization](features/semantic-policy-flow-visualization.md) (`SPF-*`); amended `VIS-SEC-03`/`POL-FR-05` for syntax-only expression parsing; reconciled agents (`policy-parsing-engineer`, `frontend-visualization-engineer`, `qa-test-engineer`, `project-architect`) and skills (retired `apim-display-text-lenient-parsing`, `sanitized-diagram-export`; rewrote `policy-xml-flow-graph-modeling`; added `roslyn-syntax-only-expression-analysis`, `progressive-disclosure-port-preservation`, `elk-compound-lane-layout`).
- [x] Phase SEMANTIC-POLICY-FLOW-VISUALIZATION-1, Tasks SPF-1–SPF-7: source-mapped loader, fragment regions, Roslyn syntax-only expression analyzer, schemaVersion 2 model builder, APIM rule table, state analyzer, rewritten endpoint; legacy `PolicyFlowParser`/`PolicyControlFlowParser` and their tests deleted (@policy-parsing-engineer)
- [x] Phase SEMANTIC-POLICY-FLOW-VISUALIZATION-2, Tasks SPF-8–SPF-14: outcome-preserving projection, ELK lane layout (Inbound → Backend → Outbound columns, On-error lane underneath), visual language and legend, in-place expand/collapse view wired into `App.tsx`, inspector, search, accessibility; legacy `PolicyFlowDiagram` deleted (@frontend-visualization-engineer)
- [x] Phase SEMANTIC-POLICY-FLOW-VISUALIZATION-3, Tasks SPF-15–SPF-17: semantic acceptance + backend performance suite, fixture host + browser smoke (`semantic-flow-browser-smoke.mjs`), live reference-scope smoke (@qa-test-engineer)
  - Results: [Verification record](features/semantic-policy-flow-visualization.md#verification-record-2026-09-26).

## Current Task
- [ ] SPF-18: Human review of semantic fidelity and visual clarity against the live backend (`docs/reviews/semantic-policy-flow-review.json`).

## Remaining
- [ ] SPF-18: Human fidelity/visual review (reference scope `apis/universal-llm-api`).
- [ ] UI-5: Superseded by SPF-18 for the replaced diagram; left pending rather than approved (see the feature's Open Question 1).

## Blockers
- SPF-18 and UI-5 require an actual human reviewer; automated browser and live smoke evidence is not approval.

## Notes
- Workflow engine run 9ff8f541-dedf-43e8-9543-ad9d6c4aa800
- Harness: copilot
- The run ID above is historical; the 2026-09-25 UI completion and acceptance checks were performed directly, without replaying completed backend work or starting Feature 4.
- Reproducible commands and synthetic acceptance procedure: [Policy Flow Visualization Web UI](features/policy-flow-visualization-ui.md#9-running-and-verifying-the-ui).
- New acceptance files: `tests/smoke/UiFixtureHost/UiFixtureHost.csproj`, `tests/smoke/UiFixtureHost/Program.cs`, `tests/smoke/ui-fixture-smoke.mjs`. The isolated host uses the real controllers/parsers with synthetic Azure results; no production backend endpoint was added.
