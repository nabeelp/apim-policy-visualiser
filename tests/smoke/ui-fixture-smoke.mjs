// Run UiFixtureHost first (dotnet run --project tests/smoke/UiFixtureHost). Checks the schemaVersion 2 acceptance
// fixtures served by the real controller/model pipeline, not browser render timing (see semantic-flow-browser-smoke.mjs).
import assert from "node:assert/strict";

const baseUrl = "http://127.0.0.1:5022";

async function get(path) {
  return fetch(`${baseUrl}${path}`, { signal: AbortSignal.timeout(15_000) });
}

const healthResponse = await get("/healthz");
assert.equal(healthResponse.status, 200);
const health = await healthResponse.json();
assert.equal(health.mode, "synthetic-ui-acceptance", "Do not run against a live Azure backend");
assert(health.policyBytes >= 300 * 1024, "large fixture should be at least 300 KB");
assert(health.policyLines >= 2500, "large fixture should be at least 2,500 lines");

const catalogResponse = await get("/api/scopes");
assert.equal(catalogResponse.status, 200);
const catalog = await catalogResponse.json();
assert.equal(catalog.global.scopeId, "global");
assert.equal(catalog.products[0].apis[0].scopeId, catalog.apis[0].scopeId);

const requiredKinds = ["sequence", "branch", "explicit-response", "stage-exception", "loop-back", "raises-error", "data-dependency"];
for (const scope of ["apis/example", "apis/example/operations/read", "apis/large", "apis/legacy", "global"]) {
  const response = await get(`/api/policy/effective-flow?scope=${encodeURIComponent(scope)}`);
  assert.equal(response.status, 200, scope);
  const model = await response.json();
  assert.equal(model.schemaVersion, 2, scope);
  assert.equal(model.scopeId, scope);
  assert.deepEqual(model.stages.map((stage) => stage.name), ["inbound", "backend", "outbound", "on-error"]);
  assert.equal(typeof model.source.text, "string");
  const ids = new Set(model.elements.map((element) => element.id));
  assert.equal(ids.size, model.elements.length, `${scope}: element IDs are unique`);
  assert(model.elements.every((element) => !element.parentId || ids.has(element.parentId)), `${scope}: parents resolve`);
  assert(model.edges.every((edge) => ids.has(edge.from) && ids.has(edge.to)), `${scope}: edge endpoints resolve`);
  assert(model.elements.every((element) => !Object.hasOwn(element, "sourceElement")));
  if (scope.startsWith("apis/example") || scope === "apis/large") {
    const kinds = new Set(model.edges.map((edge) => edge.kind));
    for (const kind of requiredKinds) assert(kinds.has(kind), `${scope}: missing ${kind} edges`);
    assert(model.elements.some((element) => element.kind === "group" && element.fragment), `${scope}: fragment groups`);
  }
  console.log(`PASS ${scope}: ${model.elements.length} elements, ${model.edges.length} edges`);
}

for (const [scope, status, detail] of [
  ["apis/forbidden", 403, "Fixture identity cannot read this effective policy."],
  ["apis/missing", 404, "Fixture effective policy no longer exists."],
  ["apis/invalid", 422, "could not be parsed"],
]) {
  const response = await get(`/api/policy/effective-flow?scope=${encodeURIComponent(scope)}`);
  assert.equal(response.status, status, scope);
  assert.match(response.headers.get("content-type"), /application\/problem\+json/);
  const problem = await response.json();
  assert(problem.detail.includes(detail), scope);
  console.log(`PASS ${scope}: HTTP ${status} with descriptive problem detail`);
}

const page = await get("/");
assert.equal(page.status, 200);
assert.match(await page.text(), /<div id="root">/);
console.log(`PASS: built UI is served; large synthetic fixture is ${health.policyBytes} bytes / ${health.policyLines} lines.`);
