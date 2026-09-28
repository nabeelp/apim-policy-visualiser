---
name: azure-arm-authenticated-client
description: "Acquire and cache DefaultAzureCredential ARM bearer tokens, pin an explicit ARM api-version, and map ARM 404/403 responses to descriptive typed results for APIM management-plane calls."
---

# Skill: Azure ARM Authenticated Client

Use this skill whenever a component needs to call an Azure Resource Manager
(ARM) endpoint for the APIM Effective Policy Flow Visualizer — for example
`ArmTokenProvider` (scope discovery) or `EffectivePolicyClient` (effective
policy retrieval). It codifies one token-acquisition/caching implementation
and one 404/403 error-mapping convention so both consumers behave
identically instead of drifting.

Load `references/error-mapping.md` now. It defines the exact typed-result
shape both `ArmTokenProvider` and `EffectivePolicyClient` must use for
ARM failure responses.

---

## Process

### Step 1: Acquire the credential

Construct exactly one `Azure.Identity.DefaultAzureCredential` instance per
process (register it as a singleton in DI). Do not create a new instance
per request — `DefaultAzureCredential` internally probes multiple credential
sources (environment, managed identity, Azure CLI) and that probe is
expensive to repeat.

**Inputs needed:**
- Target scope: `https://management.azure.com/.default`

**Output:**
- A single shared `TokenCredential` used by every ARM-calling component.

### Step 2: Cache the token

Wrap the credential in a token provider that caches the acquired
`AccessToken` and only requests a new one when the cached token is within
5 minutes of `ExpiresOn`. Compare against `DateTimeOffset.UtcNow`, not local
time. Guard the refresh check with a lock (or `SemaphoreSlim`) so concurrent
callers do not each trigger a redundant credential call.

- If the cached token has more than 5 minutes remaining: return it unchanged.
- If it has 5 minutes or less remaining, or none is cached: request a new
  token and replace the cache before returning it.

**Output:**
- A bearer token string ready to attach as `Authorization: Bearer {token}`.

### Step 3: Never log the credential or token

Do not pass the `TokenCredential`, the raw token string, or the `Authorization`
header value to any logger, exception message, or trace span. When writing a
test for this, assert on captured log output rather than trusting a code
review — log redaction bugs are silent failures.

### Step 4: Pin the ARM api-version explicitly

Every ARM request built on top of this credential must include a literal,
hard-coded `api-version` query parameter (for example
`api-version=2024-05-01` for the effective-policy call). Never omit
it or resolve it dynamically — ARM's default api-version changes over time
and would silently alter response shape. Verify the pinned version is actually
registered for `Microsoft.ApiManagement/service` (an unregistered version fails
with HTTP 400 `NoRegisteredProviderFound`, which mocked-handler tests won't catch).

**Output:**
- A fully-qualified ARM request URL with an explicit `api-version`.

### Step 5: Map ARM failure responses

After issuing the authenticated call, inspect the HTTP status code and map
it using the convention in `references/error-mapping.md` before returning
control to the caller. Every mapped result must carry a human-readable
message describing what was not found or not authorized — never a bare
status code.

**Output:**
- A typed result: success payload, `NotFound` (404), or `Forbidden` (403),
  each with a descriptive message.

---

## Gotchas

- **Redundant `DefaultAzureCredential` construction defeats caching.** Creating
  a new instance per request re-runs the full credential probe chain even if
  your token cache is otherwise correct — always share one instance via DI.
- **Comparing `ExpiresOn` against local time silently shortens or extends the
  cache window.** `AccessToken.ExpiresOn` is UTC; always compare against
  `DateTimeOffset.UtcNow`.
- **Logging the full exception from a failed ARM call can leak the bearer
  token** if the HTTP client's exception message includes request headers —
  strip or redact the `Authorization` header before logging any ARM call
  failure.
- **Skipping the api-version pin passes silently until ARM changes its
  default**, then the response shape changes with no compile-time or
  immediate runtime signal — always assert the pinned version in tests.
- **A pinned version asserted only by mocked tests can be one that does not
  exist.** `2022-08-01-preview` shipped with passing tests and failed live with
  HTTP 400 `NoRegisteredProviderFound`. Confirm the version live (`node
  tests/smoke/live-smoke.mjs`) whenever it changes.
- **`DefaultAzureCredential` picks a managed identity before `az login`** on
  Azure VMs, App Service and similar hosts, so local runs get 403 from an
  identity with no RBAC. Local development sets `AZURE_TOKEN_CREDENTIALS=dev`
  (in the `http`/`https` launch profiles) to restrict the chain to developer
  credentials; do not add it to production configuration.
- **Effective policy must use `format=xml`, not `rawxml`.** `rawxml` returns
  policy expressions unescaped (`condition="@(... "x" ...)"`), which is not
  well-formed XML; `format=xml` escapes them one extra level (see
  `policy-xml-flow-graph-modeling`).

---

## Validation

After implementing or extending an ARM-calling component, verify:

- [ ] A single `DefaultAzureCredential`/token-provider instance is shared,
      not constructed per call (check DI registration lifetime).
- [ ] A unit test proves the cached token is reused when more than 5 minutes
      remain and refreshed when at or below 5 minutes, without a live Azure
      call.
- [ ] A unit test asserts captured log/trace output never contains the raw
      token or credential value.
- [ ] Every ARM request URL includes the literal pinned `api-version`.
- [ ] 404 and 403 responses map to distinct typed results with descriptive
      messages (see `references/error-mapping.md`), never a generic 500.
- [ ] `node tests/smoke/live-smoke.mjs` passes against the real service after
      any change to an ARM URL, api-version, query parameter or credential
      setup — mocked tests cannot prove these.

If any item fails: fix the specific component (token cache, logging, or
error mapping) — do not weaken the assertions to make the test pass.
