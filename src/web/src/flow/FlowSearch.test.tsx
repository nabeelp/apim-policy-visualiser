import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import syntheticModel from "./__fixtures__/syntheticModel";
import FlowSearch from "./FlowSearch";
import { buildSearchIndex, searchFlow } from "./search";

function deepFreeze<T>(value: T): T {
  if (value && typeof value === "object" && !Object.isFrozen(value)) {
    Object.freeze(value);
    Object.values(value).forEach(deepFreeze);
  }
  return value;
}

describe("flow search", () => {
  afterEach(cleanup);
  it("finds fragment, variable, status and source-only terms without mutation", () => {
    const model = deepFreeze(syntheticModel);
    const counts = [model.elements.length, model.edges.length];
    const index = buildSearchIndex(model);
    expect(searchFlow(index, "Authenticate subscription")[0].matchKind).toBe("fragment");
    expect(searchFlow(index, "responseOwner").some((result) => result.matchKind === "variable")).toBe(true);
    expect(searchFlow(index, "403").some((result) => result.matchKind === "status code")).toBe(true);
    expect(searchFlow(index, "header-name").some((result) => result.matchKind === "source")).toBe(true);
    expect([model.elements.length, model.edges.length]).toEqual(counts);
  });

  it("is labelled, keyboard navigable and chooses a nested result", () => {
    const onChoose = vi.fn();
    render(<FlowSearch model={syntheticModel} onChoose={onChoose} />);
    const input = screen.getByRole("combobox", { name: "Search policy flow" });
    fireEvent.change(input, { target: { value: "Forward request" } });
    expect(screen.getByRole("option", { name: /Forward request to backend/ })).toBeInTheDocument();
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onChoose).toHaveBeenCalledWith(
      expect.objectContaining({ elementId: "backend/0/2" }),
    );
  });
});
