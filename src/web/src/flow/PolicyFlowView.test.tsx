import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../api/client";
import syntheticModel from "./__fixtures__/syntheticModel";
import PolicyFlowView from "./PolicyFlowView";

vi.mock("../api/client", () => ({ apiFetch: vi.fn() }));
const mockedFetch = vi.mocked(apiFetch);

describe("PolicyFlowView", () => {
  afterEach(() => {
    cleanup();
    mockedFetch.mockReset();
  });

  it("offers only the three retained views, defaults to Flow map, and expands and collapses in place", async () => {
    mockedFetch.mockResolvedValue(syntheticModel);
    render(<PolicyFlowView scopeId="synthetic" onBackToSelection={vi.fn()} />);
    expect(await screen.findByRole("region", { name: "Effective policy flow for synthetic" })).toBeInTheDocument();
    const views = within(screen.getByRole("group", { name: "Visualization views" }));
    expect(views.getAllByRole("button").map((button) => button.textContent)).toEqual([
      "Flow map", "Stage board", "Reading view",
    ]);
    expect(views.getByRole("button", { name: "Flow map" })).toHaveAttribute("aria-pressed", "true");
    expect(views.getByRole("button", { name: "Flow map" })).toHaveAccessibleDescription(
      "Connections stay visible. Hover, focus or select to reveal their labels.",
    );
    expect(screen.queryByRole("button", { name: "Quiet map" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Detailed graph" })).not.toBeInTheDocument();
    for (const stage of ["Inbound", "Backend", "Outbound", "On-error"]) {
      expect(await screen.findByRole("button", { name: new RegExp(`^Stage lane, ${stage}(,|$)`) })).toBeInTheDocument();
    }
    expect(screen.getByRole("button", { name: /Expand Authenticate subscription/ })).toBeInTheDocument();
    expect(screen.queryByText("Validate JWT")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Expand Authenticate subscription/ }));
    expect(await screen.findByText("Validate JWT")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Collapse Authenticate subscription/ }));
    await waitFor(() => expect(screen.queryByText("Validate JWT")).not.toBeInTheDocument());
  }, 15_000);

  it("implements overview, expand-all, fit and visibility toolbar controls", async () => {
    mockedFetch.mockResolvedValue(syntheticModel);
    render(<PolicyFlowView scopeId="synthetic" onBackToSelection={vi.fn()} />);
    await screen.findByRole("region", { name: "Effective policy flow for synthetic" });
    fireEvent.click(screen.getByRole("button", { name: "Expand all" }));
    expect(await screen.findByText("Forward request to backend")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Collapse to overview" }));
    await waitFor(() =>
      expect(screen.queryByText("Forward request to backend")).not.toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: "Fit view" })).toBeEnabled();
    fireEvent.click(screen.getByText("Display options"));
    expect(screen.getByRole("checkbox", { name: "Data dependencies" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Observability" })).toBeChecked();
  }, 15_000);

  it.each([403, 404, 422])("shows the backend detail for a %s response and offers return", async (status) => {
    const onBack = vi.fn();
    mockedFetch.mockRejectedValue(new Error(`${status}: backend ProblemDetails detail`));
    render(<PolicyFlowView scopeId="synthetic" onBackToSelection={onBack} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("backend ProblemDetails detail");
    fireEvent.click(screen.getByRole("button", { name: "Back to scope selection" }));
    expect(onBack).toHaveBeenCalledOnce();
  });

  it("does not display a stale response for a previous scope", async () => {
    let resolveFirst!: (model: typeof syntheticModel) => void;
    mockedFetch
      .mockReturnValueOnce(new Promise((resolve) => { resolveFirst = resolve; }))
      .mockResolvedValueOnce({ ...syntheticModel, scopeId: "new-scope" });
    const { rerender } = render(<PolicyFlowView scopeId="old-scope" onBackToSelection={vi.fn()} />);
    rerender(<PolicyFlowView scopeId="new-scope" onBackToSelection={vi.fn()} />);
    expect(await screen.findByRole("region", { name: "Effective policy flow for new-scope" })).toBeInTheDocument();
    resolveFirst({ ...syntheticModel, scopeId: "old-scope" });
    await Promise.resolve();
    expect(screen.queryByRole("region", { name: "Effective policy flow for old-scope" })).not.toBeInTheDocument();
  });

  it("has no legacy PolicyFlowDiagram module", () => {
    const modules = import.meta.glob("../components/PolicyFlowDiagram*");
    expect(Object.keys(modules)).toEqual([]);
  });

  it("switches among the retained views without losing expansion or inspector access", async () => {
    mockedFetch.mockResolvedValue(syntheticModel);
    render(<PolicyFlowView scopeId="synthetic" onBackToSelection={vi.fn()} />);
    await screen.findByRole("region", { name: "Effective policy flow for synthetic" });
    fireEvent.click(screen.getByRole("button", { name: "Stage board" }));
    fireEvent.click(screen.getByRole("button", { name: "Expand Authenticate subscription" }));
    fireEvent.click(await screen.findByRole("button", { name: /^Validate JWT/ }));
    expect(screen.getByRole("complementary", { name: "Flow inspector" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reading view" }));
    expect(screen.getByRole("region", { name: "Inbound policy outline" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Backend policy outline" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Close inspector" }));
    fireEvent.click(screen.getByRole("button", { name: "Flow map" }));
    expect(await screen.findByRole("button", { name: /Policy action, Validate JWT/ })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Stage board" }));
    fireEvent.click(screen.getByRole("button", { name: /^Retry backend request/ }));
    fireEvent.click(screen.getByRole("button", { name: "Reading view" }));
    expect(screen.getByRole("region", { name: "Backend policy outline" })).toBeInTheDocument();
  }, 15_000);
});
