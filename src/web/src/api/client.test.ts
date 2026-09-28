import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch } from "./client";

describe("apiFetch", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("preserves a problem detail and HTTP status", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            title: "Policy parse failure",
            detail: "Policy XML is malformed near line 14.",
            status: 422,
          }),
          {
            status: 422,
            headers: { "content-type": "application/problem+json" },
          },
        ),
      ),
    );

    const error = await apiFetch("/api/policy/effective-flow").catch(
      (caught: unknown) => caught,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      message: "Policy XML is malformed near line 14.",
      status: 422,
      problem: { title: "Policy parse failure" },
    });
  });

  it("keeps a malformed JSON error body readable", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response("Gateway returned malformed JSON", {
          status: 502,
          headers: { "content-type": "application/json" },
        }),
      ),
    );

    await expect(apiFetch("/api/scopes")).rejects.toMatchObject({
      message: "Gateway returned malformed JSON",
      status: 502,
    });
  });

  it("reports an unreadable successful JSON response clearly", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response("not-json", {
          status: 200,
          headers: { "content-type": "application/json" },
        }),
      ),
    );

    await expect(apiFetch("/api/scopes")).rejects.toMatchObject({
      message:
        "The API returned a successful response that could not be read as JSON.",
      status: 200,
    });
  });
});
