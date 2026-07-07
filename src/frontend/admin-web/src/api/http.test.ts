import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ApiError,
  apiFetch,
  setAccessToken,
  setSessionExpiredHandler,
  setTenantOverride,
  tryRestoreSession,
} from "./http";

// The contracts under test were all verified live against the real backend
// (see the doc comment in http.ts): envelope unwrap for core/WaAdmin/WaBilling,
// raw-DTO passthrough for WaGateway/WaIntel, the structured 403 step_up_required,
// 422 field-error dicts, and the 401 → refresh → retry cycle. These tests pin
// that behavior so a refactor of http.ts can't silently regress every screen.

// node environment has no sessionStorage — hand-rolled in-memory stand-in.
function fakeSessionStorage(): Storage {
  const store = new Map<string, string>();
  return {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
    clear: () => store.clear(),
    key: (i: number) => [...store.keys()][i] ?? null,
    get length() {
      return store.size;
    },
  };
}

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  vi.stubGlobal("sessionStorage", fakeSessionStorage());
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
  setAccessToken(null);
  setSessionExpiredHandler(() => {});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("envelope handling", () => {
  it("unwraps a {status,message,data} envelope to its data", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { status: true, message: null, data: { id: "u1" } }),
    );

    await expect(apiFetch("/identity/api/v1/x")).resolves.toEqual({ id: "u1" });
  });

  it("passes a raw DTO through untouched — string status is not an envelope", async () => {
    // WaGateway/WaIntel return raw DTOs; some carry a string `status` field
    // ("accepted", "DRAFT") plus a message-ish property. Envelope detection
    // must require a BOOLEAN status or every send result would be mangled.
    const dto = { status: "accepted", message: "queued", wamid: "wamid.1" };
    fetchMock.mockResolvedValueOnce(jsonResponse(200, dto));

    await expect(apiFetch("/messaging/api/v1/messages")).resolves.toEqual(dto);
  });

  it("passes arrays through even though they contain envelope-shaped keys", async () => {
    const rows = [{ status: true, message: null, data: 1 }];
    fetchMock.mockResolvedValueOnce(jsonResponse(200, rows));

    await expect(apiFetch("/intel/v1/rows")).resolves.toEqual(rows);
  });

  it("throws on an envelope with status false even when HTTP is 200", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, {
        status: false,
        message: { responseMessage: "Template not found." },
      }),
    );

    await expect(apiFetch("/admin/v1/x")).rejects.toThrow("Template not found.");
  });
});

describe("error normalization", () => {
  it("parses the structured 403 step_up_required envelope (no data property)", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(403, {
        status: false,
        message: { responseMessage: "step_up_required" },
      }),
    );

    const error = await apiFetch("/identity/api/v1/x").catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(403);
    expect((error as ApiError).code).toBe("step_up_required");
    expect((error as ApiError).isStepUpRequired).toBe(true);
  });

  it("detects step-up when it arrives as an errorMessage key instead", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(403, {
        status: false,
        message: { errorMessage: { step_up_required: ["Fresh OTP needed."] } },
      }),
    );

    const error = (await apiFetch("/x").catch((e: unknown) => e)) as ApiError;
    expect(error.isStepUpRequired).toBe(true);
  });

  it("maps a 422 envelope errorMessage dict to fieldErrors", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(422, {
        status: false,
        message: {
          errorMessage: {
            email: ["Email is already taken."],
            scopeId: ["Tenant-scoped roles require a tenant id."],
          },
        },
      }),
    );

    const error = (await apiFetch("/x").catch((e: unknown) => e)) as ApiError;
    expect(error.fieldErrors).toEqual({
      email: ["Email is already taken."],
      scopeId: ["Tenant-scoped roles require a tenant id."],
    });
    // With no responseMessage the human-readable text is the joined field messages.
    expect(error.message).toContain("Email is already taken.");
    expect(error.isStepUpRequired).toBe(false);
  });

  it("normalizes RFC 9457 ProblemDetails (framework 401/403/400)", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(400, {
        type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        title: "Bad Request",
        detail: "The request field 'page' is invalid.",
        errors: { page: ["Must be positive."] },
      }),
    );

    const error = (await apiFetch("/x").catch((e: unknown) => e)) as ApiError;
    expect(error.message).toBe("The request field 'page' is invalid.");
    expect(error.fieldErrors).toEqual({ page: ["Must be positive."] });
  });

  it("falls back to plain text and a generic message for non-JSON errors", async () => {
    fetchMock.mockResolvedValueOnce(new Response("upstream timeout", { status: 504 }));
    const textError = (await apiFetch("/x").catch((e: unknown) => e)) as ApiError;
    expect(textError.message).toBe("upstream timeout");

    fetchMock.mockResolvedValueOnce(new Response("", { status: 500 }));
    const emptyError = (await apiFetch("/x").catch((e: unknown) => e)) as ApiError;
    expect(emptyError.message).toBe("Request failed (500)");
  });
});

