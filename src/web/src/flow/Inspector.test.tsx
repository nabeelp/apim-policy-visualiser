import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import syntheticModel from "./__fixtures__/syntheticModel";
import Inspector from "./Inspector";
import { expandAll, projectFlow } from "./projection";

const projection = projectFlow(syntheticModel, expandAll(syntheticModel), {
  showDataDependencies: true,
});

describe("Inspector", () => {
  afterEach(cleanup);
  it("shows edge priority, verbatim condition and inferred summary", () => {
    const branch = projection.edges.find((edge) => edge.kind === "branch" && edge.priority === 1)!;
    render(
      <Inspector
        model={syntheticModel}
        selection={{ type: "edge", id: branch.id }}
        projectedEdges={projection.edges}
        onClose={vi.fn()}
        onSelectEdge={vi.fn()}
        onHighlightVariable={vi.fn()}
      />,
    );
    expect(screen.getByText("Priority").nextSibling).toHaveTextContent("1");
    expect(screen.getByText(branch.condition!.text)).toBeInTheDocument();
    expect(screen.getByText("Inferred")).toBeInTheDocument();
    expect(screen.getByText(branch.condition!.summary!)).toBeInTheDocument();
  });

  it("renders exact source lines as text and shows identity and provenance", () => {
    render(
      <Inspector
        model={syntheticModel}
        selection={{ type: "node", id: "inbound/fragment:authentication#1/4" }}
        projectedEdges={projection.edges}
        onClose={vi.fn()}
        onSelectEdge={vi.fn()}
        onHighlightVariable={vi.fn()}
      />,
    );
    expect(screen.getByRole("heading", { name: "Validate JWT" })).toBeInTheDocument();
    expect(screen.getByLabelText("Exact policy source slice")).toHaveTextContent("validate-jwt");
    expect(screen.getByText("APIM rule")).toBeInTheDocument();
  });

  it("highlights variables, selects listed edges and restores focus on Escape", () => {
    const highlight = vi.fn();
    const selectEdge = vi.fn();
    const close = vi.fn();
    const restore = vi.fn();
    render(
      <Inspector
        model={syntheticModel}
        selection={{ type: "node", id: "inbound/fragment:responses-lookup#1/0" }}
        projectedEdges={projection.edges}
        onClose={close}
        onSelectEdge={selectEdge}
        onHighlightVariable={highlight}
        returnFocus={restore}
      />,
    );
    fireEvent.click(screen.getByText(/responseOwner \(written\)/));
    fireEvent.click(screen.getByRole("button", { name: /Highlight responseOwner/ }));
    expect(highlight).toHaveBeenCalledWith("responseOwner");
    const edgeButton = screen.getAllByRole("button", { name: /^(Incoming|Outgoing) / })[0];
    fireEvent.click(edgeButton);
    expect(selectEdge).toHaveBeenCalled();
    fireEvent.keyDown(screen.getByRole("complementary", { name: "Flow inspector" }), { key: "Escape" });
    expect(close).toHaveBeenCalled();
    return Promise.resolve().then(() => expect(restore).toHaveBeenCalled());
  });
});
