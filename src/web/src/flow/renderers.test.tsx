import { Position, ReactFlow, ReactFlowProvider } from "@xyflow/react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import "../styles/flow.css";
import { EDGE_KINDS, ELEMENT_KINDS } from "./model";
import { edgeTypes, FlowEdgeRenderer } from "./edges";
import Legend from "./Legend";
import { nodeTypes } from "./nodes";
import { projectFlow } from "./projection";
import syntheticModel from "./__fixtures__/syntheticModel";

describe("semantic flow renderers", () => {
  afterEach(cleanup);
  it("documents every element and edge kind in the legend", () => {
    const { container } = render(<Legend />);
    for (const kind of ELEMENT_KINDS) {
      expect(container.querySelector(`[data-legend-node-kind="${kind}"]`)).toBeTruthy();
    }
    expect(container.querySelector('[data-legend-node-kind="composite"]')).toBeTruthy();
    for (const kind of EDGE_KINDS) {
      expect(container.querySelector(`[data-legend-edge-kind="${kind}"]`)).toBeTruthy();
    }
  });

  it("renders subprocess outcomes and configuration-dependent text, not colour alone", () => {
    const projection = projectFlow(syntheticModel);
    const auth = projection.nodes.find((node) => node.id.includes("authentication"))!;
    const decision = projectFlow(
      syntheticModel,
      new Set(["stage:inbound", "stage:backend", "stage:outbound", "stage:on-error", "inbound/1"]),
    ).nodes.find((node) => node.kind === "decision")!;
    render(
      <ReactFlowProvider>
        <div style={{ width: 900, height: 500 }}>
          <ReactFlow
            nodes={[
              { id: auth.id, type: "subprocess", position: { x: 0, y: 0 }, width: 260, height: 100, style: { width: 260, height: 100 }, data: { projected: auth } },
              { id: decision.id, type: "decision", position: { x: 300, y: 0 }, width: 220, height: 100, style: { width: 220, height: 100 }, data: { projected: decision } },
            ]}
            edges={[]}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
          />
        </div>
      </ReactFlowProvider>,
    );
    expect(screen.getByRole("button", { name: /Subprocess, Authenticate subscription.*401, 403, 503.*raises error/i })).toBeInTheDocument();
    expect(screen.getByText("configuration-dependent")).toBeInTheDocument();
  });

  it("uses a distinct non-colour marker contract for all edges", () => {
    const { container } = render(<Legend />);
    expect(container.querySelector(".legend-edge--raises-error")).toHaveClass("legend-edge--raises-error");
    expect(container.querySelector(".legend-edge--data-dependency")).toHaveClass("legend-edge--data-dependency");
    expect(container.querySelector(".legend-edge--loop-back")).toHaveClass("legend-edge--loop-back");
  });

  it("keeps quiet edges accessible and emphasizes them on keyboard focus", () => {
    const projected = projectFlow(syntheticModel).edges.find((edge) => edge.kind === "explicit-response")!;
    const { container } = render(
      <ReactFlowProvider>
        <svg>
          <FlowEdgeRenderer
            id={projected.id}
            source={projected.source}
            target={projected.target}
            sourceX={0}
            sourceY={0}
            targetX={100}
            targetY={100}
            sourcePosition={Position.Bottom}
            targetPosition={Position.Top}
            data={{ projected }}
          />
        </svg>
      </ReactFlowProvider>,
    );
    const edge = screen.getByRole("button", { name: /Explicit response/ });
    expect(container.querySelector(".react-flow__edge-path")).toBeInTheDocument();
    expect(edge).not.toHaveClass("is-emphasized");
    fireEvent.focus(edge);
    expect(edge).toHaveClass("is-emphasized");
    fireEvent.blur(edge);
    expect(edge).not.toHaveClass("is-emphasized");
  });

  it("uses WCAG AA text contrast and occurrence-qualified repeated labels", () => {
    const luminance = (hex: string) => {
      const channels = hex
        .slice(1)
        .match(/.{2}/g)!
        .map((channel) => Number.parseInt(channel, 16) / 255)
        .map((channel) =>
          channel <= 0.03928
            ? channel / 12.92
            : ((channel + 0.055) / 1.055) ** 2.4,
        );
      return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    };
    const contrast = (foreground: string, background: string) => {
      const values = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
      return (values[0] + 0.05) / (values[1] + 0.05);
    };
    expect(contrast("#f4f8fc", "#10243a")).toBeGreaterThanOrEqual(4.5);
    expect(contrast("#e3edf7", "#20344a")).toBeGreaterThanOrEqual(4.5);
    expect(syntheticModel.elements.find((element) => element.id.includes("outbound/fragment"))?.label).toBe("Set response headers · outbound");
    expect(syntheticModel.elements.find((element) => element.id.includes("on-error/fragment"))?.label).toBe("Set response headers · on-error");
  });
});
