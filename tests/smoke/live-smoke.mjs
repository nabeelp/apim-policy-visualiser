#!/usr/bin/env node
// Live smoke test against the real Azure APIM instance (tasks POL-5 and SPF-17).
//
// Exercises GET /api/scopes and GET /api/policy/effective-flow for one scope of
// every kind (Global, Product, API, Operation) and fails unless each returns
// HTTP 200 with a non-empty schemaVersion 2 model. It then checks structural
// semantic invariants for SMOKE_REFERENCE_SCOPE (default apis/universal-llm-api).
// Only counts and IDs are printed; policy text, expressions and labels never are. Mocked unit tests cannot catch wrong ARM
// api-versions, unusable policy formats, or the wrong credential being chosen;
// this script exists to catch exactly those.
//
// Requirements: `az login` with read access to the APIM service, and
// `Apim__ServiceName` (or appsettings) naming the service. Reuses a backend that
// is already healthy at SMOKE_BASE_URL (default http://localhost:5021);
// otherwise starts one with the `http` launch profile and stops it afterwards.

import { spawn, spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const baseUrl = (process.env.SMOKE_BASE_URL ?? "http://localhost:5021").replace(/\/+$/, "");
const startupTimeoutMs = Number(process.env.SMOKE_STARTUP_TIMEOUT_MS ?? 180_000);
const requestTimeoutMs = 90_000;
const referenceScope = process.env.SMOKE_REFERENCE_SCOPE ?? "apis/universal-llm-api";

const hints = [
  "Run `az login` with an identity that can read the APIM service.",
  "On an Azure VM, DefaultAzureCredential prefers the managed identity; the `http` launch profile sets AZURE_TOKEN_CREDENTIALS=dev to use your CLI login.",
  "Set Apim__ServiceName (and optionally Apim__SubscriptionId / Apim__ResourceGroupName).",
  "Set SMOKE_REFERENCE_SCOPE to a scope ID with fragments, early responses and a backend retry (default apis/universal-llm-api).",
];

async function get(path) {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: { Accept: "application/json" },
    signal: AbortSignal.timeout(requestTimeoutMs),
  });
  const text = await response.text();
  let body;
  try { body = JSON.parse(text); } catch { body = text; }
  return { status: response.status, body };
}

async function healthy() {
  try { return (await get("/healthz")).status === 200; } catch { return false; }
}

function stopBackend(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === "win32") spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
  else { try { process.kill(-child.pid, "SIGTERM"); } catch { child.kill("SIGTERM"); } }
}

async function startBackend() {
  console.log(`[smoke] No healthy backend at ${baseUrl}; starting one with the http launch profile...`);
  const child = spawn("dotnet", [
    "run",
    "--project", join(repoRoot, "src", "server", "ApimPolicyVisualizer.Api", "ApimPolicyVisualizer.Api.csproj"),
    "--launch-profile", "http",
  ], { cwd: repoRoot, stdio: ["ignore", "pipe", "pipe"], detached: process.platform !== "win32" });
  let log = "";
  child.stdout.on("data", (chunk) => { log += chunk; });
  child.stderr.on("data", (chunk) => { log += chunk; });

  const deadline = Date.now() + startupTimeoutMs;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`Backend exited with code ${child.exitCode} before becoming healthy.\n${log.slice(-4000)}`);
    if (await healthy()) return child;
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  stopBackend(child);
  throw new Error(`Backend did not become healthy within ${startupTimeoutMs} ms.\n${log.slice(-4000)}`);
}

function describe(result) {
  const body = typeof result.body === "string" ? result.body : JSON.stringify(result.body);
  return `HTTP ${result.status}: ${body.slice(0, 600)}`;
}

