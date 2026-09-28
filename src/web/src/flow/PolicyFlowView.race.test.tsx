import { act, cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../api/client";
import backendModelJson from "./__fixtures__/backendExampleModel.json";
import syntheticModel from "./__fixtures__/syntheticModel";
import type { FlowLayout } from "./layout";
import type { EffectivePolicyFlowModel } from "./model";
import PolicyFlowView from "./PolicyFlowView";

vi.mock("../api/client", () => ({ apiFetch: vi.fn() }));

// Every layout call is held until the test releases it, so stale/fresh completion order is deterministic.
const held: (() => Promise<void>)[] = [];
vi.mock("./layout", async () => {
  const actual = await vi.importActual<typeof import("./layout")>("./layout");
  const layoutFlow = (...args: Parameters<typeof actual.layoutFlow>): Promise<FlowLayout> =>
    new Promise((resolve) => {
      held.push(() => actual.layoutFlow(...args).then(resolve));
    });
  return { ...actual, layoutFlow, default: layoutFlow };
});

const mockedFetch = vi.mocked(apiFetch);

describe("PolicyFlowView layout races", () => {
  afterEach(() => {
    cleanup();
    mockedFetch.mockReset();
  });

  it("never applies a layout that was still running for the previous scope", async () => {
    const first = { ...syntheticModel, scopeId: "apis/first" } as EffectivePolicyFlowModel;
    const second = { ...(backendModelJson as unknown as EffectivePolicyFlowModel), scopeId: "apis/second" };
    let resolveSecond!: (model: EffectivePolicyFlowModel) => void;
    mockedFetch
      .mockResolvedValueOnce(first)
      .mockReturnValueOnce(new Promise((resolve) => { resolveSecond = resolve; }));

    const view = render(<PolicyFlowView scopeId="apis/first" onBackToSelection={vi.fn()} />);
    await vi.waitFor(() => expect(held).toHaveLength(1));

    // Scope changes while the first scope's layout is still running; that layout then completes.
    view.rerender(<PolicyFlowView scopeId="apis/second" onBackToSelection={vi.fn()} />);
    await act(async () => {
      await held[0]();
    });
    // The second model arrives; its own layout is still pending, so nothing from the first scope may render.
    await act(async () => resolveSecond(second));
    await vi.waitFor(() => expect(held).toHaveLength(2));
    expect(screen.queryByText("Authenticate subscription")).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: /Effective policy flow for/ })).not.toBeInTheDocument();

    await act(async () => {
      await held[1]();
    });
    expect(await screen.findByRole("region", { name: "Effective policy flow for apis/second" })).toBeInTheDocument();
    expect(await screen.findByText("Authenticate caller")).toBeInTheDocument();
    expect(screen.queryByText("Authenticate subscription")).not.toBeInTheDocument();
  }, 15_000);
});
