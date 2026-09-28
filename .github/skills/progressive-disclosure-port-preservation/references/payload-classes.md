# Projected Edge Payload Classes

> Load when: deciding whether two lifted edges may merge into one projected edge.

Merge key: `(rep(source), rep(target), kind, payloadClass)`.

| Edge kind | payloadClass | Label after merge |
|-----------|--------------|-------------------|
| `branch` | `priority:<n>` | Condition summary of that priority |
| `otherwise`, `no-match`, `bypass` | kind name | Fixed label |
| `explicit-response` | `explicit` | Union of status codes, ascending, joined with ` / ` |
| `raises-error`, `stage-exception` | `error` | "raises error" / "unhandled failure" |
| `sequence`, `merge`, `stage`, `loop-exit`, `preflight` | `flow` | None |
| `loop-back` | `loop` | Internal only: becomes a node badge when collapsed |
| `data-dependency` | `data` | "affects subsequent requests" |

Status codes of merged explicit-response edges are unioned into the label; all
other payload differences keep edges apart.