/** Structural semantic invariants (SPF-17); returns failure messages containing only IDs and counts. */
function referenceFailures(model) {
  const failures = [];
  const elements = model.elements ?? [];
  const edges = model.edges ?? [];
  const byId = new Map(elements.map((element) => [element.id, element]));
  const isWithin = (id, ancestorId) => {
    for (let current = byId.get(id); current; current = current.parentId ? byId.get(current.parentId) : undefined) {
      if (current.id === ancestorId) return true;
    }
    return false;
  };

  if (model.schemaVersion !== 2) failures.push("schemaVersion is not 2");
  const stages = model.stages ?? [];
  for (const name of ["inbound", "backend", "outbound", "on-error"]) {
    if (!stages.some((stage) => stage.name === name && stage.present)) failures.push(`stage ${name} is not present`);
  }
  const groups = elements.filter((element) => element.kind === "group" && element.fragment);
  if (groups.length === 0) failures.push("no fragment-occurrence groups");
  const occurrences = new Map();
  for (const group of groups) occurrences.set(group.fragment.name, [...(occurrences.get(group.fragment.name) ?? []), group.fragment.occurrenceId]);
  for (const [name, ids] of occurrences) {
    if (new Set(ids).size !== ids.length) failures.push(`fragment ${name} has duplicate occurrence IDs`);
  }
  if (!edges.some((edge) => edge.kind === "explicit-response")) failures.push("no explicit-response edge");
  const presentNormal = stages.filter((stage) => stage.present && stage.name !== "on-error").length;
  const stageExceptions = edges.filter((edge) => edge.kind === "stage-exception");
  if (stageExceptions.length !== presentNormal) failures.push(`expected ${presentNormal} stage-exception edges, found ${stageExceptions.length}`);
  const loops = elements.filter((element) => element.kind === "loop" && element.stage === "backend");
  if (loops.length === 0) failures.push("no retry loop container in backend");
  for (const edge of edges.filter((candidate) => candidate.kind === "loop-back")) {
    const loop = loops.find((candidate) => isWithin(edge.from, candidate.id));
    if (!loop || !isWithin(edge.to, loop.id)) failures.push(`loop-back ${edge.id} leaves its loop container`);
  }
  const errors = (model.diagnostics ?? []).filter((diagnostic) => diagnostic.severity === "error");
  if (errors.length) failures.push(`${errors.length} error-severity diagnostics (${errors.map((d) => d.code).join(", ")})`);
  const dangling = edges.filter((edge) => !byId.has(edge.from) || !byId.has(edge.to));
  if (dangling.length) failures.push(`${dangling.length} edges with unresolved endpoints`);
  const orphans = elements.filter((element) => element.parentId && !byId.has(element.parentId));
  if (orphans.length) failures.push(`${orphans.length} elements with unresolved parentId`);

  if (failures.length === 0) {
    console.log(`[smoke] PASS reference ${model.scopeId}: ${elements.length} elements, ${edges.length} edges, ` +
      `${groups.length} fragment occurrences, ${edges.filter((e) => e.kind === "explicit-response").length} explicit responses, ` +
      `${loops.length} backend loop(s), ${stageExceptions.length} stage-exception edges`);
  }
  return failures;
}

async function main() {
  const scopes = await get("/api/scopes");
  if (scopes.status !== 200) throw new Error(`GET /api/scopes failed. ${describe(scopes)}`);

  const catalog = scopes.body;
  const allApis = [...(catalog.apis ?? []), ...(catalog.products ?? []).flatMap((product) => product.apis ?? [])];
  const apiWithOperations = allApis.find((api) => (api.operations ?? []).length > 0);
  const targets = [
    { kind: "Global", scopeId: catalog.global?.scopeId },
    { kind: "Product", scopeId: catalog.products?.[0]?.scopeId },
    { kind: "API", scopeId: allApis[0]?.scopeId },
    { kind: "Operation", scopeId: apiWithOperations?.operations?.[0]?.scopeId },
  ];

  const failures = [];
  for (const target of targets) {
    if (!target.scopeId) {
      failures.push(`${target.kind}: the scope catalog contains no ${target.kind} scope to exercise.`);
      continue;
    }
    const flow = await get(`/api/policy/effective-flow?scope=${encodeURIComponent(target.scopeId)}`);
    if (flow.status !== 200) {
      failures.push(`${target.kind} '${target.scopeId}': ${describe(flow)}`);
      continue;
    }
    const elements = flow.body?.elements ?? [];
    const edges = flow.body?.edges ?? [];
    if (flow.body?.schemaVersion !== 2 || elements.length === 0) {
      failures.push(`${target.kind} '${target.scopeId}': expected a non-empty schemaVersion 2 model.`);
      continue;
    }
    // Conditions must be the authored expression text, not XML-escaped transport text.
    const escaped = edges.find((edge) => typeof edge.condition?.text === "string" && /&(?:quot|gt|lt|amp|apos);/.test(edge.condition.text));
    if (escaped) {
      failures.push(`${target.kind} '${target.scopeId}': edge ${escaped.id} condition is still XML-escaped.`);
      continue;
    }
    console.log(`[smoke] PASS ${target.kind.padEnd(9)} ${target.scopeId} -> ${elements.length} elements, ${edges.length} edges`);
  }

  const reference = await get(`/api/policy/effective-flow?scope=${encodeURIComponent(referenceScope)}`);
  if (reference.status !== 200) failures.push(`Reference '${referenceScope}': ${describe(reference)}`);
  else failures.push(...referenceFailures(reference.body).map((message) => `Reference '${referenceScope}': ${message}`));

  if (failures.length > 0) throw new Error(`Live smoke failed:\n- ${failures.join("\n- ")}`);
  console.log("[smoke] All scope kinds returned a non-empty model and the reference scope met every semantic invariant.");
}

let backend;
try {
  if (await healthy()) console.log(`[smoke] Reusing healthy backend at ${baseUrl}`);
  else backend = await startBackend();
  await main();
} catch (error) {
  console.error(`[smoke] ${error instanceof Error ? error.message : String(error)}`);
  console.error(`[smoke] Hints:\n  - ${hints.join("\n  - ")}`);
  process.exitCode = 1;
} finally {
  stopBackend(backend);
}
