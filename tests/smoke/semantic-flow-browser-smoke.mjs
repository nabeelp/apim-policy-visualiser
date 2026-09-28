#!/usr/bin/env node
// Browser performance, offline and keyboard smoke for the semantic policy flow view (task SPF-16).
//
// Run from the repository root:
//   npm --prefix src/web run build          # the fixture host serves src/web/dist
//   npm --prefix tests/smoke install        # playwright-core only; no browser download
//   node tests/smoke/semantic-flow-browser-smoke.mjs
//
// Uses the locally installed Microsoft Edge (playwright channel "msedge") in headless mode and the synthetic,
// loopback-only UiFixtureHost at http://127.0.0.1:5022 (reused when healthy, otherwise started and stopped here).
// It never contacts Azure. Fails when any request leaves 127.0.0.1/localhost, when an SPF-NF-01 p95 budget is
// exceeded, or when keyboard traversal/focus restoration fails. Raw timings are written to
// docs/reviews/semantic-flow-performance.json.

import { spawn, spawnSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { cpus, totalmem, platform, release } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const baseUrl = "http://127.0.0.1:5022";
const runs = Number(process.env.SMOKE_RUNS ?? 20);
const budgets = { overviewMs: 2000, toggleMs: 500, searchMs: 200 };
const reportPath = join(repoRoot, "docs", "reviews", "semantic-flow-performance.json");

let chromium;
try {
  ({ chromium } = await import("playwright-core"));
} catch {
  console.error("[browser-smoke] playwright-core is missing. Run `npm --prefix tests/smoke install`.");
  process.exit(1);
}

async function healthy() {
  try {
    const response = await fetch(`${baseUrl}/healthz`, { signal: AbortSignal.timeout(3000) });
    return response.ok && (await response.json()).mode === "synthetic-ui-acceptance";
  } catch {
    return false;
  }
}

function stop(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === "win32") spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
  else child.kill("SIGTERM");
}

async function startHost() {
  const child = spawn("dotnet", ["run", "--project", join(repoRoot, "tests", "smoke", "UiFixtureHost", "UiFixtureHost.csproj")], {
    cwd: repoRoot,
    stdio: ["ignore", "pipe", "pipe"],
  });
  let log = "";
  child.stdout.on("data", (chunk) => { log += chunk; });
  child.stderr.on("data", (chunk) => { log += chunk; });
  const deadline = Date.now() + 180_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`Fixture host exited early:\n${log.slice(-3000)}`);
    if (await healthy()) return child;
    await new Promise((resolve) => setTimeout(resolve, 1500));
  }
  stop(child);
  throw new Error(`Fixture host did not become healthy:\n${log.slice(-3000)}`);
}

function percentile(values, p) {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)];
}

const summarize = (values) => ({
  runs: values.length,
  p50: Math.round(percentile(values, 50)),
  p95: Math.round(percentile(values, 95)),
  max: Math.round(Math.max(...values)),
  raw: values.map((value) => Math.round(value)),
});

const paintedFrames = () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));

async function openScope(page, label, scopeId) {
  await page.goto(`${baseUrl}/`);
  const select = page.getByRole("combobox", { name: "API" });
  await select.locator("option", { hasText: label }).first().waitFor({ state: "attached" });
  const responseDone = page
    .waitForResponse((response) => response.url().includes("/api/policy/effective-flow") && response.url().includes(encodeURIComponent(scopeId)))
    .then(async (response) => { await response.finished(); return Date.now(); });
  await select.selectOption({ label });
  const received = await responseDone;
  await page.locator('.react-flow__node[data-id="stage:inbound"]').waitFor();
  await page.locator(".flow-node__body").first().waitFor();
  await page.evaluate(paintedFrames);
  return Date.now() - received;
}

