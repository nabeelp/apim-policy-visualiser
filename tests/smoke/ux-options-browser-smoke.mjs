// Build src\web and start UiFixtureHost, then run:
// node tests\smoke\ux-options-browser-smoke.mjs <screenshot-directory>
import assert from "node:assert/strict";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

const output = process.argv[2];
assert(output, "Provide a directory for the UX comparison screenshots.");
const directory = resolve(output);
mkdirSync(directory, { recursive: true });
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const baseUrl = "http://127.0.0.1:5022";
const health = await (await fetch(`${baseUrl}/healthz`)).json();
assert.equal(health.mode, "synthetic-ui-acceptance", "Only use the local synthetic fixture host.");

const browser = await chromium.launch({ channel: "msedge", headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
const failures = [];
const metrics = {};
page.on("pageerror", (error) => failures.push(error.message));
await page.route("**/*", async (route) => {
  const url = new URL(route.request().url());
  if (!["127.0.0.1", "localhost"].includes(url.hostname)) {
    failures.push(`Unexpected external request: ${url.origin}`);
    await route.abort();
  } else {
    await route.continue();
  }
});

async function settled() {
  await page.evaluate(() => new Promise((done) =>
    requestAnimationFrame(() => requestAnimationFrame(done))));
}

async function screenshot(name) {
  await settled();
  await page.screenshot({ path: join(directory, name) });
}

async function checkViewport(label) {
  await page.waitForFunction(() =>
    document.documentElement.scrollHeight <= innerHeight + 2 &&
    document.documentElement.scrollWidth <= innerWidth, null, { timeout: 3000 });
  const sizes = await page.evaluate(() => {
    const inspector = document.querySelector(".flow-inspector");
    return {
      viewportHeight: innerHeight,
      viewportWidth: innerWidth,
      documentHeight: document.documentElement.scrollHeight,
      documentWidth: document.documentElement.scrollWidth,
      inspectorOverflow: inspector ? inspector.scrollWidth - inspector.clientWidth : 0,
    };
  });
  assert(sizes.documentHeight <= sizes.viewportHeight + 2, `${label}: page overflows vertically: ${JSON.stringify(sizes)}`);
  assert(sizes.documentWidth <= sizes.viewportWidth, `${label}: page overflows horizontally`);
  assert(sizes.inspectorOverflow <= 1, `${label}: inspector has unwanted horizontal overflow`);
}

async function checkA11y(label) {
  const violations = await page.evaluate(async () => {
    const result = await window.axe.run(document.querySelector(".semantic-flow"), { resultTypes: ["violations"] });
    return result.violations
      .filter((item) => ["serious", "critical"].includes(item.impact))
      .map((item) => ({ id: item.id, targets: item.nodes.map((node) => node.target) }));
  });
  assert.deepEqual(violations, [], `${label}: accessibility violations`);
}

async function checkCompactHeader(label) {
  const header = await page.locator(".app-header").boundingBox();
  const canvas = await page.locator(".flow-workspace").boundingBox();
  const viewport = page.viewportSize();
  assert(header && canvas && viewport, `${label}: header and workspace are visible`);
  assert(header.height <= 48, `${label}: app header exceeds 48px`);
  assert(canvas.y <= 180, `${label}: workspace starts below 180px: ${canvas.y}`);
  assert(canvas.height >= viewport.height * 0.75, `${label}: workspace must use at least 75% of the viewport`);
  metrics[label] = { headerHeight: header.height, canvasTop: canvas.y, canvasHeight: canvas.height };
}

try {
  await page.goto(baseUrl);
  await page.getByRole("combobox", { name: "API", exact: true }).selectOption({ label: "Example API" });
  await page.getByRole("button", { name: "Flow map", exact: true }).waitFor();
  assert.deepEqual(await page.getByRole("group", { name: "Visualization views" }).getByRole("button").allTextContents(),
    ["Flow map", "Stage board", "Reading view"]);
  assert.equal(await page.getByRole("button", { name: "Flow map", exact: true }).getAttribute("aria-pressed"), "true");
  assert.equal(await page.getByRole("button", { name: "Detailed graph", exact: true }).count(), 0);
  await checkCompactHeader("1440px sidebar open");
  await page.setViewportSize({ width: 1280, height: 720 });
  await checkViewport("1280x720 Flow map with scope sidebar");
  await checkCompactHeader("1280x720 sidebar open");
  await page.getByText("Display options", { exact: true }).click();
  await page.getByRole("checkbox", { name: "Data dependencies" }).waitFor();
  await page.getByText("Display options", { exact: true }).click();
  await page.getByText("Legend", { exact: true }).click();
  const legend = await page.locator(".flow-legend__content").boundingBox();
  assert(legend && legend.x >= 0 && legend.x + legend.width <= 1280, "Legend stays inside the viewport");
  await page.getByText("Legend", { exact: true }).click();
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.getByRole("button", { name: "Collapse scope selection panel" }).click();
  await page.addScriptTag({ path: join(root, "src", "web", "node_modules", "axe-core", "axe.min.js") });

  await page.getByRole("button", { name: "Expand Enforce model allow-list", exact: true }).click();
  await page.getByRole("button", { name: "Expand Choose: variable requestedModel is empty", exact: true }).click();
  await page.locator(".flow-edge--branch").first().waitFor();
  await page.getByRole("button", { name: "Fit view", exact: true }).click();
  await page.getByRole("button", { name: "Collapse Enforce model allow-list", exact: true }).click();
  await page.getByRole("button", { name: "Expand Enforce model allow-list", exact: true }).click();
  await page.locator(".flow-edge--branch").first().waitFor();
  const group = page.locator('.react-flow__node[data-id="inbound/fragment:model-allowlist#1"]');
  await page.waitForFunction(() => {
    const group = document.querySelector('.react-flow__node[data-id="inbound/fragment:model-allowlist#1"]')?.getBoundingClientRect();
    const canvas = document.querySelector(".flow-canvas")?.getBoundingClientRect();
    return group && canvas && group.x >= canvas.x && group.y >= canvas.y &&
      group.right <= canvas.right && group.bottom <= canvas.bottom;
  }, null, { timeout: 10_000 });
  await settled();
  metrics.quiet = {
    edges: await page.locator(".flow-edge").count(),
    labels: await page.locator(".flow-edge__label").count(),
    groupBounds: await group.boundingBox(),
  };
  assert.equal(metrics.quiet.labels, 0, "Flow map should hide idle edge labels, not the edges.");
  await screenshot("01-flow-map.png");
  await checkViewport("Flow map");

  const outcome = page.locator(".flow-edge--explicit-response").first();
  await outcome.focus();
  assert(await page.locator(".flow-edge__label").count() > 0, "Keyboard focus must reveal an outcome label.");
  assert.equal(await page.locator(".flow-edge").count(), metrics.quiet.edges, "Revealing a label must preserve all projected edges.");
  await page.getByRole("button", { name: "Flow map", exact: true }).focus();
  assert.equal(await page.locator(".flow-edge__label").count(), 0, "Labels should disappear when focus leaves an edge.");

  await page.getByRole("button", { name: "Stage board", exact: true }).click();
  await page.getByRole("button", { name: "Collapse to overview" }).click();
  await page.getByRole("button", { name: /^Expand Retry while/ }).click();
  await page.getByRole("button", { name: /^Forward request/ }).waitFor();
  // Reset the stage's scroll after expanding so its heading and parent remain visible in the review.
  await page.locator(".outline-stage__scroll").evaluateAll((items) => items.forEach((item) => { item.scrollTop = 0; }));
  assert.equal(await page.locator(".outline-stage").count(), 4);
  metrics.board = { cards: await page.locator(".outline-row").count(), edges: await page.locator(".flow-edge").count() };
  await checkViewport("Stage board");
  await checkCompactHeader("Stage board");
  await checkA11y("Stage board");
  await screenshot("02-stage-board.png");

  await page.getByRole("button", { name: "Reading view", exact: true }).click();
  await page.getByRole("button", { name: "Expand Enforce model allow-list", exact: true }).click();
  await page.getByRole("button", { name: "Expand Choose: variable requestedModel is empty", exact: true }).click();
  const decision = page.getByRole("button", { name: /^First match of 2 conditions, else continue decision/ });
  await decision.click();
  await page.getByRole("complementary", { name: "Flow inspector" }).waitFor();
  await checkViewport("Reading view");
  await checkCompactHeader("Reading view");
  await checkA11y("Reading view");
  await screenshot("03-reading-view.png");
  await page.keyboard.press("Escape");
  await page.getByRole("complementary", { name: "Flow inspector" }).waitFor({ state: "hidden" });
  assert(await decision.evaluate((element) => element === document.activeElement), "Escape should return focus to the selected card.");

  const search = page.getByRole("combobox", { name: "Search policy flow" });
  await search.fill("Forward request");
  await page.getByRole("option").first().waitFor();
  await search.press("Enter");
  await page.getByRole("region", { name: "Backend policy outline" }).waitFor();
  assert.equal(await page.locator(".outline-stage").count(), 1, "Reading view must display only the focused stage.");
  await page.getByRole("heading", { name: /Forward request/ }).waitFor();
  await checkViewport("Cross-stage search");

  await page.getByRole("button", { name: "Close inspector" }).click();
  await page.getByRole("button", { name: "Stage board", exact: true }).click();
  await page.getByRole("button", { name: "Expand scope selection panel" }).click();
  await page.setViewportSize({ width: 1280, height: 900 });
  await checkViewport("1280px with scope sidebar");
  assert.deepEqual(failures, [], "No runtime errors or external requests");
  writeFileSync(join(directory, "validation.json"), JSON.stringify({ viewport: "1440x900", metrics, failures, checks: [
    "Only Flow map, Stage board and Reading view are available; Flow map is the default",
    "Compact header and at least 75% workspace height at 1440x900 and 1280x720",
    "Focused expanded graph fits canvas", "Quiet mode preserves edges and reveals labels on keyboard focus",
    "Stage board and reading view pass serious/critical axe checks including contrast",
    "No document or inspector overflow", "Escape returns focus", "Cross-stage search follows the result",
    "1280px sidebar-open viewport", "No external requests or runtime errors",
  ] }, null, 2));
  console.log(JSON.stringify({ result: "PASS", screenshots: directory, metrics }, null, 2));
} finally {
  await browser.close();
}
