import { describe, expect, it } from "vitest";
import syntheticModel from "./__fixtures__/syntheticModel";
import {
  collapseToOverview,
  expandAll,
  expandAncestors,
  projectFlow,
} from "./projection";

function deepFreeze<T>(value: T): T {
  if (value && typeof value === "object" && !Object.isFrozen(value)) {
    Object.freeze(value);
    Object.values(value).forEach(deepFreeze);
  }
  return value;
}

function terminalOutcomes(nodes: readonly { id: string }[], edges: ReturnType<typeof projectFlow>["edges"]) {
  const visible = new Set(nodes.map((node) => node.id));
  const seen = new Set<string>(["entry:request"]);
  const queue = ["entry:request"];
  const outcomes = new Set<string>();
  while (queue.length) {
    const source = queue.shift()!;
    for (const edge of edges.filter((candidate) => candidate.source === source && candidate.kind !== "data-dependency")) {
      if (edge.target.startsWith("terminal:")) outcomes.add(`${edge.kind}:${edge.target}`);
      if (visible.has(edge.target) && !seen.has(edge.target)) {
        seen.add(edge.target);
        queue.push(edge.target);
      }
    }
  }
  return [...outcomes].sort();
}

describe("projectFlow", () => {
  it("lifts collapsed authentication outcomes without merging semantic kinds", () => {
    const projection = projectFlow(syntheticModel, collapseToOverview(syntheticModel));
    const authentication = projection.nodes.find((node) => node.id.includes("authentication#1"));
    expect(authentication).toMatchObject({
      collapsed: true,
      explicitResponseCodes: ["401", "403", "503"],
      raisesError: true,
    });
    expect(
      projection.edges.filter(
        (edge) =>
          edge.source === authentication?.id && edge.kind === "explicit-response",
      ),
    ).toEqual([
      expect.objectContaining({
        label: "401 / 403 / 503",
        statusCodes: ["401", "403", "503"],
        contributors: expect.arrayContaining([
          expect.stringContaining("explicit-response"),
        ]),
      }),
    ]);
    expect(
      projection.edges.find(
        (edge) =>
          edge.source === authentication?.id && edge.kind === "explicit-response",
      )?.contributors,
    ).toHaveLength(3);
    expect(
      projection.edges.some(
        (edge) => edge.source === authentication?.id && edge.kind === "raises-error",
      ),
    ).toBe(true);
  });

  it("preserves reachable terminal outcomes and boundary contributors in every expansion state", () => {
    deepFreeze(syntheticModel);
    const overview = collapseToOverview(syntheticModel);
    const containers = syntheticModel.elements.filter((element) =>
      ["group", "choose", "loop"].includes(element.kind),
    );
    const states = [
      overview,
      ...containers.map((container) => new Set([...overview, container.id])),
      expandAll(syntheticModel),
    ];
    const baseline = terminalOutcomes(
      projectFlow(syntheticModel, expandAll(syntheticModel)).nodes,
      projectFlow(syntheticModel, expandAll(syntheticModel)).edges,
    );
    for (const state of states) {
      const projection = projectFlow(syntheticModel, state);
      expect(terminalOutcomes(projection.nodes, projection.edges)).toEqual(baseline);
      const contributorCounts = new Map<string, number>();
      projection.edges.forEach((edge) =>
        edge.contributors.forEach((id) =>
          contributorCounts.set(id, (contributorCounts.get(id) ?? 0) + 1),
        ),
      );
      expect([...contributorCounts.values()].every((count) => count === 1)).toBe(true);
    }
  });

  it("summarizes a collapsed retry and restores its exact loop-back edge when expanded", () => {
    const overview = projectFlow(syntheticModel, collapseToOverview(syntheticModel));
    expect(overview.nodes.find((node) => node.id === "backend/0")?.loopBadge).toContain(
      "response status is 429",
    );
    expect(overview.edges.some((edge) => edge.kind === "loop-back")).toBe(false);

    const expanded = projectFlow(
      syntheticModel,
      new Set([...collapseToOverview(syntheticModel), "backend/0"]),
    );
    const modelLoopEdge = syntheticModel.edges.find((edge) => edge.kind === "loop-back")!;
    expect(
      expanded.edges.find((edge) => edge.kind === "loop-back")?.contributors,
    ).toContain(modelLoopEdge.id);
  });

  it("changes only observability and data-dependency visibility options", () => {
    const expanded = expandAll(syntheticModel);
    const normal = projectFlow(syntheticModel, expanded, {
      showDataDependencies: false,
      showObservability: true,
    });
    const toggled = projectFlow(syntheticModel, expanded, {
      showDataDependencies: true,
      showObservability: false,
    });
    expect(normal.edges.some((edge) => edge.kind === "data-dependency")).toBe(false);
    expect(toggled.edges.some((edge) => edge.kind === "data-dependency")).toBe(true);
    expect(toggled.nodes.some((node) => node.element?.observability)).toBe(false);
    expect(normal.nodes.some((node) => node.element?.observability)).toBe(true);
  });

  it("expands exactly the ancestor chain for a deeply nested step", () => {
    const expanded = expandAncestors(
      syntheticModel,
      "backend/0/fragment:retry-auth#1/0",
      new Set(),
    );
    expect([...expanded]).toEqual([
      "stage:backend",
      "backend/0",
      "backend/0/fragment:retry-auth#1",
    ]);
  });

  it("creates stable overview composites for consecutive simple top-level steps", () => {
    const first = projectFlow(syntheticModel);
    const second = projectFlow(syntheticModel);
    const composite = first.nodes.find((node) => node.kind === "composite");
    expect(composite?.id).toBe("composite:inbound/3");
    expect(composite?.memberIds).toEqual(["inbound/3", "inbound/4", "inbound/5", "inbound/6"]);
    expect(second.nodes.find((node) => node.kind === "composite")?.id).toBe(composite?.id);
  });
});
