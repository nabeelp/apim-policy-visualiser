# ARM Error Mapping Convention

> Load when: implementing or reviewing the failure-response handling of an
> ARM-calling component (`ArmTokenProvider`, `EffectivePolicyClient`, or any
> future ARM client in this project).

Every component that issues an authenticated ARM call must map the HTTP
response to one of the outcomes below. Do not let an unmapped status code
propagate as a generic 500 — the endpoint layer (`ScopesController`,
`PolicyFlowController`) relies on this mapping to choose its own HTTP status.

| ARM HTTP status | Typed result | Descriptive message pattern | Endpoint-layer status |
|------------------|--------------|------------------------------|------------------------|
| 200 (or other 2xx) | `Success<T>` payload | n/a | 200 |
| 404 | `NotFound` | `"{ResourceKind} '{identifier}' was not found."` | 404 |
| 403 | `Forbidden` | `"Access to '{identifier}' was denied. Verify the caller's role assignment."` | 403 |
| any other non-2xx | Let it surface as an unmapped exception | n/a | 500 (unexpected) |

## Rules

1. **Identifier in the message, not the raw ARM response body.** Include the
   resource identifier (service name, scope ID) the caller asked for so the
   message is actionable. Do not echo the raw ARM error JSON — it may
   contain internal identifiers not meant for the API consumer.
2. **404 and 403 are structurally distinct types**, not the same type with a
   different status code field. This lets the controller layer pattern-match
   without inspecting a magic number.
3. **Every consumer (`ArmTokenProvider`'s downstream callers,
   `EffectivePolicyClient`) uses the same two typed results** — do not invent
   a second `NotFound`/`Forbidden` pair in a different namespace. Both
   `apim-connectivity-engineer` and `policy-parsing-engineer` share this
   mapping so their endpoints behave consistently.
4. **Test both branches independently.** A test double or mocked
   `HttpMessageHandler` returning 404 and one returning 403 should each be
   asserted against the corresponding typed result and message content —
   do not rely on the presence of an exception alone.
