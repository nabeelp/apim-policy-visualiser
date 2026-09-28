import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../api/client";
import syntheticModel from "./__fixtures__/syntheticModel";
import PolicyFlowView from "./PolicyFlowView";

vi.mock("../api/client", () => ({ apiFetch: vi.fn() }));
const mockedFetch = vi.mocked(apiFetch);

async function expectNoSeriousOrCriticalViolations(container: HTMLElement) {
  const result = await axe.run(container, {
    resultTypes: ["violations"],
    rules: {
      "color-contrast": { enabled: false },
    },
  });
  expect(
    result.violations.filter((violation) =>
      violation.impact === "serious" || violation.impact === "critical",
    ),
  ).toEqual([]);
}

async function renderView() {
  mockedFetch.mockResolvedValue(syntheticModel);
  const rendered = render(
    <PolicyFlowView scopeId="synthetic" onBackToSelection={vi.fn()} />,
  );
  await screen.findByRole("region", { name: "Effective policy flow for synthetic" });
  await screen.findByRole("button", {
    name: /Subprocess, Authenticate subscription, explicit responses 401, 403, 503, raises error/i,
  });
  return rendered;
}

describe("PolicyFlowView accessibility", () => {
  afterEach(() => {
    cleanup();
    mockedFetch.mockReset();
  });

  it("has no serious or critical axe violations in the overview", async () => {
    const { container } = await renderView();
    expect(
      screen.getByRole("button", {
        name: /Subprocess, Authenticate subscription, explicit responses 401, 403, 503, raises error/i,
      }),
    ).toBeInTheDocument();
    await expectNoSeriousOrCriticalViolations(container);
  }, 20_000);

  it("operates an expanded retry with keyboard only", async () => {
    const user = userEvent.setup();
    const { container } = await renderView();
    const expand = screen.getByRole("button", { name: "Expand Retry backend request" });
    expand.focus();
    await user.keyboard("{Enter}");
    expect(await screen.findByText("Forward request to backend")).toBeInTheDocument();
    const retryNode = screen.getByRole("button", { name: /Retry loop, Retry backend request/ });
    retryNode.focus();
    await user.keyboard(" ");
    await waitFor(() =>
      expect(screen.queryByText("Forward request to backend")).not.toBeInTheDocument(),
    );
    await expectNoSeriousOrCriticalViolations(container);
  }, 20_000);

  it("opens and closes the inspector, and activates edge items by keyboard", async () => {
    const user = userEvent.setup();
    const { container } = await renderView();
    const requestEntry = screen.getByRole("button", {
      name: /Entry, Incoming API request/,
    });
    requestEntry.focus();
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("complementary", { name: "Flow inspector" })).toBeInTheDocument();
    const edgeButton = screen.getAllByRole("button", { name: /^(Incoming|Outgoing) / })[0];
    edgeButton.focus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("heading", { name: "Edge" })).toBeInTheDocument();
    await user.keyboard("{Escape}");
    await waitFor(() =>
      expect(screen.queryByRole("complementary", { name: "Flow inspector" })).not.toBeInTheDocument(),
    );
    await expectNoSeriousOrCriticalViolations(container);
  }, 20_000);

  it("shows keyboard-navigable search results with no serious axe violations", async () => {
    const { container } = await renderView();
    const search = screen.getByRole("combobox", { name: "Search policy flow" });
    search.focus();
    fireEvent.change(search, { target: { value: "Forward request" } });
    expect(await screen.findByRole("option", { name: /Forward request to backend/ })).toBeInTheDocument();
    fireEvent.keyDown(search, { key: "Enter" });
    expect(await screen.findByRole("heading", { name: "Forward request to backend" })).toBeInTheDocument();
    await expectNoSeriousOrCriticalViolations(container);
  }, 20_000);

  it("tabs and shift-tabs through visible node controls in document order", async () => {
    const user = userEvent.setup();
    await renderView();
    const nodeButtons = screen.getAllByRole("button").filter((button) =>
      button.hasAttribute("data-flow-node-id"),
    );
    nodeButtons[0].focus();
    if (nodeButtons.length > 1) {
      await user.tab();
      expect(document.activeElement).toBe(nodeButtons[1]);
      await user.tab({ shift: true });
      expect(document.activeElement).toBe(nodeButtons[0]);
    }
  }, 20_000);
});
