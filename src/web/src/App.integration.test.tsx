import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { apiFetch } from "./api/client";
import App from "./App";
import syntheticModel from "./flow/__fixtures__/syntheticModel";

vi.mock("./api/client", () => ({
  apiFetch: vi.fn(),
}));

const mockApiFetch = vi.mocked(apiFetch);

beforeAll(() => {
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
});

const catalog = {
  global: { scopeId: "global", displayName: "Global" },
  products: [],
  apis: [],
};

const flow = { ...syntheticModel, scopeId: "global", scopeKind: "Global" };

describe("App integration", () => {
  afterEach(() => {
    cleanup();
    mockApiFetch.mockReset();
  });

  it("loads scopes and renders the policy flow for the selected scope", async () => {
    mockApiFetch.mockImplementation(async (path: string) =>
      path === "/api/scopes" ? catalog : flow,
    );

    render(<App />);

    fireEvent.click(await screen.findByRole("button", { name: "Global" }));

    expect(
      await screen.findByRole("region", {
        name: "Effective policy flow for global",
      }),
    ).toBeInTheDocument();
    expect(mockApiFetch).toHaveBeenCalledWith(
      "/api/policy/effective-flow?scope=global",
      expect.anything(),
    );
    fireEvent.click(screen.getByText("Display options"));
    expect(
      screen.getByRole("button", { name: "Back to scope selection" }),
    ).toBeInTheDocument();
    for (const stage of ["Inbound", "Backend", "Outbound", "On-error"]) {
      expect(screen.getByRole("button", { name: new RegExp(`^Stage lane, ${stage}(,|$)`) })).toBeInTheDocument();
    }
    expect(screen.getByRole("button", { name: /Expand Authenticate subscription/ })).toBeInTheDocument();
    expect(screen.getByText("Explicit response to caller")).toBeInTheDocument();
    expect(screen.getByText("Final backend response to caller")).toBeInTheDocument();
    expect(screen.getByText("Error response to caller")).toBeInTheDocument();
  });

  it("returns to and focuses selection, expanding a collapsed sidebar", async () => {
    mockApiFetch.mockImplementation(async (path: string) => {
      if (path === "/api/scopes") {
        return catalog;
      }
      return flow;
    });

    render(<App />);

    fireEvent.click(await screen.findByRole("button", { name: "Global" }));
    await screen.findByRole("region", {
      name: "Effective policy flow for global",
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Collapse scope selection panel" }),
    );
    fireEvent.click(screen.getByText("Display options"));
    fireEvent.click(
      await screen.findByRole("button", { name: "Back to scope selection" }),
    );

    expect(
      screen.getByText("Select a policy scope to visualize its execution path."),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Collapse scope selection panel" }),
    ).toHaveAttribute("aria-expanded", "true");
    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: "Choose a scope" }),
      ).toHaveFocus();
    });
  });

  it("surfaces a backend parse failure and provides the focused return path", async () => {
    mockApiFetch.mockImplementation(async (path: string) => {
      if (path === "/api/scopes") {
        return catalog;
      }
      throw new Error(
        "The effective policy for scope 'global' could not be parsed: malformed XML.",
      );
    });

    render(<App />);
    fireEvent.click(await screen.findByRole("button", { name: "Global" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "could not be parsed: malformed XML",
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Back to scope selection" }),
    );
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { name: "Choose a scope" }),
      ).toHaveFocus(),
    );
  });
});
