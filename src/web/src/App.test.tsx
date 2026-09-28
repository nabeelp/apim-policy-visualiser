import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import App from "./App";

describe("App", () => {
  it("renders the desktop side panel and policy canvas", () => {
    render(<App />);

    expect(
      screen.getByRole("complementary", { name: "Choose a scope" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("main", { name: "Effective policy flow" }),
    ).toBeInTheDocument();
  });
});
