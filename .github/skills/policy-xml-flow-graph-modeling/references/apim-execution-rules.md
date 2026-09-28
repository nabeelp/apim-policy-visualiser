# APIM Execution Rules for the Flow Model

> Load when: adding or changing element kinds, edge kinds or `ApimSemanticRules`.

## Edge kinds

| Kind | From → To | Meaning |
|------|-----------|---------|
| `sequence` | element → next element | Ordinary execution order |
| `branch` | decision → first element of a `when` | Priority-ordered, first match wins; carries `priority` and `condition` |
| `otherwise` | decision → first element of `otherwise` | Taken when no `when` matched |
| `no-match` | decision → merge | `choose` without `otherwise` |
| `bypass` | decision → merge | Empty `when`/`otherwise` |
| `merge` | open exit → merge | Branch rejoins |
| `loop-back` | loop-test → first body element | Retry repeats (stays in container) |
| `loop-exit` | loop-test → continuation | Retry finished or not applicable |
| `explicit-response` | `return-response` → explicit-response terminal | Pipeline ends immediately |
| `raises-error` | error-raising step → On-error entry / gateway default | Policy failure |
| `stage-exception` | stage → On-error entry / gateway default | Any unhandled failure in the stage |
| `stage` | entry/stage → next stage/terminal | Stage ordering |
| `preflight` | preflight entry → cors → preflight terminal | CORS preflight route |
| `data-dependency` | cache-store-value → cache-lookup-value | Affects subsequent requests; not control flow |

## Error-raising predicates (`raises-error`)

| Tag | Raises when |
|-----|-------------|
| `forward-request` | Always (transport); also HTTP status when `fail-on-error-status-code="true"` |
| `send-request` | Unless `ignore-error="true"` |
| `validate-jwt`, `validate-azure-ad-token`, `check-header`, `ip-filter`, `rate-limit`, `rate-limit-by-key`, `quota`, `quota-by-key`, `limit-concurrency`, `llm-token-limit`, `azure-openai-token-limit` | Always |
| `validate-content`, `validate-parameters`, `validate-headers`, `validate-status-code` | Only if an action attribute is `prevent` (expression-valued → configuration-dependent) |

## Terminals

- `Explicit response to caller` — every `return-response` (also inside On-error).
- `Final backend response to caller` — end of Outbound.
- `Error response to caller` — non-terminating end of On-error.
- `Gateway default error response` — when no `on-error` section exists.
- `Preflight response to caller` — CORS preflight route.

## `return-response` status

`set-status` child → code + reason; `response-variable-name` → "status from
variable"; neither → `200 OK (no body)` (APIM default).

## Model structure conventions (schemaVersion 2)

Contract types: `src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModel.cs`
and its mirror `src/web/src/flow/model.ts`.

### Containment (`parentId`)

- Top level (`parentId: null`, `stage: null`): `entry:request`, `entry:preflight`
  (only with cors), the four stage containers `stage:inbound`, `stage:backend`,
  `stage:outbound`, `stage:on-error` (always emitted; `stages[].present` says
  whether the section exists), and the terminals that are referenced by an edge:
  `terminal:final-response`, `terminal:explicit-response`,
  `terminal:error-response` (on-error present), `terminal:gateway-default-error`
  (on-error absent), `terminal:preflight-response` (cors present).
- `entry:on-error` is the first child (`order: 0`) of `stage:on-error` when the
  section is present.
- Containers: `stage`, `group` (fragment occurrence), `choose`, `loop` (retry).
  - `choose` children in order: its `decision` (order 0), then every branch body
    element in clause order, then its `merge` (last, only if some branch
    continues).
  - `loop` children: body elements, then its `loop-test` (last).
- Leaves: `step`, `opaque`, `decision`, `merge`, `loop-test`, `entry`,
  `terminal`.

### IDs (stable, structural)

- Stage children: `inbound/0`, `inbound/1`, …; nested: `inbound/3/0`.
- Fragment group: `<parentPath>/fragment:<occurrenceId>` (e.g.
  `inbound/fragment:security-handler#1`); its children continue the path
  (`inbound/fragment:security-handler#1/0`).
- Decision / merge / loop test: `<containerId>/decision`, `<containerId>/merge`,
  `<containerId>/test`.
- Edge IDs: `e:<from>-><to>:<kind>[:<priority>]`, unique.

### Edges

- Control edges connect **leaf** elements, with two exceptions:
  `stage-exception` goes from a stage container to `entry:on-error` (or
  `terminal:gateway-default-error`), and `data-dependency` connects leaves in
  different stages.
- `entry:request` → first leaf of Inbound (`sequence`); open exits flow across
  stages with `sequence` edges (skipping empty stages); Outbound's open exits →
  `terminal:final-response`; On-error open exits → `terminal:error-response`.
- `explicit-response` edges all target `terminal:explicit-response`.
- `raises-error` edges target `entry:on-error` (or the gateway-default terminal).
- A `branch` edge carries `priority` (1-based) and `condition`; `otherwise`,
  `no-match` and `bypass` target the first body leaf or the merge.
- `loop-back`: loop test → first body leaf; `loop-exit`: loop test → continuation.

### Labels

Backend produces human labels (`Set variable requestedModel`,
`Return 403 Forbidden`, `Forward request to backend`, `Choose: <first
summary or "3 branches">`, `Retry while <summary>`); fragment groups use the
SPF-FR-02 precedence and repeated occurrences append ` · <stage>` when the
fragment occurs in more than one stage.
