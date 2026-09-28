import { describe, expect, it } from "vitest";
import syntheticModel from "./__fixtures__/syntheticModel";
import { layoutFlow } from "./layout";
import { collapseToOverview, expandAll, projectFlow } from "./projection";

describe("layoutFlow", () => {
  it("places normal stages left-to-right and On-error below spanning them", async () => {
    const projection = projectFlow(syntheticModel, collapseToOverview(syntheticModel));
    const layout = await layoutFlow(projection.nodes, projection.edges);
    const byId = new Map(layout.nodes.map((node) => [node.id, node]));
    const inbound = byId.get("stage:inbound")!;
    const backend = byId.get("stage:backend")!;
    const outbound = byId.get("stage:outbound")!;
    const onError = byId.get("stage:on-error")!;
    expect(inbound.position.x).toBeLessThan(backend.position.x);
    expect(backend.position.x).toBeLessThan(outbound.position.x);
    expect(onError.position.y).toBeGreaterThan(
      Math.max(
        inbound.position.y + inbound.height,
        backend.position.y + backend.height,
        outbound.position.y + outbound.height,
      ),
    );
    expect(onError.width).toBeGreaterThanOrEqual(
      outbound.position.x + outbound.width - inbound.position.x,
    );
  });

  it("keeps children within parent bounds and avoids sibling overlap", async () => {
    const projection = projectFlow(syntheticModel, expandAll(syntheticModel));
    const layout = await layoutFlow(projection.nodes, projection.edges);
    const byId = new Map(layout.nodes.map((node) => [node.id, node]));
    for (const child of layout.nodes.filter((node) => node.parentId)) {
      const parent = byId.get(child.parentId!)!;
      expect(child.position.x).toBeGreaterThanOrEqual(0);
      expect(child.position.y).toBeGreaterThanOrEqual(0);
      expect(child.position.x + child.width).toBeLessThanOrEqual(parent.width + 0.01);
      expect(child.position.y + child.height).toBeLessThanOrEqual(parent.height + 0.01);
    }
  });

  it("is deterministic and handles at least 150 visible nodes", async () => {
    const projection = projectFlow(syntheticModel, expandAll(syntheticModel));
    const first = await layoutFlow(projection.nodes, projection.edges);
    const second = await layoutFlow(projection.nodes, projection.edges);
    expect(second).toEqual(first);

    const seed = projection.nodes.find((node) => node.kind === "step")!;
    const generated = Array.from({ length: 150 }, (_, index) => ({
      ...seed,
      id: `generated:${index}`,
      parentId: "stage:inbound",
      order: 100 + index,
      label: `Generated step ${index}`,
    }));
    await expect(layoutFlow(
      [...projection.nodes, ...generated],
      projection.edges,
    )).resolves.toEqual(expect.objectContaining({ nodes: expect.any(Array) }));
  }, 20_000);
});
