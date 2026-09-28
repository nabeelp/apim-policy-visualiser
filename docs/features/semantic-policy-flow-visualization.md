# Feature: Semantic Policy Flow Visualization

## Traceability

| Canonical ID | Owner / Source Link | Relationship |
|--------------|---------------------|--------------|
| VIS-CONSTRAINT-01 | [Vision](../PRD.md#VIS-CONSTRAINT-01) | participates |
| VIS-SEC-02 | [Vision](../PRD.md#VIS-SEC-02) | participates |
| VIS-SEC-03 | [Vision](../PRD.md#VIS-SEC-03) | participates |
| VIS-A11Y-01 | [Vision](../PRD.md#VIS-A11Y-01) | participates |
| POL-FR-01 | [Effective Policy Retrieval and Flow Graph Model](effective-policy-flow-model.md#POL-FR-01) | consumes |
| POL-FR-02 | [Effective Policy Retrieval and Flow Graph Model](effective-policy-flow-model.md#POL-FR-02) | participates (re-implements) |
| POL-FR-03 | [Effective Policy Retrieval and Flow Graph Model](effective-policy-flow-model.md#POL-FR-03) | participates (re-implements) |
| POL-FR-04 | [Effective Policy Retrieval and Flow Graph Model](effective-policy-flow-model.md#POL-FR-04) | participates (new response schema) |
| POL-FR-05 | [Effective Policy Retrieval and Flow Graph Model](effective-policy-flow-model.md#POL-FR-05) | participates |
| UI-FR-01 | [Policy Flow Visualization Web UI](policy-flow-visualization-ui.md#UI-FR-01) | consumes |
| UI-FR-02 | [Policy Flow Visualization Web UI](policy-flow-visualization-ui.md#UI-FR-02) | participates (re-implements) |
| UI-FR-04 | [Policy Flow Visualization Web UI](policy-flow-visualization-ui.md#UI-FR-04) | participates |
| SPF-US-01 | This feature | owns |
| SPF-US-02 | This feature | owns |
| SPF-US-03 | This feature | owns |
| SPF-US-04 | This feature | owns |
| SPF-US-05 | This feature | owns |
| SPF-US-06 | This feature | owns |
| SPF-FR-01 | This feature | owns |
| SPF-FR-02 | This feature | owns |
| SPF-FR-03 | This feature | owns |
| SPF-FR-04 | This feature | owns |
| SPF-FR-05 | This feature | owns |
| SPF-FR-06 | This feature | owns |
| SPF-FR-07 | This feature | owns |
| SPF-FR-08 | This feature | owns |
| SPF-FR-09 | This feature | owns |
| SPF-FR-10 | This feature | owns |
| SPF-FR-11 | This feature | owns |
| SPF-FR-12 | This feature | owns |
| SPF-FR-13 | This feature | owns |
| SPF-FR-14 | This feature | owns |
| SPF-FR-15 | This feature | owns |
| SPF-FR-16 | This feature | owns |
| SPF-FR-17 | This feature | owns |
| SPF-NF-01 | This feature | owns |
| SPF-SEC-01 | This feature | owns |
| SPF-A11Y-01 | This feature | owns |

**PRD:** [docs/PRD.md](../PRD.md)

**Source material:** [docs/research/visualizer-enhancements.md](../research/visualizer-enhancements.md)
is research input, not an execution source. This feature turns its recommendations into
generic, executable requirements for the existing ASP.NET Core + React/`@xyflow/react`
stack. **It replaces** the unimplemented former Feature 4 (*Policy Flow Visualizer
Enhancements*, `ENH-*`), which is retired in full. None of its tasks were started.

---

## 1. Feature Overview

**Feature Name:** Semantic Policy Flow Visualization

**ID Prefix:** SPF

**Parent Document:** [PRD](../PRD.md)

**Status:** Implemented (SPF-1–SPF-17 complete; SPF-18 human review pending)

**Approved visualization views (2026-09-26):** The frontend's view switch
offers three retained views, with **Flow map** as the default. The former
**Detailed graph** option has been removed:
**Flow map** lowers border/edge emphasis and reveals edge labels on hover, keyboard
focus, or selection; **Stage board** presents independently scrollable stage columns;
**Reading view** presents one stage's expandable source hierarchy beside the inspector.
The latter two are explicitly source-order outlines, not execution traces; branches,
loops, and connection payloads remain inspectable in the shared model/inspector.
Error and explicit-response summaries remain on cards, and stage-level connections
and request/response endpoints have disclosures. Search and expansion state are
shared across modes. Canonical execution semantics are unchanged. Reloading returns
to Flow map. Review screenshots use the local synthetic fixture, not live Azure data.

The desktop shell uses a compact single-line application header. Scope context and
view choices share a row above the controls; view guidance is available in each
button's tooltip and accessible description. The workspace fills the remaining
viewport height, with scrolling contained in the scope panel, outlines and inspector.
Browser checks require the header to stay within 48px, the workspace to start within
180px, and at least 75% of viewport height to remain available for visualization at
1440x900 and 1280x720, including with the scope panel open.

**Summary:** Replace the current XML-tree-shaped flow graph and its diagram with a
**hierarchical, semantics-aware execution graph**. The backend builds a source-mapped
execution model from the live ARM effective policy. That model covers fragment-occurrence
groups taken from APIM's `include-fragment: Begin/End` comments, first-match decisions
with explicit merge/no-match/bypass paths, terminal `return-response` nodes, bounded retry
loops, APIM stage and error semantics, CORS preflight, variable state, cache data
dependencies, and Roslyn syntax-only expression analysis. The frontend renders it
left-to-right: Inbound → Backend → Outbound columns with an On-error lane spanning
underneath. Subprocesses expand in place. An inspector shows exact source and fact
provenance, and search is included.

**Scope:** Live ARM effective policy only (`format=xml&effective=true`). The feature is
generic: no policy-specific knowledge or annotation catalogue.

**Excluded:** paste/upload import, lenient display-text parsing, SVG/JSON export,
annotation catalogues, constant propagation or "unreachable branch" claims, and editing.

**Dependencies:** Effective Policy Retrieval and Flow Graph Model, Policy Flow Visualization Web UI

**Priority:** Must

---

## 2. Context: Existing System State

**Completed Feature Tasks:**
- `APIM-1`–`APIM-3`: scope discovery and ARM credential.
- `POL-1`–`POL-4`: effective policy retrieval, linear and control-flow parsers, and the endpoint.
- `UI-1`–`UI-4` and `UI-6`: React shell, scope selector, diagram and wiring.
- `UI-5` (human review of the current UI) is pending. See Open Question 1.

**Relevant Existing Components:**
- Kept unchanged: `Policy/EffectivePolicyClient.cs` (POL-1) and all of `Scopes/` and `Azure/`.
- Replaced: `Policy/PolicyFlowParser.cs` and `Policy/PolicyControlFlowParser.cs`. They
  ignore comments (`IgnoreComments = true`), carry no source spans, model `choose` as
  fan-out without merge/no-match, and draw on-error as one edge from each section's last node.
- Rewritten: `Policy/PolicyFlowController.cs`. It keeps the route and its 400/403/404/422
  behaviour and returns the new schema.
- Replaced: `src/web/src/components/PolicyFlowDiagram.tsx`, which uses fixed column
  positions with no grouping, projection or inspector.
- Updated: `App.tsx` wiring and `styles/theme.css`.
- Kept unchanged: `ScopeSelector.tsx`.

**Verified live input characteristics** (read-only probe of
`apim-zlyway6g7icoy/apis/universal-llm-api`, 2026-09-25, not committed):
- About 181 KB and 1,799 lines of well-formed `format=xml` output.
- 16 fragment occurrences, marked by `<!--include-fragment: Begin {name} policy fragment scope-->`
  and a matching `End`. Begin and End are always siblings under the same parent. Three
  fragments occur twice, including `set-response-headers` in both outbound and on-error.
- A commented-out `<include-fragment>` example.
- 14 `return-response` elements, 1 `retry` in backend whose body contains two fragment
  occurrences, 41 `choose` elements, and 58 `set-variable` elements.
- Multi-line expressions are encoded with `&#xD;&#xA;`.
- `unified-ai-api` is about 275 KB and 2,517 lines.

**Existing Agents Involved:** `policy-parsing-engineer`, `frontend-visualization-engineer`,
`qa-test-engineer`, `project-architect` (constraint amendment and skill retirement).

**Established Conventions:**
- xUnit + NSubstitute backend tests.
- Vitest + React Testing Library + `axe-core` frontend tests.
- Desktop only, ≥1280px (`VIS-CONSTRAINT-01`).
- No persistence (`VIS-SEC-02`).
- Pinned ARM api-version.
- Test commands must select and execute at least one test.

---

## 3. Feature Goals and Non-Goals

### 3.1 Goals
From [Bottom line](../research/visualizer-enhancements.md#bottom-line), the diagram must
make five things immediately obvious:
1. Where a request can terminate early.
2. Which conditions select one path over another, as first-match rather than parallel.
3. What repeats during retries, and what does not.
4. How request state changes: variables written and read, such as model or backend target.
5. Why a backend HTTP error response is not an On-error event, while an execution failure is.

It must also do the following:
- Present a readable overview built from fragment-occurrence groups rather than one box
  per XML element, with in-place expansion down to exact source.
- Mark every statement with its provenance: **structural**, **APIM rule**, **inferred**
  (from expression syntax), or **comment**.

### 3.2 Non-Goals
- Scope discovery and ARM retrieval (`APIM-*`, `POL-1`) do not change.
- No policy expression is ever compiled, evaluated or executed (`VIS-SEC-03`, amended).
- No claim is made that a branch is unreachable. Configuration-dependent branches are
  labelled, never hidden.
- No policy-specific rules. For example, the tool will not "know" alias-fallback
  semantics; it derives what the syntax supports.
- No local import, lenient parsing, export, mobile layout, or editing.

---

## 4. User Stories

```forge-requirement
{"id":"SPF-US-01","kind":"story","text":"As a platform engineer, I want a readable overview of the effective policy with Inbound, Backend and Outbound as sequential lanes and On-error as a separate exception lane, so that I understand its execution model without reading thousands of lines."}
```

```forge-requirement
{"id":"SPF-US-02","kind":"story","text":"As a developer, I want to expand fragment occurrences, decisions and retry loops in place, so that I can inspect internal logic without losing my place in the overall flow."}
```

```forge-requirement
{"id":"SPF-US-03","kind":"story","text":"As a reviewer, I want explicit early responses, backend HTTP error responses and execution failures drawn as different paths, so that I know whether Outbound or On-error runs."}
```

```forge-requirement
{"id":"SPF-US-04","kind":"story","text":"As a developer, I want to see what repeats during retries and which elements write and read request state variables, so that I understand attempts, routing and model selection."}
```

```forge-requirement
{"id":"SPF-US-05","kind":"story","text":"As a developer, I want to select any node or edge and see its exact condition, source lines, variables, comment and whether each statement is structural, an APIM rule, inferred or from a comment, so that I can verify the diagram against the source."}
```

```forge-requirement
{"id":"SPF-US-06","kind":"story","text":"As a user, I want to search by policy, fragment, variable, status code or source text, so that I can find relevant processing quickly."}
```

| ID | Ownership | Priority |
|----|-----------|----------|
| SPF-US-01 | Owns | Must |
| SPF-US-02 | Owns | Must |
| SPF-US-03 | Owns | Must |
| SPF-US-04 | Owns | Must |
| SPF-US-05 | Owns | Must |
| SPF-US-06 | Owns | Should |

---

## 5. Technical Approach

### 5.1 Impact on Existing Architecture
- **Backend pipeline** (per request, in memory only):
  1. `EffectivePolicyClient` (unchanged) returns the policy XML.
  2. `PolicySourceLoader` produces a source-mapped tree that keeps comments.
  3. `FragmentRegionBuilder` identifies fragment-occurrence regions.
  4. `PolicyExpressionAnalyzer` (Roslyn, syntax only) analyses expressions.
  5. `PolicyFlowModelBuilder` builds the structure; `ApimSemanticRules` adds stage, exit
     and CORS rules; `PolicyStateAnalyzer` adds variables, configuration-dependence and
     cache data dependencies.
  6. The controller returns the schemaVersion 2 JSON.
- **Removed:** `PolicyFlowParser.cs`, `PolicyControlFlowParser.cs` and their tests, their
  DI registrations in `Program.cs` and `tests/smoke/UiFixtureHost/Program.cs`, and the
  `PolicyFlowGraph` JSON shape.
- **Frontend pipeline:**
  1. Fetch the model.
  2. `projection.ts` turns the full model plus the expanded-set into visible nodes and
     edges, lifting edges out of collapsed groups while keeping each outcome kind separate.
  3. `layout.ts` runs ELK layered, left-to-right, with compound nodes and ports.
  4. React Flow custom nodes and edges render the result.
  5. Inspector and search sit on top.
- **Removed from the frontend:** `components/PolicyFlowDiagram.tsx` and its tests. The
  diagram section of `theme.css` is replaced.

### 5.2 New Components

| Path | Purpose |
|------|---------|
| `src/server/ApimPolicyVisualizer.Api/Policy/Source/PolicySourceLoader.cs` | `XmlReader` with line info; start/end spans for elements, comments and attributes; keeps comments; decodes ARM expression escaping; DTD prohibited |
| `src/server/ApimPolicyVisualizer.Api/Policy/Source/FragmentRegionBuilder.cs` | Pairs Begin/End marker comments into nested occurrence regions and derives labels from comments |
| `src/server/ApimPolicyVisualizer.Api/Policy/Expressions/PolicyExpressionAnalyzer.cs` | Roslyn syntax-only facts: reads, named values, context members, return paths, catches, key prefixes, condition summaries |
| `src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModel.cs` | schemaVersion 2 contract records |
| `src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModelBuilder.cs` | Structural elements, edges, labels and categories |
| `src/server/ApimPolicyVisualizer.Api/Policy/Model/ApimSemanticRules.cs` | Versioned APIM rule table: stage lanes, exception exits, forward-request, CORS, terminals |
| `src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyStateAnalyzer.cs` | Variable index, configuration-dependent badges, cache data-dependency edges |
| `src/web/src/flow/model.ts` | TypeScript mirror of the schemaVersion 2 contract |
| `src/web/src/flow/projection.ts` | Expansion-state projection that preserves outcomes |
| `src/web/src/flow/layout.ts` | elkjs compound layered layout: left-to-right columns, On-error lane underneath |
| `src/web/src/flow/nodes.tsx`, `edges.tsx`, `Legend.tsx`, `src/web/src/styles/flow.css` | Visual language |
| `src/web/src/flow/PolicyFlowView.tsx` | Composition: fetch, states, canvas, toolbar |
| `src/web/src/flow/Inspector.tsx` | Source detail and provenance panel |
| `src/web/src/flow/search.ts`, `FlowSearch.tsx` | Search index and control |
| `tests/ApimPolicyVisualizer.Api.Tests/Fixtures/ai-gateway-synthetic.xml` | Sanitized synthetic fixture reproducing the reference policy's structure (no customer data) |

**Schema sketch.** Normative content is defined in SPF-FR-12; field names can be refined
during SPF-4.

```text
{ schemaVersion: 2, scopeId, scopeKind,
  source: { text, lineCount },
  stages: [{ id, name, present }],
  elements: [{ id, kind: stage|group|composite|step|decision|clause|merge|loop|loop-test|terminal|entry|opaque,
               stage, parentId, order, label, labelProvenance, tag, category,
               fragment?: { name, occurrenceId, index, count },
               span: { startLine, startColumn, endLine, endColumn },
               attributes[], expressions[{ location, text, span, analysis }],
               comment?, badges[], facts[{ text, provenance, ruleId? }],
               exits: { explicitResponseCodes[], raisesError } }],
  edges: [{ id, from, to, kind, label?, priority?, condition?: { text, summary?, provenance } }],
  variables: [{ name, writers[], readers[] }],
  diagnostics: [{ severity, code, message, span? }] }
```

### 5.3 Technology Additions

| Package | Version (latest stable, checked 2026-09-25) | Notes |
|---------|------------------|-------|
| `Microsoft.CodeAnalysis.CSharp` (NuGet) | 5.9.0 | Uses only `CSharpSyntaxTree.ParseText` / `SyntaxFactory.Parse*` and syntax walkers. Never uses `CSharpCompilation`, `SemanticModel`, `Emit` or `Microsoft.CodeAnalysis.CSharp.Scripting` (enforced by a test). |
| `elkjs` (npm) | 0.12.0 | Bundled `elk.bundled.js`, run in-process; compound layered layout with ports. No CDN. |
| `playwright-core` (npm, `tests/smoke` only) | latest stable at implementation time | Browser smoke test using the locally installed Microsoft Edge (`channel: "msedge"`); no browser download. |

`@xyflow/react` stays at 12.11.6.

---

## 6. Functional Requirements

```forge-requirement
{"id":"SPF-FR-01","kind":"requirement","text":"The backend loads effective policy XML with DTD processing prohibited and no XML resolver, retains comments, restores ARM double-escaped expression text, and records a 1-based start/end line and column span for every element, comment and expression-bearing attribute; the unmodified source text is returned with the model so the UI can slice exact snippets; malformed XML still yields HTTP 422 with the parser message."}
```

```forge-requirement
{"id":"SPF-FR-02","kind":"requirement","text":"Paired sibling comments 'include-fragment: Begin {name} policy fragment scope' and 'include-fragment: End {name} policy fragment scope' define a fragment-occurrence group spanning the siblings between them; each occurrence has a distinct stable ID ({name}#{n} in source order) plus index, total count and stage; repeated occurrences are never merged into one execution node; nested regions nest; unmatched or crossed markers produce a warning diagnostic with span and leave affected elements ungrouped; the group label is the immediately preceding single-line sibling comment with any leading 'Step N:' prefix removed, else the 'Fragment: {title}' line of the first inner comment, else the humanised fragment name, with labelProvenance recorded; comments never create executable elements, including markup inside comments such as a commented-out include-fragment."}
```

```forge-requirement
{"id":"SPF-FR-03","kind":"requirement","text":"Inbound, Backend and Outbound are modeled as sequential stages linked in that order from a 'Request' entry node; On-error is a separate exception lane, never a fourth sequential stage: each present normal stage has exactly one stage-level 'unhandled execution failure' exception edge to the On-error entry when an on-error section exists, or to a 'Gateway default error response' terminal (APIM default 400/500 handling) when it is absent; non-terminating On-error flow ends at an 'Error response to caller' terminal and never resumes Outbound, while a return-response inside On-error reaches the explicit-response terminal; Outbound ends at a 'Final backend response to caller' terminal; absent stages are shown as present:false with no fabricated steps."}
```

```forge-requirement
{"id":"SPF-FR-04","kind":"requirement","text":"Each choose is an ordered first-match decision: one branch edge per when in source order carrying its 1-based priority and verbatim condition, an otherwise edge when present, an explicit 'no match: continue' edge to the merge when otherwise is absent, a bypass edge to the merge for each empty when/otherwise, and a merge node that every non-terminating branch rejoins before the next sibling; branches are never presented as parallel."}
```

```forge-requirement
{"id":"SPF-FR-05","kind":"requirement","text":"Each return-response is a terminal step carrying the status code and reason from its set-status child, or 'status from variable' when response-variable-name is used, or the APIM default '200 OK (no body)' otherwise; it has an explicit-response edge to a single 'Explicit response to caller' terminal (including when it appears inside On-error), never rejoins the normal flow, is not routed through On-error, and has no sequence edge to later siblings; every ancestor group aggregates the distinct explicit-response status codes it contains."}
```

```forge-requirement
{"id":"SPF-FR-06","kind":"requirement","text":"Each retry is a loop container whose body executes once before a synthesized loop-test node representing 'retry condition true AND retry budget remains'; every non-terminating exit of the body connects to the loop test, while explicit-response and raises-error exits do not; the loop-back edge returns from the loop test to the first body step and stays inside the loop container (never re-entering earlier steps or stages); the loop-exit edge resolves to the nearest enclosing continuation (next sibling, else the enclosing block's continuation, next stage or final-response terminal); count, interval, max-interval, delta and first-fast-retry are shown verbatim as facts, with inferred summaries of condition and count when SPF-FR-10 can produce them, and the first attempt is annotated as using state prepared before the loop."}
```

```forge-requirement
{"id":"SPF-FR-07","kind":"requirement","text":"A versioned, predicate-based APIM rule table (tag plus attribute conditions) attaches semantics to recognised policies, each fact carrying a ruleId and provenance apim-rule: forward-request is the primary backend call whose transport failures (timeout, connection) raise an error to On-error, while backend HTTP error responses continue to Outbound unless fail-on-error-status-code is 'true'; send-request raises an error on failure unless ignore-error is 'true', in which case it continues with a null response variable; validate-jwt, validate-azure-ad-token, check-header, ip-filter, rate-limit, rate-limit-by-key, quota, quota-by-key, limit-concurrency, llm-token-limit and azure-openai-token-limit raise an error on failure; validate-content, validate-parameters, validate-headers and validate-status-code raise an error only where their action attributes are 'prevent' (continue for detect/ignore; configuration-dependent when expression-valued); raises-error exits are distinct from explicit responses; set-backend-service is a routing-state update, not a call; cache-lookup-value is state access, not an early response."}
```

```forge-requirement
{"id":"SPF-FR-08","kind":"requirement","text":"Every policy element is classified into exactly one category (backend-call, routing, authentication, validation, transformation, state, cache, observability, cors, inherited, control, unknown); configuration children (value, dimension, audiences, issuers, required-claims, openid-config, header/parameter value children, allowed-origins and similar) are folded into their parent as properties and never emitted as steps; trace, emit-metric, llm-emit-token-metric, azure-openai-emit-token-metric and log-to-eventhub are flagged compact observability steps; any unrecognised element is retained as a visible opaque step with an unresolved-control-effect diagnostic and never dropped."}
```

```forge-requirement
{"id":"SPF-FR-09","kind":"requirement","text":"When a cors policy is present in Inbound, the model adds a separate 'CORS preflight request (if no matching OPTIONS operation)' entry, badged configuration-dependent, that routes to the first applicable cors step and then to a 'Preflight response to caller' terminal, with an apim-rule fact explaining that an explicitly defined matching OPTIONS operation disables this handling; the ordinary request entry path is not drawn through preflight handling, and the cors step still appears in its source position on the ordinary path."}
```

```forge-requirement
{"id":"SPF-FR-10","kind":"requirement","text":"Every @(...) and @{...} expression is parsed with the Roslyn C# parser in syntax-only mode (no compilation, semantic model, emit, scripting or reflection) to extract context.Variables names read (indexer, GetValueOrDefault, ContainsKey, TryGetValue), {{named-value}} references, context request/response/subscription/product/user members read, the ordered top-level if/return paths of block expressions, try/catch presence with the caught fallback value, literal string prefixes of cache keys, and a human-readable summary for conditions built only from comparisons, logical operators, null checks, string Contains/StartsWith/EndsWith/Equals calls and literals; every derived fact is provenance inferred with a source span; anything else or any parse error falls back to an opaque verbatim label plus a diagnostic; a C# return statement or locally caught exception is never represented as pipeline termination or an On-error transfer."}
```

```forge-requirement
{"id":"SPF-FR-11","kind":"requirement","text":"The model includes a variable index mapping each variable to the elements that write it (set-variable name, cache-lookup-value variable-name, send-request response-variable-name, validate-jwt output-token-variable-name and similar) and the elements whose expressions read it, and classifies each decision's inputs into dependency classes propagated through variable writers: configuration (named values, variables with no writer in this policy, context.Api/context.Deployment/context.Product settings) versus runtime (request, response, subscription/user identity and values computed from them); only decisions with a configuration input carry the configuration-dependent badge (plus 'variable not assigned in this policy' when applicable), runtime-only decisions are rendered normally; the tool never claims a branch is unreachable; each cache-store-value and cache-lookup-value pair with equal caching-type and matching literal key prefix is linked by a data-dependency edge labeled as affecting subsequent requests, which is never counted or traversed as control flow."}
```

```forge-requirement
{"id":"SPF-FR-12","kind":"requirement","text":"GET /api/policy/effective-flow?scope={scopeId} returns a schemaVersion 2 JSON model containing scope metadata, source text and line count, stages, a hierarchical element list (id, kind, stage, parentId, order, label, labelProvenance, tag, category, fragment occurrence, span, attributes, expressions with analysis, comment, badges, facts with provenance, aggregated exits), typed edges (sequence, branch, no-match, bypass, merge, loop-back, loop-exit, explicit-response, raises-error, stage-exception, preflight, data-dependency), a variable index and diagnostics; every edge endpoint and parentId resolves to an element; the 400/403/404/422 behaviour of POL-FR-04 is preserved; non-fatal analysis problems are diagnostics, not failures."}
```

```forge-requirement
{"id":"SPF-FR-13","kind":"requirement","text":"The diagram opens on an overview laid out left-to-right with Inbound, Backend and Outbound as columns and an On-error lane spanning underneath, showing the Request entry, the CORS preflight entry when present, each stage's top-level items with fragment occurrences, decisions and retry loops collapsed as double-bordered subprocesses, each run of two or more consecutive ungrouped simple top-level steps collapsed into one composite, and the three caller terminals; any subprocess expands and collapses in place on the same canvas with automatic re-layout that keeps the toggled node in view; toolbar controls provide Collapse to overview, Expand all, Fit view and toggles for data-dependency edges and observability steps."}
```

```forge-requirement
{"id":"SPF-FR-14","kind":"requirement","text":"In every expansion state the visible graph is a quotient projection of the full model: an edge with an endpoint inside a collapsed subprocess is lifted to the outermost collapsed ancestor, and each projected edge retains the IDs and semantic payload (kind, priority, condition, status codes) of its contributing full-model edges; edges merge only when kind, target and payload class are compatible; a collapsed subprocess separately exposes its entry, normal continuation, explicit-response (labeled with aggregated status codes), raises-error and data-dependency outcomes, and a collapsed retry shows its internal loop as a loop badge with the summarized condition and budget; no outcome reachable in the full model disappears, merges with a different kind, or gains a target it cannot reach in the full model."}
```

```forge-requirement
{"id":"SPF-FR-15","kind":"requirement","text":"Nodes and edges use one legend-documented visual language: double-bordered subprocess nodes with an expand/collapse affordance, decision nodes with priority-numbered branch labels, merge dots, solid sequence edges, dashed red exception edges (raises-error and stage-exception), amber explicit-response terminals and edges, dotted data-dependency edges, a distinct loop container with a curved loop-back edge, compact observability nodes, subdued configuration-dependent styling plus badge (never hidden), opaque and diagnostic badges, and occurrence-qualified labels (e.g. 'Set response headers · outbound' versus '· on-error') for repeated fragments; no information is conveyed by colour alone."}
```

```forge-requirement
{"id":"SPF-FR-16","kind":"requirement","text":"Selecting a node or edge opens an inspector showing friendly name, policy tag or fragment name with occurrence, stage/structural path, verbatim condition plus inferred summary when available, source line range with the exact source slice and line numbers, variables read and written (each listing and highlighting its writers/readers on the canvas), the associated comment, badges, diagnostics, and every fact with its provenance (structural, apim-rule, inferred, comment); no statement is displayed without a provenance."}
```

```forge-requirement
{"id":"SPF-FR-17","kind":"requirement","text":"A search control matches element labels, policy tags, fragment names, variable names, HTTP status codes and source text; results show stage and structural path; choosing a result expands its collapsed ancestors, centers and selects it (opening the inspector); search never adds, removes or rewrites model elements or edges."}
```

| ID | Affects Existing | Priority |
|----|------------------|----------|
| SPF-FR-01 | Yes: replaces `PolicyFlowParser.Load` | Must |
| SPF-FR-02 | No | Must |
| SPF-FR-03 | Yes: replaces section error-path edges | Must |
| SPF-FR-04 | Yes: replaces `choose` expansion | Must |
| SPF-FR-05 | No | Must |
| SPF-FR-06 | Yes: replaces `retry` expansion | Must |
| SPF-FR-07 | No | Must |
| SPF-FR-08 | Yes: replaces `KeyAttributes` labelling | Must |
| SPF-FR-09 | No | Should |
| SPF-FR-10 | No | Must |
| SPF-FR-11 | No | Must |
| SPF-FR-12 | Yes: `PolicyFlowController` response schema | Must |
| SPF-FR-13 | Yes: replaces `PolicyFlowDiagram.tsx` | Must |
| SPF-FR-14 | No | Must |
| SPF-FR-15 | Yes: replaces diagram theme | Must |
| SPF-FR-16 | No | Must |
| SPF-FR-17 | No | Should |

---

## 7. Non-Functional Requirements

```forge-requirement
{"id":"SPF-NF-01","kind":"requirement","text":"For a synthetic effective policy of about 300 KB and 2,500 lines on a baseline machine (>=4 logical cores, 8 GB RAM, Chromium-based browser): in-process model building completes within 1 s at p95 excluding ARM latency; response-received-to-overview-painted completes within 2 s at p95 over 20 runs; expanding or collapsing one subprocess resulting in up to 150 visible nodes re-lays-out and paints within 500 ms at p95; search results appear within 200 ms; budgets are never met by dropping elements, edges or outcomes."}
```

```forge-requirement
{"id":"SPF-SEC-01","kind":"constraint","text":"The frontend makes no network requests other than to the local backend API: elkjs, fonts and all assets are bundled locally (no CDN, remote fonts, analytics or LLM calls); policy source text and derived models exist only in request memory and browser tab state; policy text, expressions and variable values are never written to logs."}
```

```forge-requirement
{"id":"SPF-A11Y-01","kind":"requirement","text":"The overview, an expanded subprocess, the inspector and search are keyboard operable (Tab/Shift+Tab reaches every visible node, Enter or Space toggles expansion, Enter selects a node, the inspector lists the selected node's incoming and outgoing edges as focusable items whose activation selects that edge, Escape closes the inspector and restores focus) with accessible names describing each node's kind, label and outcomes, and automated axe-core scans of those four states report zero serious or critical violations."}
```

| ID | Priority |
|----|----------|
| SPF-NF-01 | Should |
| SPF-SEC-01 | Must |
| SPF-A11Y-01 | Should |

---

## 8. Agent Impact Assessment

### 8.1 Existing Agents — Extended Responsibilities

| Agent | New Responsibilities | Modified Boundaries |
|-------|---------------------|-------------------|
| `policy-parsing-engineer` | Builds the source-mapped loader, fragment regions, Roslyn syntax-only analyzer, execution model builder, APIM rule table, state analyzer and the schemaVersion 2 endpoint. Removes the legacy parsers. | Now owns C# expression **syntax** analysis. The boundary moves from "verbatim only" to "parse, never compile or evaluate". No longer bound to the `POL-2`/`POL-3` graph shape. |
| `frontend-visualization-engineer` | Builds the projection engine, ELK layout, visual language, `PolicyFlowView` composition, inspector, search and accessibility. Removes `PolicyFlowDiagram`. | No longer bound to the `UI-3`/`UI-4` per-section colour and edge-style contract. Owns the new legend-documented visual language. |
| `qa-test-engineer` | Semantic acceptance suite, fixture host and browser performance smoke, live reference-scope smoke, and coordination of the fidelity review. | Unchanged boundary: never implements feature code. |
| `project-architect` | Records the `VIS-SEC-03`/`POL-FR-05` amendment and retires former-Feature 4 skills and agent references (see §9.0). | None. |

### 8.2 New Agents Required

None.

### 8.3 Existing Agents — No Changes

| Agent | Reason |
|-------|--------|
| `apim-connectivity-engineer` | Scope discovery, credentials and ARM retrieval are unchanged. |
| `project-orchestrator`, `workflow-orchestrator`, `forge-team-builder` | Coordination and tooling only. |

---

## 9. Implementation Phases

### 9.0 Team and Skill Reconciliation (before execution)

These are authoring steps, not engine tasks.
- Run `forge-build-agent-team` in Feature Increment mode against this document. It
  replaces the `ENH-*` responsibilities in `.github/agents/policy-parsing-engineer.md`,
  `frontend-visualization-engineer.md` and `qa-test-engineer.md` with the `SPF-*` ownership above.
- Run `forge-build-project-skills` in reconciliation mode:
  - Retire `apim-display-text-lenient-parsing` and `sanitized-diagram-export`.
  - Rewrite `policy-xml-flow-graph-modeling` for the new model.
  - Add candidates for Roslyn syntax-only expression analysis, outcome-preserving
    collapsed-graph projection, and ELK compound layout with React Flow.

### 9.1 Execution-Sized Task Review

| ID | Observable outcome | Owner | Prerequisite interface | Output / test files | Acceptance → check | Exclusions |
|----|--------------------|-------|------------------------|---------------------|--------------------|-----------|
| SPF-1 | Source-mapped tree with comments and spans | policy-parsing-engineer | ARM XML string (POL-1) | `Policy/Source/PolicySourceLoader.cs`, `PolicySourceLoaderTests.cs` | Spans, comments, decode, DTD → `PolicySourceLoaderTests` | Fragments, analysis |
| SPF-2 | Fragment occurrence regions and labels | policy-parsing-engineer | SPF-1 tree | `FragmentRegionBuilder.cs`, `FragmentRegionBuilderTests.cs` | Pairing, occurrences, nesting, commented markup → `FragmentRegionBuilderTests` | Model building |
| SPF-3 | Roslyn syntax-only expression facts | policy-parsing-engineer | SPF-1 spans | `PolicyExpressionAnalyzer.cs`, csproj, `PolicyExpressionAnalyzerTests.cs` | Reads, returns, catches, summaries, opaque, no compile → `PolicyExpressionAnalyzerTests` | Variable index |
| SPF-4 | Structural execution model and synthetic fixture | policy-parsing-engineer | SPF-2, SPF-3 | `PolicyFlowModel.cs`, `PolicyFlowModelBuilder.cs`, fixture, test csproj, `PolicyFlowModelBuilderTests.cs` | choose/return/retry/tag mapping → `PolicyFlowModelBuilderTests` | Stage exits, state |
| SPF-5 | APIM stage, exit and CORS semantics | policy-parsing-engineer | SPF-4 | `ApimSemanticRules.cs`, builder, `ApimSemanticRulesTests.cs` | Lanes, exception exits, forward-request, CORS → `ApimSemanticRulesTests` | State |
| SPF-6 | Variable index, config-dependence, cache data edges | policy-parsing-engineer | SPF-5 | `PolicyStateAnalyzer.cs`, builder, `PolicyStateAnalyzerTests.cs` | Writers/readers, badges, data edges → `PolicyStateAnalyzerTests` | UI |
| SPF-7 | schemaVersion 2 endpoint; legacy parsers removed | policy-parsing-engineer | SPF-6 | Controller, `Program.cs`, `UiFixtureHost/Program.cs`, `PolicyFlowControllerTests.cs`, `LegacyFlowGraphRemovalTests.cs` | Contract, status codes, no legacy types → controller and removal tests | Frontend |
| SPF-8 | Contract types and outcome-preserving projection | frontend-visualization-engineer | SPF-7 schema | `flow/model.ts`, `projection.ts`, `__fixtures__/syntheticModel.ts`, `projection.test.ts` | Lifting, dedupe, invariants → `projection.test.ts` | Layout, rendering |
| SPF-9 | ELK left-to-right lane layout | frontend-visualization-engineer | SPF-8 | `package.json`, lockfile, `flow/layout.ts`, `layout.test.ts` | Column order, On-error below, containment, determinism → `layout.test.ts` | Rendering |
| SPF-10 | Node/edge visual language and legend | frontend-visualization-engineer | SPF-8 | `nodes.tsx`, `edges.tsx`, `Legend.tsx`, `flow.css`, `renderers.test.tsx` | Kinds distinguishable, not colour-only → `renderers.test.tsx` | Composition |
| SPF-11 | In-place expand/collapse view wired into App | frontend-visualization-engineer | SPF-9, SPF-10 | `PolicyFlowView.tsx`, `PolicyFlowView.test.tsx`, `App.tsx`, `App.integration.test.tsx`, `main.tsx`, `theme.css` | Overview, toggle, toolbar, states, reachability → view and App tests | Inspector, search |
| SPF-12 | Inspector with provenance and variable highlighting | frontend-visualization-engineer | SPF-11 | `Inspector.tsx`, `Inspector.test.tsx`, `PolicyFlowView.tsx` | Fields, slice, provenance, highlight → `Inspector.test.tsx` | Search |
| SPF-13 | Search that focuses without mutation | frontend-visualization-engineer | SPF-12 | `search.ts`, `FlowSearch.tsx`, `FlowSearch.test.tsx`, `PolicyFlowView.tsx` | Match fields, expand ancestors, no mutation → `FlowSearch.test.tsx` | Export |
| SPF-14 | Keyboard and axe accessibility | frontend-visualization-engineer | SPF-13 | `package.json` (user-event), `PolicyFlowView.a11y.test.tsx`, `nodes.tsx` | Four states zero serious/critical → a11y test | Manual audit |
| SPF-15 | Semantic acceptance suite and backend performance | qa-test-engineer | SPF-7 | `SemanticFlowAcceptanceTests.cs`, `SemanticFlowPerformanceTests.cs` | 18 semantic cases, 1 s p95 build → acceptance and performance tests | Live Azure |
| SPF-16 | Fixture host and browser performance/offline smoke | qa-test-engineer | SPF-14 | `UiFixtureHost/Program.cs`, `ui-fixture-smoke.mjs`, `semantic-flow-browser-smoke.mjs`, `tests/smoke/package.json` | UI NF budgets, zero external requests, real Tab order → smoke scripts | Live Azure |
| SPF-17 | Live reference-scope smoke | qa-test-engineer | SPF-7 | `tests/smoke/live-smoke.mjs` | Live structural invariants → live smoke | Committing live content |
| SPF-18 | Human fidelity and visual review | (human) | SPF-14–SPF-17 | `docs/reviews/semantic-policy-flow-review.json` | Reviewer checklist | Automated sign-off |

### Phase 1: Source-Mapped Semantic Model (backend)

```forge-task
{
  "id": "SPF-1",
  "title": "Implement source-mapped policy loader",
  "description": "Implement PolicySourceLoader, which turns the effective policy XML string into an in-memory source-mapped tree using XmlReader with IXmlLineInfo. It prohibits DTDs, sets XmlResolver to null, and retains comments (unlike the legacy loader, which uses IgnoreComments=true). Each element, comment and expression-bearing attribute records a 1-based start and end line and column span; end positions are captured from the matching EndElement, or equal the start for empty elements. ARM's extra escaping layer is removed from expression-bearing values only (text containing @( or @{). The original source text and line count are preserved. The root must be <policies>. Empty input, a missing root or malformed XML raise PolicyParseException carrying the XmlException message (including line and position) so the controller can keep returning 422. Out of scope: fragment regions, expression analysis and model building.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["POL-1"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/Source/PolicySourceLoader.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicySourceLoaderTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~PolicySourceLoaderTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-01"],
    "acceptanceCriteria": [
      "A multi-line fixture yields exact start/end line and column spans for nested elements, self-closing elements, comments and attributes, asserted against known positions",
      "Comments, including one containing tag-shaped text such as <include-fragment fragment-id=\"x\" />, are retained as comment nodes and produce no element nodes",
      "An attribute value ARM-escaped as &amp;gt; inside @(...) is restored to > while a non-expression attribute containing &amp; is unchanged",
      "A document with a DOCTYPE, a non-<policies> root, empty input or malformed markup raises PolicyParseException with a descriptive message"
    ],
    "constraints": ["Keep the source text and tree in memory only; never write them to disk or logs"],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-02"],
    "references": ["docs/research/visualizer-enhancements.md#Keep source traceability"]
  }
}
```

```forge-task
{
  "id": "SPF-2",
  "title": "Build fragment-occurrence regions from marker comments",
  "description": "Implement FragmentRegionBuilder over the SPF-1 tree. It pairs sibling comments matching 'include-fragment: Begin {name} policy fragment scope' and 'include-fragment: End {name} policy fragment scope' (whitespace-tolerant) into regions covering the siblings between them. Regions may nest. Each occurrence gets the ID {name}#{n}, numbered in document order per name, plus its index, total count for that name, and the stage in which it appears. Labels are chosen in this order: (1) the immediately preceding single-line sibling comment that is not itself a marker, with any leading 'Step N:' or 'Step N-M:' prefix removed; (2) the 'Fragment: {title}' line of the first comment inside the region; (3) the fragment name humanised (e.g. set-response-headers becomes 'Set response headers'). The label provenance (comment or fragment-name) is recorded. The region's descriptive comment text (for example a Purpose paragraph) is kept as the description. Unmatched, crossed or cross-parent markers produce a warning diagnostic with span, and the affected siblings stay ungrouped. Comment text is never parsed into elements. Out of scope: building model elements.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["SPF-1"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/Source/FragmentRegionBuilder.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/FragmentRegionBuilderTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~FragmentRegionBuilderTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-02"],
    "acceptanceCriteria": [
      "A fragment appearing once in outbound and once in on-error yields two regions with distinct occurrence IDs, count 2 and their respective stages",
      "A region inside retry/choose/when is found at that nesting level, and nested regions report the correct parent region",
      "Label precedence is proven by three cases: a preceding 'Step 3: Validate model' comment gives 'Validate model'; with no preceding comment, the 'Fragment: X' header is used; otherwise the humanised name",
      "An unmatched Begin, a crossed Begin/End pair and a commented-out <include-fragment> each produce the expected diagnostic or no region, and never an executable element"
    ],
    "constraints": ["Comments supply labels and descriptions only; they never change control flow"],
    "constraintRefs": [],
    "references": ["docs/research/visualizer-enhancements.md#7. Use comments for grouping, but not as executable truth"]
  }
}
```

```forge-task
{
  "id": "SPF-3",
  "title": "Implement Roslyn syntax-only expression analyzer",
  "description": "Add Microsoft.CodeAnalysis.CSharp 5.9.0 and implement PolicyExpressionAnalyzer. It parses @(expr) with SyntaxFactory.ParseExpression and @{ block } as a block statement, then walks the syntax tree only, extracting: context.Variables names read via indexer, GetValueOrDefault<T>, ContainsKey or TryGetValue; {{named-value}} references; context.Request/Response/Subscription/Product/User/Deployment member paths read (e.g. Request.OriginalUrl.Path, Response.StatusCode); the ordered top-level if/else-if/return paths of block expressions, each with a condition summary and returned-value kind (literal, variable, expression); try/catch presence with the literal fallback returned in the catch; the literal string prefix of concatenated key expressions; and a human-readable summary for conditions made only of comparisons, &&/||/!, null checks, string Contains/StartsWith/EndsWith/Equals and literals (e.g. 'Response status = 429 OR (status >= 500 AND reason does not contain \"Backend pool\")'). Every fact carries provenance inferred and a source span mapped back through SPF-1 spans. Anything else, or any syntax diagnostic, yields an opaque result with the verbatim text plus an analysis diagnostic. A C# return or caught exception is reported as expression-local only. Out of scope: the variable writer index and model building.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["SPF-1"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/ApimPolicyVisualizer.Api.csproj",
    "src/server/ApimPolicyVisualizer.Api/Policy/Expressions/PolicyExpressionAnalyzer.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicyExpressionAnalyzerTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~PolicyExpressionAnalyzerTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-10", "docs/features/effective-policy-flow-model.md#POL-FR-05"],
    "acceptanceCriteria": [
      "Variable reads through all four access forms, named values and context member paths are extracted from representative expressions",
      "A seven-branch if/return block (method sentinel, parameter, URL patterns, header, JSON body) yields seven ordered return paths in source order, and a surrounding try/catch returning \"\" is reported as a local catch with fallback \"\"",
      "The retry-style condition 'context.Response.StatusCode == 429 || (context.Response.StatusCode >= 500 && !context.Response.StatusReason.Contains(\"Backend pool\"))' yields the expected readable summary; a LINQ/lambda-heavy expression yields an opaque result",
      "An expression calling System.IO.File.Delete(...) is analysed as syntax only, and a reflection-based test asserts the analyzer references no CSharpCompilation, SemanticModel, Emit or Scripting API",
      "A syntactically invalid expression produces an opaque result and a diagnostic, not an exception"
    ],
    "constraints": ["Never compile, bind, evaluate or execute expression text; syntax trees only"],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-03"],
    "references": ["docs/research/visualizer-enhancements.md#B. Model extraction", "docs/research/visualizer-enhancements.md#Exact annotations to attach"]
  }
}
```

```forge-task
{
  "id": "SPF-4",
  "title": "Build structural execution model and synthetic fixture",
  "description": "Define the schemaVersion 2 contract records in PolicyFlowModel.cs, following the SPF-FR-12 field list. Implement the structural part of PolicyFlowModelBuilder from the SPF-1 tree, SPF-2 regions and SPF-3 analysis: stage elements; group elements for fragment occurrences; ordered steps with category classification and configuration-child folding (SPF-FR-08); choose as a decision with ordered branch edges carrying priority and condition (verbatim plus inferred summary), an otherwise edge, a no-match edge, bypass edges for empty clauses, and a merge node (SPF-FR-04); return-response terminals with status and reason, an explicit-response edge, and aggregated codes on every ancestor (SPF-FR-05); retry loop containers with a synthesized loop-test node reached by every non-terminating body exit, loop-back, loop-exit resolved through the nearest enclosing continuation, and attribute facts (SPF-FR-06); opaque steps for unknown tags; friendly labels per tag (e.g. 'Set variable requestedModel', 'Return 403 Forbidden', 'Forward request to backend'); sequence edges and stable IDs derived from structural path; structural facts with provenance structural. Also create a sanitized synthetic fixture that reproduces the reference policy's shape with invented names and values: 10 or more inbound fragment regions with Step comments, early returns (400/401/403/404/503/500), validate-jwt, a model-extraction block expression with try/catch, a retry in backend containing two fragment occurrences and a forward-request, a Responses-style cache-lookup-value in inbound and cache-store-value in outbound, a fragment repeated in outbound and on-error, a cors policy, an on-error choose on status 429 with emit-metric, a commented-out include-fragment, and one unknown tag. Register the fixture as CopyToOutputDirectory. Out of scope: stage lanes and exception exits (SPF-5), the variable index and data edges (SPF-6), and the controller.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["SPF-2", "SPF-3"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModel.cs",
    "src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModelBuilder.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/Fixtures/ai-gateway-synthetic.xml",
    "tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicyFlowModelBuilderTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~PolicyFlowModelBuilderTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-04",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-05",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-06",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-08",
      "docs/features/effective-policy-flow-model.md#POL-FR-02",
      "docs/features/effective-policy-flow-model.md#POL-FR-03"
    ],
    "acceptanceCriteria": [
      "A choose without otherwise has priority-numbered branch edges, one no-match edge and a merge; an empty when produces a bypass edge; a when ending in return-response does not reach the merge",
      "A return-response nested three levels deep has exactly one explicit-response edge and no sequence edge to later siblings, each ancestor group lists its status code once, and a return-response without set-status or response-variable-name is labeled 200 OK (no body)",
      "A retry has a loop-test node that every non-terminating body leaf (including both arms of a body choose) reaches while return/raise exits do not, one loop-back edge from the loop test to the first body step, a loop-exit to the next sibling or, when the retry is last in its stage, to the stage continuation, and verbatim count/interval/first-fast-retry facts",
      "value/dimension/audiences children are folded as properties; trace/emit-metric are compact observability steps; an unknown tag is an opaque step with a diagnostic",
      "Building the synthetic fixture twice yields identical element and edge IDs, every edge endpoint and parentId resolves, and no fixture content contains customer-specific names"
    ],
    "constraints": ["Do not commit any content fetched from a live APIM instance"],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-03"],
    "references": [
      "docs/research/visualizer-enhancements.md#2. Map policy syntax to execution semantics",
      "docs/research/visualizer-enhancements.md#3. Inbound should expand into meaningful subprocesses",
      "docs/research/visualizer-enhancements.md#4. The backend retry loop needs a dedicated diagram"
    ]
  }
}
```

```forge-task
{
  "id": "SPF-5",
  "title": "Apply APIM stage, exit and CORS semantic rules",
  "description": "Implement ApimSemanticRules as a versioned, predicate-based rule table (tag plus attribute conditions) and apply it in PolicyFlowModelBuilder. It adds: a Request entry node with sequence links Inbound, then Backend, then Outbound; the On-error lane as an exception lane with an On-error entry node when the section exists; exactly one stage-exception edge from each present normal stage to the On-error entry, or to a 'Gateway default error response' terminal when on-error is absent; terminals 'Final backend response to caller' after Outbound and 'Error response to caller' after non-terminating On-error flow, with On-error never linked to Outbound and any return-response inside On-error reaching the explicit-response terminal (SPF-FR-03); raises-error exits per the SPF-FR-07 predicates, covering forward-request transport failure (always), forward-request HTTP status (only when fail-on-error-status-code='true'), send-request (unless ignore-error='true'), the always-raising limit and validation list, and validate-content/parameters/headers/status-code only for action 'prevent'; routing-state and cache-state facts (SPF-FR-07); and, when cors is in Inbound, a configuration-dependent 'CORS preflight request (if no matching OPTIONS operation)' entry to the first applicable cors step and a 'Preflight response to caller' terminal (SPF-FR-09). Every rule fact carries a ruleId and provenance apim-rule. Groups aggregate raisesError from their descendants. Out of scope: the variable index and data-dependency edges.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["SPF-4"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/Model/ApimSemanticRules.cs",
    "src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModelBuilder.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/ApimSemanticRulesTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~ApimSemanticRulesTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-03",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-07",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-09"
    ],
    "acceptanceCriteria": [
      "Exactly three stage-exception edges exist for a policy with all four sections, no edge leads from any On-error element to an Outbound element, non-terminating On-error flow ends at the error-response terminal, and a return-response inside On-error reaches the explicit-response terminal",
      "A policy without on-error routes its three stage-exception edges to the gateway-default error terminal and has no On-error entry",
      "validate-jwt has a raises-error exit, and its failed-validation-httpcode attribute is shown as a fact rather than as an explicit-response edge",
      "forward-request without fail-on-error-status-code has one transport-failure raises-error exit and the continue-to-Outbound fact for HTTP status; with the attribute set to true it gains an HTTP-status raises-error exit",
      "send-request with ignore-error='true' has no raises-error exit and with ignore-error='false' has one; validate-content with action 'detect' has none and with 'prevent' has one",
      "With cors present, the configuration-dependent preflight entry reaches the cors step and the preflight terminal, while the ordinary Request entry path contains no preflight edge; without cors, no preflight elements exist",
      "Every apim-rule fact has a non-empty ruleId present in the rule table"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": [
      "docs/research/visualizer-enhancements.md#5. Distinguish three kinds of “failure”",
      "docs/research/visualizer-enhancements.md#CORS is a special entry path",
      "docs/research/visualizer-enhancements.md#Why these boundaries matter"
    ]
  }
}
```

```forge-task
{
  "id": "SPF-6",
  "title": "Add variable state index, configuration-dependence and cache data edges",
  "description": "Implement PolicyStateAnalyzer and apply it in PolicyFlowModelBuilder. It builds the variable index: writers are set-variable name, cache-lookup-value variable-name, send-request response-variable-name, validate-jwt output-token-variable-name and other *-variable-name attributes; readers are elements whose SPF-3 analysis reads the variable. Each writer records whether its value is a literal and which dependency classes its expression reads. Classes propagate through variable writers: configuration covers named values, variables with no writer in this policy, and context.Api/Deployment/Product settings; runtime covers request, response, subscription/user identity and values computed from them. Only decisions with a configuration input get the configuration-dependent badge (and variable-not-assigned when there is no writer); runtime-only decisions stay unbadged, and no branch is ever marked unreachable or hidden. Each cache-store-value and cache-lookup-value pair with equal caching-type and matching literal key prefix is linked by one data-dependency edge from store to lookup, labeled 'affects subsequent requests' with provenance inferred. These edges are excluded from sequence/branch traversal helpers. Out of scope: the controller and UI.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["SPF-5"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyStateAnalyzer.cs",
    "src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModelBuilder.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicyStateAnalyzerTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter FullyQualifiedName~PolicyStateAnalyzerTests"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-11"],
    "acceptanceCriteria": [
      "A variable written by two set-variable elements and read by a retry condition and a when condition lists both writers and both readers",
      "A when reading a variable with no writer has the configuration-dependent and variable-not-assigned badges; a when reading a {{named-value}}-derived variable has the configuration-dependent badge; a when reading only a variable computed from context.Request.Method or context.Response.StatusCode has neither badge",
      "The fixture's outbound cache-store-value and inbound cache-lookup-value with the same literal key prefix are linked by exactly one data-dependency edge, and a pair with different prefixes or caching-type is not linked",
      "A reachability helper walking control edges from the Request entry never traverses a data-dependency edge"
    ],
    "constraints": ["Never claim or render a branch as unreachable"],
    "constraintRefs": [],
    "references": [
      "docs/research/visualizer-enhancements.md#Separate “present in source” from “selected by this configuration”",
      "docs/research/visualizer-enhancements.md#6. Outbound is small, but its conditions matter"
    ]
  }
}
```

```forge-task
{
  "id": "SPF-7",
  "title": "Serve schemaVersion 2 model and remove legacy flow graph",
  "description": "Rewrite PolicyFlowController so GET /api/policy/effective-flow runs PolicySourceLoader, then FragmentRegionBuilder, then PolicyExpressionAnalyzer, then PolicyFlowModelBuilder (with rules and state), and returns the schemaVersion 2 response with scopeId, scopeKind, source text and line count. Keep the existing 400 (missing or unknown scope), 403/404 (catalog or ARM) and 422 (PolicyParseException) ProblemDetails behaviour. Log only the scope ID, never policy content. Register the new services in Program.cs and tests/smoke/UiFixtureHost/Program.cs. Delete src/server/ApimPolicyVisualizer.Api/Policy/PolicyFlowParser.cs, src/server/ApimPolicyVisualizer.Api/Policy/PolicyControlFlowParser.cs, tests/ApimPolicyVisualizer.Api.Tests/PolicyFlowParserTests.cs and tests/ApimPolicyVisualizer.Api.Tests/PolicyControlFlowParserTests.cs, and add a removal test proving those types no longer exist. Out of scope: frontend changes.",
  "ownerAgent": "policy-parsing-engineer",
  "dependencies": ["SPF-6", "POL-4"],
  "expectedOutputs": [
    "src/server/ApimPolicyVisualizer.Api/Policy/PolicyFlowController.cs",
    "src/server/ApimPolicyVisualizer.Api/Program.cs",
    "tests/smoke/UiFixtureHost/Program.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/PolicyFlowControllerTests.cs",
    "tests/ApimPolicyVisualizer.Api.Tests/LegacyFlowGraphRemovalTests.cs"
  ],
  "validationCommands": [
    "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter \"FullyQualifiedName~PolicyFlowControllerTests|FullyQualifiedName~LegacyFlowGraphRemovalTests\"",
    "dotnet build tests/smoke/UiFixtureHost/UiFixtureHost.csproj"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-12",
      "docs/features/effective-policy-flow-model.md#POL-FR-04"
    ],
    "acceptanceCriteria": [
      "A controller test using the synthetic fixture through a mocked IEffectivePolicyClient returns 200 with schemaVersion 2, the exact source text, and every edge endpoint and parentId resolving",
      "Missing scope, unknown scope, ARM 403, ARM 404 and malformed XML return 400/400/403/404/422 ProblemDetails with descriptive detail",
      "The serialized response contains no XElement or internal-only properties, and a logging test proves no policy text is logged",
      "LegacyFlowGraphRemovalTests asserts that PolicyFlowParser, PolicyControlFlowParser and PolicyFlowGraph are absent from the API assembly, and the full backend test project builds"
    ],
    "constraints": [],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-02", "docs/features/semantic-policy-flow-visualization.md#SPF-SEC-01"],
    "references": ["docs/features/effective-policy-flow-model.md#POL-FR-01"]
  }
}
```

### Phase 2: Hierarchical Visualization (frontend)

```forge-task
{
  "id": "SPF-8",
  "title": "Implement contract types and outcome-preserving projection",
  "description": "Create src/web/src/flow/model.ts, mirroring the SPF-7 schemaVersion 2 response, and projection.ts, a pure function (model, expandedIds, options) -> { nodes, edges }. The default expansion (overview) shows stage lanes expanded, their top-level fragment groups, decisions and retry loops collapsed, and each run of two or more consecutive ungrouped simple top-level steps wrapped in a synthetic composite. Every edge whose endpoint lies inside a collapsed ancestor is lifted to the outermost collapsed ancestor as a quotient graph. Each projected edge keeps the contributing full-model edge IDs and payload (kind, priority, condition, status codes). Edges merge only when kind, target and payload class are compatible; distinct branch priorities never merge. Explicit-response edges carry the union of status codes as their label. Edges internal to one collapsed node are dropped from the canvas and summarised on the node: a collapsed retry gets a loop badge with its condition and budget summary. Observability steps can be hidden by an option that reconnects their predecessor to their successor with the same edge kind. Data-dependency edges can be toggled off. Also add expandAncestors(id) and collapseToOverview helpers. Add a hand-authored syntheticModel.ts fixture matching the backend synthetic fixture's shapes. Out of scope: layout and rendering.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["SPF-7"],
  "expectedOutputs": [
    "src/web/src/flow/model.ts",
    "src/web/src/flow/projection.ts",
    "src/web/src/flow/__fixtures__/syntheticModel.ts",
    "src/web/src/flow/projection.test.ts"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/flow/projection.test.ts"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-14",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-13"
    ],
    "acceptanceCriteria": [
      "In the overview, a collapsed group containing returns 401/403/503 and a validate-jwt shows exactly one explicit-response edge labeled '401 / 403 / 503' and one raises-error edge, separate from its normal continuation",
      "A property test over every single-group expansion state and the fully expanded state of the synthetic model shows that the set of (outcome kind, terminal) pairs reachable from the Request entry is identical, and that every full-model edge crossing a visible boundary is contained in exactly one projected edge's contributing IDs",
      "No projected edge connects two nodes whose full-model descendants have no edge of that kind between them; two when-branches with different priorities into the same collapsed target stay two edges",
      "A collapsed retry exposes a loop badge carrying the loop-test summary, and expanding it shows the loop-back edge whose contributing ID matches the model's loop-back edge",
      "Hiding observability steps and toggling data-dependency edges change only those items; expandAncestors on a deeply nested step expands exactly its ancestor chain"
    ],
    "constraints": ["Projection must be pure and never mutate the model"],
    "constraintRefs": [],
    "references": ["docs/research/visualizer-enhancements.md#1. The top-level visualisation"]
  }
}
```

```forge-task
{
  "id": "SPF-9",
  "title": "Implement ELK left-to-right lane layout",
  "description": "Add elkjs 0.12.0 (bundled build, no worker URL fetched from a network) and implement layout.ts, an async function that takes projected nodes and edges and returns positioned nodes, sizes and edge routes. Use a deterministic two-level layout, because a single ELK run cannot express the lane grid. (1) Lay out each lane's contents independently with ELK layered (direction RIGHT, hierarchyHandling INCLUDE_CHILDREN, so collapsed subprocesses expose separate ports for continuation, explicit-response and raises-error). (2) Place the Inbound, Backend and Outbound lanes left to right in that order with equal top alignment. Put the entry nodes to the left of Inbound and the caller terminals to the right of Outbound. (3) Place the On-error lane below the union of the normal lanes, sized to span their combined width, with the error terminals to its right. (4) Route cross-lane edges (stage sequence, stage-exception, explicit-response to the shared terminal, data-dependency) with orthogonal routes computed after placement. Children stay inside their compound parents with padding reserved for headers. Layout must be deterministic for identical input. Out of scope: React rendering.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["SPF-8"],
  "expectedOutputs": [
    "src/web/package.json",
    "src/web/package-lock.json",
    "src/web/src/flow/layout.ts",
    "src/web/src/flow/layout.test.ts"
  ],
  "validationCommands": [
    "npm --prefix src/web install",
    "npm --prefix src/web run test -- run src/flow/layout.test.ts"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-13"],
    "acceptanceCriteria": [
      "For the synthetic overview, the lane x positions satisfy Inbound < Backend < Outbound, and the On-error lane's top is below the bottom of all three lanes with a width spanning them",
      "Every child's bounds lie within its parent's bounds, and nodes do not overlap within a lane",
      "Two layouts of identical input produce identical coordinates",
      "The fully expanded synthetic model and a generated 150-visible-node projection lay out without error"
    ],
    "constraints": ["Bundle elkjs locally; no CDN or remote worker script"],
    "constraintRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-SEC-01", "docs/PRD.md#VIS-CONSTRAINT-01"],
    "references": ["docs/research/visualizer-enhancements.md#1. The top-level visualisation"]
  }
}
```

```forge-task
{
  "id": "SPF-10",
  "title": "Implement node and edge visual language with legend",
  "description": "Implement React Flow custom node types in nodes.tsx: stage lane, subprocess (double border, expand/collapse button, occurrence-qualified label, badges for explicit-response codes, raises-error, configuration-dependent, opaque and diagnostics), composite, action step with category icon/text, compact observability, decision (diamond), merge dot, loop container with a budget/condition summary header, loop test, entry, and terminals (amber explicit response, neutral final response, red error response, preflight response). Implement edge types in edges.tsx: solid sequence; branch with a priority number and condition-summary label; no-match; bypass; dashed red raises-error and stage-exception; amber explicit-response; curved loop-back; loop-exit; preflight; dotted data-dependency. Implement Legend.tsx documenting every node and edge kind, and flow.css. Every kind must be distinguishable without colour through shape, stroke pattern, icon or text. Configuration-dependent elements are subdued but still labelled and never hidden. Colour contrast must meet WCAG AA. Out of scope: composition and data fetching.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["SPF-8"],
  "expectedOutputs": [
    "src/web/src/flow/nodes.tsx",
    "src/web/src/flow/edges.tsx",
    "src/web/src/flow/Legend.tsx",
    "src/web/src/styles/flow.css",
    "src/web/src/flow/renderers.test.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/flow/renderers.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-15"],
    "acceptanceCriteria": [
      "Each node kind renders with a distinct accessible role description and visible text or shape marker, and each edge kind renders with a distinct stroke pattern or marker plus an aria label naming its kind",
      "A repeated fragment renders 'Set response headers · outbound' and '· on-error' labels; a subprocess shows its explicit-response codes and raises-error badge",
      "A configuration-dependent decision renders with the subdued class and a visible 'configuration-dependent' badge",
      "The legend lists every node and edge kind defined in model.ts, enforced by a test enumerating the kinds"
    ],
    "constraints": [],
    "constraintRefs": ["docs/PRD.md#VIS-CONSTRAINT-01"],
    "references": ["docs/research/visualizer-enhancements.md#8. Special cases and presentation choices"]
  }
}
```

```forge-task
{
  "id": "SPF-11",
  "title": "Compose in-place expand/collapse view and wire into App",
  "description": "Implement PolicyFlowView.tsx: fetch GET /api/policy/effective-flow for the selected scope with abort and stale-response protection; show loading and error states with the backend ProblemDetails message and a Back to scope selection action (UI-FR-04); and hold expansion state, running projection, then layout, then React Flow render. Toggling a subprocess expands or collapses it in place, re-lays-out, and keeps the toggled node in view. Provide a toolbar with Collapse to overview, Expand all, Fit view, and toggles for data-dependency edges and observability steps; a legend; and a diagnostics summary with a count and list. Nodes are not draggable; pan and zoom are enabled. Replace the PolicyFlowDiagram usage in App.tsx, import flow.css in main.tsx, remove the legacy diagram rules from theme.css, and delete src/web/src/components/PolicyFlowDiagram.tsx, PolicyFlowDiagram.test.tsx and PolicyFlowDiagram.a11y.test.tsx. Out of scope: inspector and search.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["SPF-9", "SPF-10"],
  "expectedOutputs": [
    "src/web/src/flow/PolicyFlowView.tsx",
    "src/web/src/flow/PolicyFlowView.test.tsx",
    "src/web/src/App.tsx",
    "src/web/src/App.integration.test.tsx",
    "src/web/src/main.tsx",
    "src/web/src/styles/theme.css"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/flow/PolicyFlowView.test.tsx src/App.integration.test.tsx",
    "npm --prefix src/web run build"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-13",
      "docs/features/policy-flow-visualization-ui.md#UI-FR-02",
      "docs/features/policy-flow-visualization-ui.md#UI-FR-04"
    ],
    "acceptanceCriteria": [
      "App.integration.test selects a scope through the real App tree with a mocked API client and sees the overview: three lanes, the On-error lane, collapsed subprocesses and three caller terminals",
      "Expanding a subprocess reveals its children on the same canvas; collapsing it restores the overview node set; Collapse to overview and Expand all behave as named",
      "403, 404 and 422 responses show the backend detail and a working Back to scope selection button; a stale response for a previous scope is never shown",
      "A test using import.meta.glob asserts that no module under src/components matches PolicyFlowDiagram, and the production build succeeds"
    ],
    "constraints": [],
    "constraintRefs": ["docs/PRD.md#VIS-CONSTRAINT-01", "docs/features/semantic-policy-flow-visualization.md#SPF-SEC-01"],
    "references": ["docs/features/policy-flow-visualization-ui.md#UI-FR-01"]
  }
}
```

```forge-task
{
  "id": "SPF-12",
  "title": "Implement inspector with provenance and variable highlighting",
  "description": "Implement Inspector.tsx and wire it into PolicyFlowView. Selecting a node or edge (click or keyboard Enter) opens a side panel showing: friendly label; tag or fragment name with occurrence (e.g. set-response-headers #2 of 2 · on-error); structural path breadcrumb; the verbatim condition with the inferred summary shown beneath, labelled Inferred; the source line range and the exact source slice from model.source.text with line numbers, in a scrollable monospace block; variables read and written, each expandable to list writers and readers, with a 'highlight' action that marks those nodes on the canvas (expanding ancestors as needed); the associated comment; badges; diagnostics; a list of the node's incoming and outgoing edges as focusable buttons (kind, label, other endpoint) whose activation selects that edge; and a facts list where every entry shows a provenance chip (Structural, APIM rule, Inferred, Comment). Escape closes the panel and returns focus to the selected node. Out of scope: search.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["SPF-11"],
  "expectedOutputs": [
    "src/web/src/flow/Inspector.tsx",
    "src/web/src/flow/Inspector.test.tsx",
    "src/web/src/flow/PolicyFlowView.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/flow/Inspector.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-16"],
    "acceptanceCriteria": [
      "Selecting a when-branch edge shows its priority, verbatim condition and an inferred summary labelled Inferred",
      "Selecting a step shows a source slice whose first and last lines equal the span's lines from source.text",
      "Highlighting a variable marks all writers and readers, expanding collapsed ancestors so each is visible",
      "Every rendered fact has exactly one provenance chip; activating an item in the edge list selects that edge and shows its details; Escape closes the inspector and restores focus"
    ],
    "constraints": ["Render source text as text only; never inject it as HTML"],
    "constraintRefs": [],
    "references": ["docs/research/visualizer-enhancements.md#Keep source traceability"]
  }
}
```

```forge-task
{
  "id": "SPF-13",
  "title": "Implement search that focuses without mutating the graph",
  "description": "Implement search.ts, which builds an index from the model over labels, tags, fragment names, variable names, explicit-response and status codes, and source-slice text, and FlowSearch.tsx, a labelled search box with a keyboard-navigable result list (stage, path and match kind per result). Wire both into PolicyFlowView. Choosing a result calls expandAncestors, re-lays-out, centres the element and selects it so the inspector opens. The model object is deep-frozen in tests to prove it is never mutated. Out of scope: export.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["SPF-12"],
  "expectedOutputs": [
    "src/web/src/flow/search.ts",
    "src/web/src/flow/FlowSearch.tsx",
    "src/web/src/flow/FlowSearch.test.tsx",
    "src/web/src/flow/PolicyFlowView.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web run test -- run src/flow/FlowSearch.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-17"],
    "acceptanceCriteria": [
      "Queries for a fragment name, a variable name, '403' and a source-only token each return the expected elements",
      "Choosing a result nested inside a collapsed retry expands exactly its ancestors, selects it and opens the inspector",
      "Searching and choosing results against a deep-frozen model throws no mutation error, and element and edge counts are unchanged"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-16"]
  }
}
```

```forge-task
{
  "id": "SPF-14",
  "title": "Verify keyboard operation and axe accessibility of flow views",
  "description": "Add @testing-library/user-event as a dev dependency and add PolicyFlowView.a11y.test.tsx, which renders four states with the synthetic model: the overview; the retry loop expanded; the inspector open; and search results shown. Using user-event keyboard simulation and explicit focus-order assertions, it checks that Tab/Shift+Tab moves through every visible node in document order, Enter or Space toggles expansion, Enter selects a node, the inspector's edge list items select edges, and Escape closes the inspector and restores focus. It also checks that accessible names include each node's kind, label and outcomes (e.g. 'Subprocess Authenticate subscription, explicit responses 401, 403, 503, raises error'), and that axe-core reports zero serious or critical violations in each state. Fix any issues in nodes.tsx. Real-browser Tab order is re-checked by SPF-16. Out of scope: a manual WCAG audit.",
  "ownerAgent": "frontend-visualization-engineer",
  "dependencies": ["SPF-13"],
  "expectedOutputs": [
    "src/web/package.json",
    "src/web/package-lock.json",
    "src/web/src/flow/PolicyFlowView.a11y.test.tsx",
    "src/web/src/flow/nodes.tsx"
  ],
  "validationCommands": [
    "npm --prefix src/web install",
    "npm --prefix src/web run test -- run src/flow/PolicyFlowView.a11y.test.tsx"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-A11Y-01",
      "docs/PRD.md#VIS-A11Y-01"
    ],
    "acceptanceCriteria": [
      "Each of the four states reports zero serious or critical axe-core violations",
      "Keyboard-only traversal, toggle, select and close work as specified, and accessible names include outcomes"
    ],
    "constraints": [],
    "constraintRefs": [],
    "references": ["docs/features/semantic-policy-flow-visualization.md#SPF-FR-15"]
  }
}
```

### Phase 3: Verification

```forge-task
{
  "id": "SPF-15",
  "title": "Add research-derived semantic acceptance suite",
  "description": "Add SemanticFlowAcceptanceTests, which runs the full backend pipeline over the synthetic fixture and asserts one named test per research-derived semantic case: (1) explicit return-response goes directly to the caller, skipping later stages, and never enters On-error; (2) a backend HTTP error continues through Outbound unless fail-on-error-status-code is true; (3) a validate-jwt failure enters On-error; (4) On-error never resumes Outbound; (5) choose is first-match, with priorities, no-match continue and merge; (6) an empty when bypasses only its block; (7) the retry body runs before the loop test, and the loop-back stays inside Backend without reaching any Inbound element; (8) repeated fragment occurrences are distinct nodes; (9) a commented-out include-fragment is not executable; (10) CORS preflight is a separate entry; (11) cache store-to-lookup is a data dependency, not control flow;   (12) a C# return and caught exception inside an expression are not pipeline termination; (13) configuration-dependent branches are badged, not hidden, and runtime-only branches are not badged; (14) the model-extraction precedence is reported as ordered inferred return paths; (15) every fact has a provenance and every apim-rule fact a ruleId; (16) a return-response inside On-error reaches the explicit-response terminal and a return-response with no status is 200 OK; (17) with no on-error section, stage failures reach the gateway-default error terminal; (18) send-request ignore-error and validate-* detect/prevent change the raises-error exits. Also add SemanticFlowPerformanceTests, which scales the synthetic fixture to about 300 KB and 2,500 lines by repeating valid fragment regions, warms up, times 20 in-process model builds, and fails if p95 exceeds the SPF-NF-01 1 s backend budget (printing p50/p95). Out of scope: live Azure.",
  "ownerAgent": "qa-test-engineer",
  "dependencies": ["SPF-7"],
  "expectedOutputs": [
      "tests/ApimPolicyVisualizer.Api.Tests/SemanticFlowAcceptanceTests.cs",
      "tests/ApimPolicyVisualizer.Api.Tests/SemanticFlowPerformanceTests.cs"
    ],
    "validationCommands": [
      "dotnet test tests/ApimPolicyVisualizer.Api.Tests/ApimPolicyVisualizer.Api.Tests.csproj --filter \"FullyQualifiedName~SemanticFlowAcceptanceTests|FullyQualifiedName~SemanticFlowPerformanceTests\""
    ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-03",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-04",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-05",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-06",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-07",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-11"
    ],
    "acceptanceCriteria": [
      "All 18 named semantic cases exist as separate tests and pass against the synthetic fixture",
      "Each test fails if its semantic invariant is removed, proven for at least cases 1, 4 and 7 by a mutated-model negative check",
      "SemanticFlowPerformanceTests reports p50/p95 over 20 builds of the scaled fixture and fails above 1 s p95"
    ],
    "constraints": ["Use only the committed synthetic fixture; never live policy content"],
    "constraintRefs": [],
    "references": [
      "docs/research/visualizer-enhancements.md#5. Distinguish three kinds of “failure”",
      "docs/research/visualizer-enhancements.md#2. Map policy syntax to execution semantics"
    ]
  }
}
```

```forge-task
{
  "id": "SPF-16",
  "title": "Add fixture host, browser performance and offline smoke",
  "description": "Update UiFixtureHost to serve the synthetic AI-gateway fixture (apis/example) and a generated 300 KB / about 2,500-line variant (apis/large) built by repeating fragment regions with valid structure, plus the existing 403/404/422 scopes. Update ui-fixture-smoke.mjs to assert schemaVersion 2 invariants: edge endpoints resolve; the kinds set includes sequence, branch, explicit-response, stage-exception and loop-back; problem details are returned. Add tests/smoke/package.json with playwright-core, and semantic-flow-browser-smoke.mjs, which launches the locally installed Microsoft Edge (channel msedge) against the built UI served by the fixture host. It records every request and fails on any request to a host other than 127.0.0.1 or localhost; measures response-to-overview-painted over 20 runs, one subprocess expand and collapse with 150 or fewer visible nodes, and search latency; fails if any SPF-NF-01 p95 budget is exceeded; verifies with real browser Tab/Shift+Tab that focus reaches every visible node of the overview in order and that Escape from the inspector restores focus; and writes docs/reviews/semantic-flow-performance.json with the raw timings and the machine description. Document the run commands at the top of the script. Out of scope: live Azure.",
  "ownerAgent": "qa-test-engineer",
  "dependencies": ["SPF-14"],
  "expectedOutputs": [
    "tests/smoke/UiFixtureHost/Program.cs",
    "tests/smoke/ui-fixture-smoke.mjs",
    "tests/smoke/package.json",
    "tests/smoke/semantic-flow-browser-smoke.mjs"
  ],
  "validationCommands": [
    "npm --prefix src/web run build",
    "npm --prefix tests/smoke install",
    "node tests/smoke/semantic-flow-browser-smoke.mjs"
  ],
  "contract": {
    "version": 2,
    "kind": "implementation",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-NF-01",
      "docs/features/semantic-policy-flow-visualization.md#SPF-A11Y-01"
    ],
    "acceptanceCriteria": [
      "The browser smoke starts or reuses the fixture host, observes zero requests to non-local hosts, and exits non-zero if any request is external",
      "p95 timings for overview paint, expand/collapse and search are computed over 20 runs, compared with the SPF-NF-01 budgets, and written to the performance report",
      "ui-fixture-smoke.mjs passes against the updated host",
      "Real-browser keyboard traversal reaches every visible overview node, and Escape from the inspector returns focus to the selected node"
    ],
    "constraints": ["Do not download browsers; use the installed Edge channel and fail with guidance if it is absent"],
    "constraintRefs": ["docs/features/semantic-policy-flow-visualization.md#SPF-SEC-01"],
    "references": ["docs/features/policy-flow-visualization-ui.md#9. Running and Verifying the UI"]
  }
}
```

```forge-task
{
  "id": "SPF-17",
  "title": "Extend live smoke with reference-scope semantic invariants",
  "description": "Extend tests/smoke/live-smoke.mjs so that, besides the existing one-scope-per-kind check, it fetches GET /api/policy/effective-flow for SMOKE_REFERENCE_SCOPE (default apis/universal-llm-api). It asserts: schemaVersion 2; all four stages present; at least one fragment-occurrence group; every fragment name that occurs more than once has distinct occurrence IDs; at least one explicit-response edge; exactly one stage-exception edge per present normal stage; a loop container in Backend whose loop-back stays within it; no error-severity diagnostics; and every edge endpoint resolving. It prints only counts and IDs, never labels, source text or expression text. It requires az login and Apim__ServiceName (documented in the script header and hints). Out of scope: committing any live content.",
  "ownerAgent": "qa-test-engineer",
  "dependencies": ["SPF-7", "POL-5"],
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
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-12",
      "docs/features/effective-policy-flow-model.md#POL-FR-01"
    ],
    "acceptanceCriteria": [
      "Against the live APIM, the reference scope passes every listed invariant, and all four scope kinds still return non-empty models",
      "Script output contains no policy text, expression text or label strings"
    ],
    "constraints": ["Never write fetched policy content to disk or stdout"],
    "constraintRefs": ["docs/PRD.md#VIS-SEC-01", "docs/PRD.md#VIS-SEC-02"],
    "references": ["docs/features/effective-policy-flow-model.md#POL-FR-04"]
  }
}
```

```forge-task
{
  "id": "SPF-18",
  "title": "Human review of semantic fidelity and visual clarity",
  "description": "A human reviewer runs the backend against the live APIM and the built UI, selects the reference scope (apis/universal-llm-api), and completes the primary journey: overview; expanding authentication, Responses ownership, model routing, the backend retry loop, Outbound and On-error; inspecting nodes and edges; and searching. The reviewer compares what is shown with the source and the research recommendations, and records the scopes exercised, findings and a pass/fail decision per criterion in the review file.",
  "dependencies": ["SPF-14", "SPF-15", "SPF-16", "SPF-17"],
  "expectedOutputs": [],
  "validationCommands": [],
  "contract": {
    "version": 2,
    "kind": "human-review",
    "reviewFile": "docs/reviews/semantic-policy-flow-review.json",
    "requirements": [],
    "requirementRefs": [
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-13",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-14",
      "docs/features/semantic-policy-flow-visualization.md#SPF-FR-16"
    ],
    "acceptanceCriteria": [
      "The reviewer completed the scope-to-overview-to-expand-to-inspect-to-search journey against the running live backend, and the notes name the scopes exercised",
      "The overview makes the five 'Bottom line' distinctions obvious: early termination, first-match selection, what retries repeat, state changes, and HTTP error versus On-error",
      "No node, edge or fact contradicts the source or the documented APIM semantics, and every inferred statement is marked Inferred",
      "The diagram is readable at 1280px and 1440px without horizontal page overflow, and upstream validation gaps reported in the Console were checked"
    ],
    "constraints": ["Reviewer notes must not include customer policy content"],
    "constraintRefs": [],
    "references": [
      "docs/research/visualizer-enhancements.md#Bottom line",
      "docs/features/semantic-policy-flow-visualization.md#SPF-US-01",
      "docs/features/semantic-policy-flow-visualization.md#SPF-US-02",
      "docs/features/semantic-policy-flow-visualization.md#SPF-US-03",
      "docs/features/semantic-policy-flow-visualization.md#SPF-US-04",
      "docs/features/semantic-policy-flow-visualization.md#SPF-US-05",
      "docs/features/semantic-policy-flow-visualization.md#SPF-US-06"
    ]
  }
}
```

---

## 10. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit (backend) | Loader, fragment regions, expression analyzer, model builder, rules, state | xUnit per component (SPF-1 to SPF-6) |
| Contract / integration (backend) | Endpoint, schema, status codes, legacy removal | `PolicyFlowControllerTests`, `LegacyFlowGraphRemovalTests` (SPF-7) |
| Semantic acceptance and backend performance | 18 research-derived invariants; 1 s p95 model build | `SemanticFlowAcceptanceTests` on the synthetic fixture (SPF-15) |
| Unit (frontend) | Projection invariants, layout geometry, renderers, search index | Vitest (SPF-8 to SPF-10, SPF-13) |
| Component / App integration | In-place toggle, toolbar, states, inspector, App wiring | Vitest + RTL with mocked API client (SPF-11, SPF-12) |
| Accessibility | Four UI states | `axe-core` + keyboard tests (SPF-14) |
| Browser performance / offline | SPF-NF-01 budgets, zero external requests | `semantic-flow-browser-smoke.mjs` with installed Edge (SPF-16) |
| Live | Reference-scope invariants | `live-smoke.mjs` (SPF-17) |
| Human | Fidelity and clarity | `docs/reviews/semantic-policy-flow-review.json` (SPF-18) |

Key scenarios:
1. Collapsing any subprocess never loses, merges or invents an outcome (property test).
2. An explicit 403 goes straight to the caller; a final backend 500 goes through Outbound; a
   validate-jwt failure goes to On-error; On-error never resumes Outbound.
3. The retry loop-back stays inside Backend and never re-runs Inbound.
4. A fragment repeated in outbound and on-error appears as two nodes, not one merged node.
5. Comments label things but never execute. A commented-out `include-fragment` is inert.
6. Expression analysis never compiles or executes code. Unrecognised forms are opaque, not guessed.

---

## 11. Rollback Considerations

- **Modified files:** `PolicyFlowController.cs`, `Program.cs`, `ApimPolicyVisualizer.Api.csproj`
  (Roslyn), the test `.csproj` (fixture copy), `UiFixtureHost/Program.cs`,
  `ui-fixture-smoke.mjs`, `live-smoke.mjs`, `App.tsx`, `App.integration.test.tsx`, `main.tsx`,
  `theme.css`, and `src/web/package.json` plus lockfile (elkjs).
- **Deleted files:** `PolicyFlowParser.cs`, `PolicyControlFlowParser.cs`, their tests, and
  `PolicyFlowDiagram*.tsx`. Rollback means reverting the SPF commits, which restores the
  `POL-2`/`POL-3`/`UI-3`/`UI-4` files and their suites.
- **New files** under `Policy/Source`, `Policy/Expressions`, `Policy/Model`, `src/web/src/flow`
  and `tests/smoke` can be removed together.
- There is no persistence, database or migration.
- The API contract change is breaking, but the only consumer is this repository's UI, which
  changes in the same feature.

---

## 12. Acceptance Criteria

1. `GET /api/policy/effective-flow` returns the schemaVersion 2 model with source spans,
   fragment-occurrence groups, typed edges, variable index and diagnostics. The 400/403/404/422
   behaviour is unchanged. The legacy parsers and diagram are gone.
2. All 18 semantic acceptance cases and the backend performance budget pass (SPF-15). The live reference scope passes its
   invariants (SPF-17).
3. The UI opens on the left-to-right lane overview with the On-error lane underneath,
   expands and collapses in place, and preserves every outcome in every collapse state (SPF-8
   property test).
4. The inspector shows exact source and provenance for every fact. Search focuses without
   mutating the graph.
5. Zero serious or critical axe violations in the four states. Zero external network requests.
   SPF-NF-01 budgets are met and recorded.
6. Every task's validation commands pass with at least one executed test, and the human review
   (SPF-18) is recorded.

---

## 13. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | What happens to pending `UI-5` (human review of the current diagram), which this feature replaces? | Leave `UI-5` pending and unexecuted. SPF-18 supersedes it once approved. Record it as superseded rather than approving it. |
| 2 | Should the browser smoke use the installed Microsoft Edge via `playwright-core`? | Yes. No browser download. The script fails with guidance if Edge is absent. |
| 3 | Are observability steps shown by default? | Yes, as compact nodes, with a toolbar toggle to hide them (edges reconnect). |
| 4 | Composite threshold for runs of ungrouped top-level steps? | Two or more consecutive simple steps. Decisions and loops are never absorbed into composites. |
| 5 | Do named values appear unresolved (`{{name}}`) in ARM effective output? | Assume yes. They are treated as configuration-dependent inputs and never resolved. |
| 6 | Is 300 KB the right performance ceiling? The largest live API seen is about 275 KB. | Yes for SPF-NF-01. Larger policies must still render correctly, with no timing guarantee. |


---

## 14. Running and Verifying

From the repository root (PowerShell):

```powershell
dotnet test tests\ApimPolicyVisualizer.Api.Tests\ApimPolicyVisualizer.Api.Tests.csproj
npm --prefix src\web run test -- run
npm --prefix src\web run build
```

**Synthetic (no Azure).** Start the loopback-only fixture host, which serves
`src/web/dist` and the real controller/model pipeline over the committed
synthetic fixture (`apis/example`), a ~300 KB / 2,700-line variant
(`apis/large`) and 403/404/422 error scopes:

```powershell
dotnet run --project tests\smoke\UiFixtureHost\UiFixtureHost.csproj
node tests\smoke\ui-fixture-smoke.mjs
npm --prefix tests\smoke install
node tests\smoke\semantic-flow-browser-smoke.mjs
```

The browser smoke uses the installed Microsoft Edge (`playwright-core`,
channel `msedge`, no browser download). It measures SPF-NF-01 timings, fails
on any request to a non-loopback host, checks real-browser Tab traversal and
Escape focus restoration, and writes
`docs/reviews/semantic-flow-performance.json`.

**Live.** With `az login` and `Apim__ServiceName` set, `pwsh -File .\start.ps1`
(or the `http` launch profile plus `npm --prefix src\web run dev`) serves
`http://localhost:5173`. `node tests\smoke\live-smoke.mjs` checks every
scope kind and the reference scope (`SMOKE_REFERENCE_SCOPE`, default
`apis/universal-llm-api`), printing only counts and IDs.

**Contract fixture.** `src/web/src/flow/__fixtures__/backendExampleModel.json`
is the backend's serialization of the synthetic fixture. The frontend
`contract.test.ts` runs projection/layout/search invariants against it, and the
backend `FrontendContractFixtureTests` fail if it is stale. Regenerate it with
`$env:UPDATE_CONTRACT_FIXTURE='1'; dotnet test ... --filter FullyQualifiedName~FrontendContractFixtureTests`.

### Verification record (2026-09-26)

| Check | Result |
|-------|--------|
| Backend suite (SPF-1–SPF-7, SPF-15, contract fixture) | 210 passed |
| Semantic acceptance (18 cases + 3 mutated-model negatives) | 21 passed |
| Backend build, scaled 307 KB / 2,500-line fixture | p50 67 ms, p95 89 ms (budget 1 s) |
| Frontend suite (SPF-8–SPF-14, backend-contract and layout-race regressions) | 48 passed across 13 files; production build passed |
| `ui-fixture-smoke.mjs` | Passed |
| Browser smoke (Edge headless, 1440×900, 20 runs) | Overview p95 704 ms (budget 2 s), expand/collapse p95 184 ms (500 ms), search p95 67 ms (200 ms); 30/30 nodes reached by Tab; Escape restores focus; 0 external requests |
| `live-smoke.mjs` against `apim-zlyway6g7icoy` | All scope kinds passed; reference `apis/universal-llm-api`: 331 elements, 335 edges, 17 fragment occurrences, 14 explicit responses, 1 backend loop, 3 stage-exception edges |

SPF-18 (human fidelity and visual review) is pending and cannot be satisfied by
these automated checks.