const failures = [];
const external = [];
let host;
let browser;
try {
  if (!(await healthy())) host = await startHost();
  try {
    browser = await chromium.launch({ channel: "msedge", headless: true });
  } catch (error) {
    throw new Error(`Microsoft Edge could not be launched via playwright-core (channel msedge). Install Edge; this smoke never downloads browsers.\n${error.message}`);
  }
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  context.on("request", (request) => {
    const host = new URL(request.url()).hostname;
    if (!["127.0.0.1", "localhost"].includes(host) && !request.url().startsWith("data:")) external.push(request.url());
  });
  const page = await context.newPage();

  // 1. Response received -> overview painted, 300 KB / 2,500+ line synthetic policy.
  const overview = [];
  for (let run = 0; run < runs; run += 1) overview.push(await openScope(page, "300 KB policy", "apis/large"));
  const largeNodes = await page.locator(".react-flow__node").count();

  // 2. Expand/collapse one subprocess (<=150 visible nodes) on the example policy.
  await openScope(page, "Example API", "apis/example");
  const toggle = [];
  let maxVisible = 0;
  for (let run = 0; run < runs; run += 1) {
    const button = page.locator('.react-flow__node[data-id="backend/0"] .flow-node__toggle').first();
    const wasExpanded = (await button.getAttribute("aria-expanded")) === "true";
    const started = Date.now();
    await button.dispatchEvent("click");
    await page.waitForFunction(
      (expected) => document.querySelector('.react-flow__node[data-id="backend/0"] .flow-node__toggle')?.getAttribute("aria-expanded") === expected,
      String(!wasExpanded),
    );
    await page.waitForFunction(
      (expanded) => Boolean(document.querySelector('.react-flow__node[data-id^="backend/0/"]')) === expanded,
      !wasExpanded,
    );
    await page.evaluate(paintedFrames);
    toggle.push(Date.now() - started);
    maxVisible = Math.max(maxVisible, await page.locator(".react-flow__node").count());
  }
  if (maxVisible > 150) failures.push(`toggle scenario rendered ${maxVisible} nodes (>150)`);

  // 3. Search latency.
  const search = [];
  const box = page.getByRole("combobox", { name: /search/i });
  const queries = ["403", "forward", "responseId", "set-header", "cors"];
  for (let run = 0; run < runs; run += 1) {
    await box.fill("");
    const query = queries[run % queries.length];
    const started = Date.now();
    await box.fill(query);
    await page.getByRole("listbox", { name: "Policy flow search results" }).getByRole("option").first().waitFor();
    await page.evaluate(paintedFrames);
    search.push(Date.now() - started);
  }
  await box.fill("");

  // 4. Real-browser keyboard traversal and focus restoration on the overview.
  await openScope(page, "Example API", "apis/example");
  const visibleIds = await page.$$eval(".flow-node__body[data-flow-node-id]", (els) => els.map((el) => el.getAttribute("data-flow-node-id")));
  await page.locator(".flow-node__body").first().focus();
  const reached = [];
  for (let press = 0; press < visibleIds.length * 3 + 20 && reached.length < visibleIds.length; press += 1) {
    const id = await page.evaluate(() => document.activeElement?.getAttribute("data-flow-node-id") ?? null);
    if (id && !reached.includes(id)) reached.push(id);
    await page.keyboard.press("Tab");
  }
  const missed = visibleIds.filter((id) => !reached.includes(id));
  if (missed.length) failures.push(`Tab traversal missed ${missed.length} visible nodes: ${missed.slice(0, 5).join(", ")}`);
  const order = reached.map((id) => visibleIds.indexOf(id));
  if (order.some((value, index) => index > 0 && value < order[index - 1])) failures.push("Tab traversal order differs from document order");

  const target = page.locator('.flow-node__body[data-flow-node-id="inbound/0"]');
  await target.focus();
  await target.click();
  await page.getByRole("complementary", { name: "Flow inspector" }).or(page.locator('[aria-label="Flow inspector"]')).first().waitFor();
  await page.keyboard.press("Escape");
  await page.waitForTimeout(200);
  const focused = await page.evaluate(() => document.activeElement?.getAttribute("data-flow-node-id") ?? null);
  if (focused !== "inbound/0") failures.push(`Escape from the inspector focused '${focused}' instead of the selected node`);

  const result = {
    overview: summarize(overview),
    toggle: summarize(toggle),
    search: summarize(search),
  };
  for (const [name, budget] of [["overview", budgets.overviewMs], ["toggle", budgets.toggleMs], ["search", budgets.searchMs]]) {
    if (result[name].p95 > budget) failures.push(`${name} p95 ${result[name].p95} ms exceeds ${budget} ms`);
  }
  if (external.length) failures.push(`${external.length} external requests, e.g. ${external[0]}`);

  const report = {
    generatedAt: new Date().toISOString(),
    requirement: "SPF-NF-01 / SPF-SEC-01 / SPF-A11Y-01",
    machine: {
      platform: `${platform()} ${release()}`,
      cpu: cpus()[0]?.model,
      logicalCores: cpus().length,
      memoryGiB: Math.round(totalmem() / 2 ** 30),
      browser: `Microsoft Edge ${browser.version()} (headless)`,
      viewport: "1440x900",
    },
    fixture: { scope: "apis/large", overviewNodes: largeNodes, toggleScope: "apis/example", toggleMaxVisibleNodes: maxVisible },
    budgetsMs: budgets,
    results: result,
    externalRequests: external.length,
    keyboard: { visibleNodes: visibleIds.length, reached: reached.length, escapeRestoresFocus: focused === "inbound/0" },
    passed: failures.length === 0,
    failures,
  };
  mkdirSync(dirname(reportPath), { recursive: true });
  writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`);
  for (const [name, value] of Object.entries(result)) {
    console.log(`[browser-smoke] ${name.padEnd(8)} p50 ${value.p50} ms, p95 ${value.p95} ms over ${value.runs} runs`);
  }
  console.log(`[browser-smoke] keyboard: ${reached.length}/${visibleIds.length} nodes reached; external requests: ${external.length}`);
  if (failures.length) throw new Error(failures.join("\n- "));
  console.log(`[browser-smoke] PASS; report written to ${reportPath}`);
} catch (error) {
  console.error(`[browser-smoke] FAIL:\n- ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
} finally {
  await browser?.close();
  stop(host);
}
