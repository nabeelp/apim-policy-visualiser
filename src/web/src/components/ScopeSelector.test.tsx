import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../api/client";
import ScopeSelector from "./ScopeSelector";

vi.mock("../api/client", () => ({
  apiFetch: vi.fn(),
}));

const mockApiFetch = vi.mocked(apiFetch);

const catalog = {
  global: {
    scopeId: "global",
    kind: "Global",
    displayName: "Global",
  },
  products: [
    {
      scopeId: "products/storefront",
      kind: "Product",
      name: "storefront",
      displayName: "Storefront",
      apis: [
        {
          scopeId: "apis/orders",
          kind: "Api",
          name: "orders",
          displayName: "Orders API",
          path: "orders",
          operations: [
            {
              scopeId: "apis/orders/operations/create-order",
              kind: "Operation",
              name: "create-order",
              displayName: "Create order",
              method: "POST",
              urlTemplate: "/",
            },
          ],
        },
      ],
    },
  ],
  apis: [
    {
      scopeId: "apis/orders",
      kind: "Api",
      name: "orders",
      displayName: "Orders API",
      path: "orders",
      operations: [
        {
          scopeId: "apis/orders/operations/create-order",
          kind: "Operation",
          name: "create-order",
          displayName: "Create order",
          method: "POST",
          urlTemplate: "/",
        },
      ],
    },
  ],
};

describe("ScopeSelector", () => {
  beforeEach(() => {
    mockApiFetch.mockReset();
  });

  afterEach(cleanup);

  it("loads scopes and emits Product, API, and Operation scope IDs", async () => {
    mockApiFetch.mockResolvedValue(catalog);
    const onScopeSelect = vi.fn();

    render(<ScopeSelector onScopeSelect={onScopeSelect} />);

    expect(screen.getByRole("status")).toHaveTextContent(
      "Loading policy scopes...",
    );
    expect(mockApiFetch).toHaveBeenCalledWith(
      "/api/scopes",
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );

    fireEvent.change(
      await screen.findByRole("combobox", { name: "Product" }),
      { target: { value: "products/storefront" } },
    );
    expect(onScopeSelect).toHaveBeenLastCalledWith("products/storefront");

    fireEvent.change(screen.getByRole("combobox", { name: "API" }), {
      target: { value: "apis/orders" },
    });
    expect(onScopeSelect).toHaveBeenLastCalledWith("apis/orders");

    fireEvent.change(screen.getByRole("combobox", { name: "Operation" }), {
      target: { value: "apis/orders/operations/create-order" },
    });
    expect(onScopeSelect).toHaveBeenLastCalledWith(
      "apis/orders/operations/create-order",
    );
  });

  it("allows the Global scope to be selected and resets the cascade", async () => {
    mockApiFetch.mockResolvedValue(catalog);
    const onScopeSelect = vi.fn();

    render(<ScopeSelector onScopeSelect={onScopeSelect} />);

    fireEvent.change(
      await screen.findByRole("combobox", { name: "Product" }),
      { target: { value: "products/storefront" } },
    );
    fireEvent.click(screen.getByRole("button", { name: "Global" }));

    expect(onScopeSelect).toHaveBeenLastCalledWith("global");
    expect(screen.getByRole("combobox", { name: "Product" })).toHaveValue("");
    expect(screen.getByRole("combobox", { name: "API" })).toHaveValue("");
    expect(screen.getByRole("combobox", { name: "Operation" })).toBeDisabled();
  });

  it("clears descendants when a parent changes and reset returns to Global", async () => {
    mockApiFetch.mockResolvedValue(catalog);
    const onScopeSelect = vi.fn();

    render(<ScopeSelector onScopeSelect={onScopeSelect} />);

    fireEvent.change(
      await screen.findByRole("combobox", { name: "Product" }),
      { target: { value: "products/storefront" } },
    );
    fireEvent.change(screen.getByRole("combobox", { name: "API" }), {
      target: { value: "apis/orders" },
    });
    fireEvent.change(screen.getByRole("combobox", { name: "Operation" }), {
      target: { value: "apis/orders/operations/create-order" },
    });

    fireEvent.change(screen.getByRole("combobox", { name: "Product" }), {
      target: { value: "" },
    });
    expect(screen.getByRole("combobox", { name: "API" })).toHaveValue("");
    expect(screen.getByRole("combobox", { name: "Operation" })).toBeDisabled();
    expect(onScopeSelect).toHaveBeenLastCalledWith("global");

    fireEvent.change(screen.getByRole("combobox", { name: "API" }), {
      target: { value: "apis/orders" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Reset hierarchy" }),
    );
    expect(onScopeSelect).toHaveBeenLastCalledWith("global");
    expect(screen.getByRole("combobox", { name: "API" })).toHaveValue("");
  });

  it("shows the backend error when loading scopes fails", async () => {
    mockApiFetch.mockRejectedValue(
      new Error("Access to APIM service 'contoso' was denied."),
    );

    render(<ScopeSelector onScopeSelect={vi.fn()} />);

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(
        "Access to APIM service 'contoso' was denied.",
      );
    });
    expect(
      screen.queryByRole("combobox", { name: "Product" }),
    ).not.toBeInTheDocument();
  });
});
