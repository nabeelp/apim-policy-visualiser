import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import axe from "axe-core";
import { afterEach, describe, expect, it, vi } from "vitest";
import backendModel from "./__fixtures__/backendExampleModel.json";
import syntheticModel from "./__fixtures__/syntheticModel";
import type { EffectivePolicyFlowModel } from "./model";
import PolicyOutline from "./PolicyOutline";
import { collapseToOverview, expandAll, projectFlow } from "./projection";

const model = backendModel as EffectivePolicyFlowModel;

function renderOutline(mode: "board" | "reading" = "board") {
  const onSelect = vi.fn();
  const onToggle = vi.fn();
  const onStageChange = vi.fn();
  const projection = projectFlow(model, collapseToOverview(model));
  const props = {
    mode, projection, activeStage: "inbound" as const, onStageChange,
    selection: null, highlightedIds: new Set<string>(), pendingFocusId: null,
    onLayoutFocused: vi.fn(), onSelect, onToggle,
  };
  return { ...render(<PolicyOutline {...props} />), props, onSelect, onToggle, onStageChange };
}

describe("Policy outline views", () => {
  afterEach(cleanup);

  it("renders the real backend stage children even when stage elements have null stage metadata", () => {
    const { container, props } = renderOutline();
    expect(model.elements.find((node) => node.id === "stage:inbound")?.stage).toBeNull();
    for (const node of props.projection.nodes.filter((node) => node.kind !== "stage")) {
      expect(container.querySelector(`[data-flow-node-id="${node.id}"]`)).toBeInTheDocument();
    }
    expect(screen.getByText(/Source order, not an execution trace/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Inbound" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "On-error" })).toBeInTheDocument();
  });

  it("keeps selection separate from expansion, with status and error outcomes on the card", () => {
    const { container, props, onSelect, onToggle } = renderOutline();
    const node = props.projection.nodes.find((item) => item.expandable && item.kind !== "stage" && item.explicitResponseCodes.length)!;
    const card = container.querySelector<HTMLButtonElement>(`[data-flow-node-id="${node.id}"]`)!;
    expect(card).toHaveTextContent(`Returns ${node.explicitResponseCodes.join(" / ")}`);
    if (node.raisesError) expect(card).toHaveTextContent("May raise error");
    fireEvent.click(card);
    expect(onSelect).toHaveBeenCalledWith({ type: "node", id: node.id });
    expect(onToggle).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: `Expand ${node.label}` }));
    expect(onToggle).toHaveBeenCalledWith(node.id);
  });

  it("renders all expanded branches and loops in the source hierarchy, not as a fabricated linear path", () => {
    const { container, props, rerender } = renderOutline();
    const projection = projectFlow(model, expandAll(model));
    rerender(<PolicyOutline {...props} projection={projection} />);
    for (const node of projection.nodes.filter((node) => node.parentId)) {
      const card = container.querySelector(`[data-flow-node-id="${node.id}"]`)!;
      expect(card).toBeInTheDocument();
      const parent = projection.nodes.find((item) => item.id === node.parentId)!;
      if (parent.kind !== "stage") {
        expect(card.closest("ul")).toHaveAttribute("aria-label", `Contents of ${parent.label}`);
      }
    }
  });

  it("shows one stage at a time and follows search focus across stages", () => {
    const { props, rerender, onStageChange } = renderOutline("reading");
    expect(screen.getByRole("region", { name: "Inbound policy outline" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Backend policy outline" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Backend" }));
    expect(onStageChange).toHaveBeenCalledWith("backend");
    const backend = props.projection.nodes.find((item) => item.stage === "backend")!;
    rerender(<PolicyOutline {...props} pendingFocusId={backend.id} />);
    expect(onStageChange).toHaveBeenLastCalledWith("backend");
  });

  it("retains stage-level failure connections for inspection", () => {
    const projection = projectFlow(syntheticModel);
    const { props, rerender, onSelect } = renderOutline();
    rerender(<PolicyOutline {...props} projection={projection} />);
    const stage = screen.getByRole("region", { name: "Inbound policy outline" });
    const connection = within(stage).getByText(/Stage connections/);
    fireEvent.click(connection);
    const edgeButton = within(stage).getAllByRole("button").find((button) => button.textContent?.includes("Unhandled stage failure"));
    expect(edgeButton).toBeDefined();
    fireEvent.click(edgeButton!);
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ type: "edge" }));
  });

  it.each(["board", "reading"] as const)("has no serious or critical accessibility violations in %s", async (mode) => {
    const { container } = renderOutline(mode);
    const results = await axe.run(container, { rules: { "color-contrast": { enabled: false } } });
    expect(results.violations.filter((item) => item.impact === "serious" || item.impact === "critical")).toEqual([]);
  });
});
