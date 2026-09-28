import { describe, expect, it } from "vitest";
import backendModelJson from "./__fixtures__/backendExampleModel.json";
import { layoutFlow } from "./layout";
import {
  EDGE_KINDS,
  ELEMENT_KINDS,
  FLOW_IDS,
  type EffectivePolicyFlowModel,
} from "./model";
import { collapseToOverview, expandAll, expandAncestors, projectFlow, type FlowProjection } from "./projection";
import syntheticModel from "./__fixtures__/syntheticModel";
import { searchFlow } from "./search";

// Serialized by the real backend for the committed synthetic fixture; kept in sync by
// tests/ApimPolicyVisualizer.Api.Tests/FrontendContractFixtureTests.cs.
const model = backendModelJson as unknown as EffectivePolicyFlowModel;

function outcomes(projection: FlowProjection): string[] {
  const visible = new Set(projection.nodes.map((node) => node.id));
  const seen = new Set<string>([FLOW_IDS.requestEntry]);
  const queue: string[] = [FLOW_IDS.requestEntry];
  const found = new Set<string>();
  while (queue.length) {
    const source = queue.shift()!;
    for (const edge of projection.edges) {
      if (edge.source !== source || edge.kind === "data-dependency") continue;
      if (edge.target.startsWith("terminal:")) found.add(`${edge.kind}->${edge.target}`);
      if (visible.has(edge.target) && !seen.has(edge.target)) {
        seen.add(edge.target);
        queue.push(edge.target);
      }
    }
  }
  return [...found].sort();
}

describe("backend contract fixture", () => {
  it("uses only contract kinds and resolvable references", () => {
    expect(model.schemaVersion).toBe(2);
    const ids = new Set(model.elements.map((element) => element.id));
    expect(ids.size).toBe(model.elements.length);
    for (const element of model.elements) {
      expect(ELEMENT_KINDS).toContain(element.kind);
      if (element.parentId) expect(ids.has(element.parentId)).toBe(true);
    }
    for (const edge of model.edges) {
      expect(EDGE_KINDS).toContain(edge.kind);
      expect(ids.has(edge.from)).toBe(true);
      expect(ids.has(edge.to)).toBe(true);
    }
    expect(model.stages.map((stage) => stage.name)).toEqual(["inbound", "backend", "outbound", "on-error"]);
  });

  it("preserves every terminal outcome and counts each crossing edge once in every single-group expansion state", () => {
    const overview = collapseToOverview(model);
    const baseline = outcomes(projectFlow(model, expandAll(model)));
    expect(baseline).toEqual(
      expect.arrayContaining([
        "explicit-response->terminal:explicit-response",
        "sequence->terminal:final-response",
      ]),
    );
    const containers = model.elements.filter((element) => ["group", "choose", "loop"].includes(element.kind));
    expect(containers.length).toBeGreaterThan(10);
    for (const state of [overview, ...containers.map((c) => new Set([...overview, c.id])), expandAll(model)]) {
      const projection = projectFlow(model, state);
      expect(outcomes(projection)).toEqual(baseline);
      const counts = new Map<string, number>();
      projection.edges.forEach((edge) => edge.contributors.forEach((id) => counts.set(id, (counts.get(id) ?? 0) + 1)));
      expect([...counts.values()].every((count) => count === 1)).toBe(true);
    }
  });

  it("lays out the overview and the fully expanded model as lanes with On-error underneath", async () => {
    for (const state of [collapseToOverview(model), expandAll(model)]) {
      const projection = projectFlow(model, state);
      const layout = await layoutFlow(projection.nodes, projection.edges);
      const lane = (id: string) => layout.nodes.find((node) => node.id === id)!;
      const [inbound, backend, outbound, onError] = ["inbound", "backend", "outbound", "on-error"].map((name) =>
        lane(`stage:${name}`),
      );
      expect(inbound.position.x).toBeLessThan(backend.position.x);
      expect(backend.position.x).toBeLessThan(outbound.position.x);
      const normalBottom = Math.max(...[inbound, backend, outbound].map((node) => node.position.y + node.height));
      expect(onError.position.y).toBeGreaterThan(normalBottom);
      expect(onError.width).toBeGreaterThanOrEqual(outbound.position.x + outbound.width - inbound.position.x);
    }
  });

  it("finds fragments, variables and status codes from the real model", () => {
    const fragment = model.elements.find((element) => element.fragment)!.fragment!;
    expect(searchFlow(model, fragment.name).length).toBeGreaterThan(0);
    expect(searchFlow(model, model.variables[0].name).length).toBeGreaterThan(0);
    expect(searchFlow(model, "403").length).toBeGreaterThan(0);
  });

  it("opens synthetic composites for expand-all and for ancestors of a hidden member", () => {
    const overview = projectFlow(syntheticModel, collapseToOverview(syntheticModel));
    const composite = overview.nodes.find((node) => node.kind === "composite");
    expect(composite).toBeDefined();
    const member = syntheticModel.elements.find(
      (element) => element.parentId === composite!.parentId && !overview.nodes.some((node) => node.id === element.id)
        && (element.kind === "step" || element.kind === "opaque"),
    )!;
    expect(member).toBeDefined();
    const all = projectFlow(syntheticModel, expandAll(syntheticModel));
    expect(all.nodes.some((node) => node.id === member.id)).toBe(true);
    expect(all.nodes.some((node) => node.kind === "composite" && node.collapsed)).toBe(false);
    const focused = projectFlow(syntheticModel, expandAncestors(syntheticModel, member.id));
    expect(focused.nodes.some((node) => node.id === member.id)).toBe(true);
  });

  it("gives the gateway-default and error-response terminals separate slots", async () => {
    const onErrorLeaf = model.elements.find((element) => element.stage === "on-error" && element.kind === "step")!;
    const withGatewayDefault: EffectivePolicyFlowModel = {
      ...model,
      elements: [
        ...model.elements,
        { ...model.elements.find((element) => element.id === FLOW_IDS.errorResponseTerminal)!, id: FLOW_IDS.gatewayDefaultErrorTerminal, label: "Gateway default error response" },
      ],
      edges: [
        ...model.edges,
        { id: "e:test", from: onErrorLeaf.id, to: FLOW_IDS.gatewayDefaultErrorTerminal, kind: "raises-error", label: null, priority: null, condition: null, facts: null },
      ],
    };
    const projection = projectFlow(withGatewayDefault, expandAll(withGatewayDefault));
    const layout = await layoutFlow(projection.nodes, projection.edges);
    const [error, gateway] = [FLOW_IDS.errorResponseTerminal, FLOW_IDS.gatewayDefaultErrorTerminal].map(
      (id) => layout.nodes.find((node) => node.id === id)!,
    );
    const overlaps =
      error.position.x < gateway.position.x + gateway.width && gateway.position.x < error.position.x + error.width &&
      error.position.y < gateway.position.y + gateway.height && gateway.position.y < error.position.y + error.height;
    expect(overlaps).toBe(false);
  });
});
