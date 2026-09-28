# Feature: Policy Flow Visualization Web UI

## Traceability

| Canonical ID | Owner / Source Link | Relationship |
|--------------|---------------------|--------------|
| VIS-CONSTRAINT-01 | [Vision](../PRD.md#8-security-and-privacy) | participates |
| VIS-A11Y-01 | [Vision](../PRD.md#9-accessibility) | participates |
| APIM-FR-03 | [Scope Discovery](apim-connectivity-scope.md#APIM-FR-03) | consumes (scope selector) |
| POL-FR-04 | [Effective Policy Retrieval and Flow Graph Model](effective-policy-flow-model.md#POL-FR-04) | consumes |
| UI-US-01 | This feature | owns |
| UI-US-02 | This feature | owns |
| UI-US-03 | This feature | owns |
| UI-FR-01 | This feature | owns |
| UI-FR-02 | This feature | owns |
| UI-FR-03 | This feature | owns |
| UI-FR-04 | This feature | owns |

**PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** Policy Flow Visualization Web UI

**ID Prefix:** UI

**Summary:** A React + TypeScript desktop web application that lets a
user select a policy scope (via
[Azure APIM Connectivity and Scope Discovery](apim-connectivity-scope.md))
and renders the resulting effective policy
([Effective Policy Retrieval and Flow Graph Model](effective-policy-flow-model.md))
as a polished, interactive flow diagram.

**Dependencies:** Azure APIM Connectivity and Scope Discovery, Effective
Policy Retrieval and Flow Graph Model

**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|--------------|------------|----------|
| UI-US-01 | platform engineer | pick a scope from a hierarchical Global/Product/API/Operation selector | I can choose exactly what to visualize | Must |
| UI-US-02 | platform engineer | see the effective policy as an interactive flow diagram with distinct styling for branches, retries, and errors | I can quickly understand control flow without reading raw XML | Must |
| UI-US-03 | platform engineer | use a site that looks polished and professional on desktop | it is pleasant to use during demos and reviews | Should |

```forge-requirement
{"id":"UI-US-01","kind":"story","text":"As a platform engineer, I want to pick a scope from a hierarchical Global/Product/API/Operation selector so I can choose exactly what to visualize."}
```

```forge-requirement
{"id":"UI-US-02","kind":"story","text":"As a platform engineer, I want to see the effective policy as an interactive flow diagram with distinct styling for branches, retries, and errors so I can quickly understand control flow without reading raw XML."}
```

```forge-requirement
{"id":"UI-US-03","kind":"story","text":"As a platform engineer, I want to use a site that looks polished and professional on desktop so it is pleasant to use during demos and reviews."}
```

---

## 3. Functional Requirements

```forge-requirement
{"id":"UI-FR-01","kind":"requirement","text":"The scope selector consumes GET /api/scopes and lets the user cascade through Global, then Product, then API, then Operation, always allowing selection at any level (Global/Product/API/Operation) since effective policy can be calculated at each."}
```

```forge-requirement
{"id":"UI-FR-02","kind":"requirement","text":"The diagram view renders the GET /api/policy/effective-flow graph via @xyflow/react with visually distinct node styles per section (inbound/backend/outbound/on-error) and distinct edge styles for sequential, branch-conditional, retry loop-back, and error-path edges, with pan and zoom controls."}
```

```forge-requirement
{"id":"UI-FR-03","kind":"requirement","text":"The layout targets desktop viewports of at least 1280px width only; no mobile breakpoints, touch gestures, or responsive collapse behavior are implemented (see VIS-CONSTRAINT-01)."}
```

```forge-requirement
{"id":"UI-FR-04","kind":"requirement","text":"Loading and error states (permission denied, scope not found, parse failure) are surfaced with a clear, human-readable message sourced from the backend's error response body, with a way to return to scope selection."}
```

| ID | Priority |
|----|----------|
| UI-FR-01 | Must |
| UI-FR-02 | Must |
| UI-FR-03 | Must |
| UI-FR-04 | Should |

---

## 4. UI / Interaction Design

- **Layout:** left-hand collapsible panel with the cascading scope
  selector (Global / Product / API / Operation); main canvas area shows
  the flow diagram for the currently selected scope.
- **Scope selector:** cascading dropdowns/lists; selecting a Product,
  API, or Operation is itself a valid scope to visualize (matching APIM's
  own effective-policy scopes), not just a path to a leaf.
- **Diagram:** distinct node colors/icons per section
  (inbound/backend/outbound/on-error); branch nodes show condition text on
  their outgoing edges; retry/error edges use a visually distinct
  (e.g. dashed red) style; pan/zoom via `@xyflow/react` controls.
- **States:** a top-of-canvas banner communicates loading and error
  states; errors include the backend's descriptive message and a
  "back to scope selection" action.
- **Viewport:** desktop-only, minimum supported width 1280px; no mobile
  layout.

---

## 5. Implementation Tasks

### Phase 1: Web App Foundation and Scope Selection

```forge-task
{
  "id": "UI-1",
  "title": "Scaffold React + Vite frontend with API client and layout shell",
  "description": "Scaffold the src/web React 19 + TypeScript app using Vite, with an API client module wrapping fetch calls to the backend (base URL from a Vite env variable), and a base two-pane layout shell (side panel + canvas). Configure Vitest + React Testing Library. Add a smoke test rendering the App shell. Out of scope: scope selector and diagram content (later tasks).",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["APIM-1"],
  "expectedOutputs": [
    "src/web/package.json",
    "src/web/vite.config.ts",
    "src/web/src/main.tsx",
    "src/web/src/App.tsx",
    "src/web/src/api/client.ts",
    "src/web/src/App.test.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web install",
    "npm --prefix src/web run test -- run src/App.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/policy-flow-visualization-ui.md#UI-FR-03"],
    "acceptanceCriteria": [
      "App shell renders a side panel region and a canvas region",
      "vitest discovers and runs at least one test in App.test.tsx and fails the run if zero tests are selected"
    ],
    "constraints": ["Desktop-only layout; do not add mobile breakpoints (VIS-CONSTRAINT-01)"],
    "constraintRefs": ["docs/PRD.md#VIS-CONSTRAINT-01"],
    "references": ["docs/PRD.md#6.2 Project Structure"]
  }
}
```

```forge-task
{
  "id": "UI-2",
  "title": "Implement cascading scope selector",
  "description": "Implement the ScopeSelector component consuming GET /api/scopes through the API client from UI-1, presenting Global, Product, API, and Operation levels as a cascading selector where any level (not only leaves) can be chosen as the active scope, emitting the selected scopeId to the parent App. Handle the loading and error states for the /api/scopes call itself.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["UI-1", "APIM-3"],
  "expectedOutputs": [
    "src/web/src/components/ScopeSelector.tsx",
    "src/web/src/components/ScopeSelector.test.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/components/ScopeSelector.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/policy-flow-visualization-ui.md#UI-FR-01"],
    "acceptanceCriteria": [
      "Selecting a Product, API, or Operation each independently emits a valid scopeId to the parent",
      "A failed /api/scopes call renders a visible error message instead of an empty/broken selector"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/features/policy-flow-visualization-ui.md#4. UI / Interaction Design"]
  }
}
```

### Phase 2: Flow Diagram Rendering and Visual Polish

```forge-task
{
  "id": "UI-3",
  "title": "Render effective policy flow diagram",
  "description": "Implement PolicyFlowDiagram using @xyflow/react, converting the GET /api/policy/effective-flow response (nodes/edges/section metadata) into React Flow nodes and edges, with per-section node styling (inbound/backend/outbound/on-error) and distinct edge styling for sequential, branch-conditional, retry loop-back, and error-path edges, with pan/zoom controls enabled. Handle loading and error states (permission denied, scope not found, parse failure) per UI-FR-04, including a control to return to scope selection.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["UI-2", "POL-4"],
  "expectedOutputs": [
    "src/web/src/components/PolicyFlowDiagram.tsx",
    "src/web/src/components/PolicyFlowDiagram.test.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/components/PolicyFlowDiagram.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/policy-flow-visualization-ui.md#UI-FR-02", "docs/features/policy-flow-visualization-ui.md#UI-FR-04", "docs/PRD.md#VIS-NFR-01"],
    "acceptanceCriteria": [
      "A sample flow graph with all four sections renders one styled node per section per node in the fixture",
      "Branch, retry, and error edges each render with a visually distinct style from a normal sequential edge",
      "A backend error response renders its descriptive message and a working back-to-selection control",
      "Manual timing check recorded during acceptance testing confirms the fetch-to-render round trip stays under 5s for a ~200 KB sample policy, per VIS-NFR-01"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/features/policy-flow-visualization-ui.md#4. UI / Interaction Design"]
  }
}
```

```forge-task
{
  "id": "UI-4",
  "title": "Apply desktop visual theme and automated accessibility checks",
  "description": "Apply a cohesive visual theme (typography, spacing, color palette) across the App shell, ScopeSelector, and PolicyFlowDiagram, targeting a minimum 1280px desktop viewport with no mobile breakpoints. Add an automated axe-core accessibility test covering the scope-selection and diagram views, asserting zero serious/critical violations.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["UI-3"],
  "expectedOutputs": [
    "src/web/src/styles/theme.css",
    "src/web/src/components/PolicyFlowDiagram.a11y.test.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/components/PolicyFlowDiagram.a11y.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/policy-flow-visualization-ui.md#UI-FR-03", "docs/PRD.md#VIS-A11Y-01"],
    "acceptanceCriteria": [
      "The theme applies consistently to the shell, selector, and diagram at >=1280px width",
      "axe-core reports zero serious or critical violations on the rendered scope-selection and diagram views"
    ],
    "constraints": ["No mobile breakpoints or responsive collapse behavior are introduced"],
    "constraintRefs": [],
    "references": ["docs/PRD.md#9. Accessibility"]
  }
}
```

```forge-task
{
  "id": "UI-6",
  "title": "Wire scope selector and flow diagram into the App shell",
  "description": "Mount ScopeSelector (UI-2) in the App side panel and PolicyFlowDiagram (UI-3) in the canvas of src/web/src/App.tsx, holding the selected scopeId in App state so a selection renders the diagram for that scope and the diagram's back-to-selection control restores the placeholder. Add an App-level integration test that renders the real App with a mocked API client (not mocked components) and drives the selection-to-diagram journey. Out of scope: changes to component internals or styling.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["UI-3"],
  "expectedOutputs": [
    "src/web/src/App.tsx",
    "src/web/src/App.integration.test.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/App.integration.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/policy-flow-visualization-ui.md#UI-FR-01", "docs/features/policy-flow-visualization-ui.md#UI-FR-02"],
    "acceptanceCriteria": [
      "Rendering App and choosing the Global scope requests /api/policy/effective-flow for that scope and renders the diagram region",
      "The diagram's back-to-selection control returns the canvas to the placeholder",
      "App.integration.test.tsx exercises the real App component tree through its entry point and fails the run if zero tests are selected"
    ],
    "constraints": ["Do not mock ScopeSelector or PolicyFlowDiagram in the App integration test"],
    "constraintRefs": [],
    "references": ["docs/features/policy-flow-visualization-ui.md#4. UI / Interaction Design"]
  }
}
```

```forge-task
{
  "id": "UI-5",
  "title": "Human sign-off on visual appeal",
  "description": "A human reviewer runs the app against the live backend (start.ps1), completes the primary journey (select Global, one API and one Operation and confirm each renders its effective policy flow), then inspects the themed app (App shell, scope selector, and policy flow diagram) at desktop widths >=1280px and confirms it meets a subjective bar of being visually polished and professional, recording the scopes exercised and findings in the review notes.",
  "dependencies": ["UI-4", "UI-6", "POL-5"],
  "expectedOutputs": [],
  "validationCommands": [],
  "contract": {
    "version": 2,
    "kind": "human-review",
    "reviewFile": "docs/reviews/ui-visual-appeal.json",
    "requirements": ["UI-US-03: As a platform engineer, I want a polished, professional-looking desktop UI so it is pleasant to use during demos and reviews."],
    "requirementRefs": [],
    "acceptanceCriteria": [
      "Against the live backend, selecting Global, one API and one Operation each renders a non-empty effective policy flow diagram, and the review notes name the scopes exercised",
      "A human reviewer confirms the app looks visually polished and professional at desktop widths >=1280px, or files specific follow-up defects"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/features/policy-flow-visualization-ui.md#4. UI / Interaction Design"]
  }
}
```

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Component Tests | ScopeSelector, PolicyFlowDiagram rendering and interaction | Vitest + React Testing Library |
| App Integration Tests | Real App tree: scope selection drives the diagram (Task UI-6) | Vitest + React Testing Library with a mocked API client only |
| Accessibility Tests | Scope-selection and diagram views | `axe-core` automated scan, zero serious/critical violations |
| Human Review | Primary journey against the live backend, then overall visual appeal | Manual sign-off recorded in `docs/reviews/ui-visual-appeal.json` (Task UI-5) |

Key test scenarios:
1. Scope selection at any hierarchy level emits a valid `scopeId`.
2. The diagram renders section-styled nodes and distinctly styled
   branch/retry/error edges from a fixed sample flow graph.
3. Automated accessibility scan reports zero serious/critical violations.
4. Selecting a scope in the rendered App shows that scope's diagram
   (component tests alone do not prove the App mounts them).

---

## 7. Acceptance Criteria

1. A user can select any Global/Product/API/Operation scope and see its
   effective policy rendered as a diagram within the performance target in
   `VIS-NFR-01`.
2. Branching, retry, and error paths are visually distinguishable from
   normal sequential flow.
3. The UI renders correctly at desktop widths of 1280px and above with no
   mobile-specific behavior implemented.
4. Automated accessibility checks and the human review (live primary
   journey plus visual appeal) both pass.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Should the diagram auto-fit/zoom to the selected scope's graph on load? | Yes — the canvas auto-fits to the full graph on initial render; the user can then pan/zoom freely. |

---

## 9. Running and Verifying the UI

From the repository root (PowerShell):

```powershell
npm --prefix src\web run test -- --run
npm --prefix src\web run build
pwsh -File .\start.ps1
```

Open `http://localhost:5173`. The Vite development server proxies `/api`
to the local backend on port 5021. `VITE_API_BASE_URL` can instead specify
an API base URL at build time; a different origin must permit the browser
origin through CORS. No browser credentials or policy content are stored.
The scope panel collapses only through its explicit button, not at a
viewport breakpoint. Returning from a diagram or error restores scope
selection and keyboard focus.

### Repeatable synthetic acceptance check

The isolated [UiFixtureHost](../../tests/smoke/UiFixtureHost/Program.cs)
serves the built UI and real scope/flow controllers and XML parsers, replacing
only Azure discovery/retrieval with synthetic data. It is a separate test
executable, bound to loopback, not a production endpoint or application mode.
It never reads Azure credentials and does not persist policy content.

After building the frontend, start it in a separate terminal:

```powershell
dotnet run --project tests\smoke\UiFixtureHost\UiFixtureHost.csproj
```

Then run:

```powershell
node tests\smoke\ui-fixture-smoke.mjs
```

> **Superseded diagram.** The `PolicyFlowDiagram` rendering verified in the
> 2026-09-25 record below was replaced by Feature 4,
> [Semantic Policy Flow Visualization](semantic-policy-flow-visualization.md).
> The fixture host now serves the synthetic AI-gateway fixture and a 300 KB
> variant; see that feature's
> [Running and Verifying](semantic-policy-flow-visualization.md#14-running-and-verifying)
> section for the current manual checks and the browser smoke.

The fixture smoke command verifies model integrity, all scope levels,
fixture size, required edge kinds, descriptive HTTP errors, and static UI
hosting. It does **not** substitute HTTP timing for browser render timing.

### Live and human acceptance

```powershell
node tests\smoke\live-smoke.mjs
```

This requires the configured APIM service and a signed-in Azure identity
with read access. It verifies live Global/Product/API/Operation graph
responses without writing policies.

UI-5 remains a **human** decision: run the primary browser journey against
the live backend and record the Global/API/Operation scopes exercised,
desktop viewport, and visual findings in the review evidence. Automated
browser inspection is not human approval. The historical 2026-09-24 review
contains only "Done" and does not establish the expanded live-journey
acceptance criteria or approve subsequent UI changes.

### Verification record (2026-09-25)

- Frontend: 22 tests passed; TypeScript/Vite production build passed.
- Backend HTTP contract: 13 controller tests passed; live smoke passed all
  four scope kinds. Live browser Global/API/Operation journeys rendered
  non-empty graphs.
- Built-browser checks: zero axe violations on selection and diagram
  views, including contrast; correct parent-before-child ordering; four
  section colors and four edge styles; pan/zoom/fit, cascade resets, and
  focused recovery from all three backend error statuses.
- Synthetic 200 KB policy: 350 rendered nodes and 350 rendered edges in
  373.2 ms and 192.1 ms at 1280x900, and 235.1 ms at 1440x900, including
  browser paint scheduling. All nodes fit on load and with the Fit View
  control. These are local parser/HTTP/browser measurements, not live
  Azure latency measurements.
- UI-5 current human sign-off remains pending. Detailed evidence and
  named live scopes are recorded in [Project Progress](../PROGRESS.md).