describe("401 refresh cycle", () => {
  it("refreshes once on 401, retries with the new token, and succeeds", async () => {
    setAccessToken("stale-token");
    fetchMock
      .mockResolvedValueOnce(jsonResponse(401, {}))
      .mockResolvedValueOnce(
        jsonResponse(200, {
          status: true,
          message: null,
          data: { accessToken: "fresh-token" },
        }),
      )
      .mockResolvedValueOnce(jsonResponse(200, { status: true, message: null, data: { ok: 1 } }));

    await expect(apiFetch("/admin/v1/x")).resolves.toEqual({ ok: 1 });

    expect(fetchMock).toHaveBeenCalledTimes(3);
    const [refreshUrl, refreshInit] = fetchMock.mock.calls[1];
    expect(String(refreshUrl)).toContain("/identity/api/v1/auth/refresh");
    // The HttpOnly cookie carries the token, but the body is still REQUIRED.
    expect(refreshInit?.body).toBe(JSON.stringify({ refreshToken: null }));
    const retryHeaders = fetchMock.mock.calls[2][1]?.headers as Record<string, string>;
    expect(retryHeaders.Authorization).toBe("Bearer fresh-token");
  });

  it("fires the session-expired handler and throws when refresh fails", async () => {
    const expired = vi.fn();
    setSessionExpiredHandler(expired);
    fetchMock
      .mockResolvedValueOnce(jsonResponse(401, {}))
      .mockResolvedValueOnce(jsonResponse(401, {}));

    const error = (await apiFetch("/x").catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(401);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it("restores a session from the refresh cookie on app load", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(200, { status: true, message: null, data: { accessToken: "restored" } }),
    );

    await expect(tryRestoreSession()).resolves.toBe("restored");
  });
});

describe("request assembly", () => {
  it("sends the X-Tenant-Id override and drops null query params", async () => {
    setTenantOverride("d0d6d0d8-caac-400c-896f-1839c0d07bfc");
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { status: true, message: null, data: [] }));

    await apiFetch("/admin/v1/x", { query: { search: "riya", page: 2, empty: null } });

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("?search=riya&page=2");
    expect(String(url)).not.toContain("empty");
    const headers = init?.headers as Record<string, string>;
    expect(headers["X-Tenant-Id"]).toBe("d0d6d0d8-caac-400c-896f-1839c0d07bfc");
    setTenantOverride(null);
  });

  it("sends bearer token, JSON body, and Idempotency-Key when provided", async () => {
    setAccessToken("token-1");
    fetchMock.mockResolvedValueOnce(jsonResponse(200, { status: true, message: null, data: {} }));

    await apiFetch("/messaging/api/v1/messages", {
      method: "POST",
      body: { to: "1555" },
      idempotencyKey: "key-1",
    });

    const init = fetchMock.mock.calls[0][1];
    const headers = init?.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer token-1");
    expect(headers["Content-Type"]).toBe("application/json");
    expect(headers["Idempotency-Key"]).toBe("key-1");
    expect(init?.body).toBe(JSON.stringify({ to: "1555" }));
    expect(init?.credentials).toBe("include");
  });
});
