import type { ApiMessage, Envelope, TokenResponse } from "./types";

/**
 * Single HTTP layer for every Wavio service, called through the YARP gateway
 * (VITE_API_BASE, default http://localhost:8080). Owns the cross-cutting
 * behavior the backend actually implements:
 *
 * - Bearer access token, kept in memory only (never localStorage). The
 *   HttpOnly `lg_refresh` cookie restores sessions across reloads.
 * - Silent single-flight refresh on 401 via /identity/api/v1/auth/refresh
 *   (credentials: "include" — the gateway's Dev CORS allows this origin).
 * - Envelope unwrap for core/WaAdmin/WaBilling ({status,message,data});
 *   WaGateway/WaIntel return raw DTOs and pass through untouched.
 * - Error normalization to ApiError: envelope Message (incl. 422 per-field
 *   dict and 403 step_up_required), ProblemDetails, or plain text.
 * - Optional Idempotency-Key (required by POST /messaging/api/v1/messages)
 *   and X-Tenant-Id override for platform admins.
 */

const API_BASE: string = import.meta.env.VITE_API_BASE ?? "";

// ── Access-token store (module state; AuthContext subscribes) ────────────────

let accessToken: string | null = null;
let onSessionExpired: (() => void) | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

/** AuthContext registers this to force logout when refresh fails. */
export function setSessionExpiredHandler(handler: () => void): void {
  onSessionExpired = handler;
}

// ── Tenant override (platform_admin acting on a tenant via X-Tenant-Id) ──────

const TENANT_OVERRIDE_KEY = "wavio.tenantOverride";

export function getTenantOverride(): string | null {
  return sessionStorage.getItem(TENANT_OVERRIDE_KEY);
}

export function setTenantOverride(tenantId: string | null): void {
  if (tenantId) sessionStorage.setItem(TENANT_OVERRIDE_KEY, tenantId);
  else sessionStorage.removeItem(TENANT_OVERRIDE_KEY);
}

// ── Errors ───────────────────────────────────────────────────────────────────

export class ApiError extends Error {
  readonly status: number;
  /** Backend's ErrorMessageEnum code or a well-known marker like "step_up_required". */
  readonly code: string | number | null;
  /** 422 validation dict: field name → messages. */
  readonly fieldErrors: Record<string, string[]> | null;

  constructor(
    status: number,
    message: string,
    code: string | number | null = null,
    fieldErrors: Record<string, string[]> | null = null,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }

  get isStepUpRequired(): boolean {
    return this.status === 403 && this.code === "step_up_required";
  }
}

function messageToError(status: number, message: ApiMessage | null | undefined): ApiError {
  const fieldErrors = message?.errorMessage ?? null;
  const isStepUp =
    message?.responseMessage === "step_up_required" ||
    (fieldErrors != null && "step_up_required" in fieldErrors);
  const text =
    message?.responseMessage ??
    (fieldErrors ? Object.values(fieldErrors).flat().join(" ") : null) ??
    `Request failed (${status})`;
  return new ApiError(
    status,
    text,
    isStepUp ? "step_up_required" : (message?.errorTypeCode ?? null),
    fieldErrors,
  );
}

function isEnvelope(value: unknown): value is Envelope<unknown> {
  // The plain `Response` envelope (e.g. the structured 403 step_up_required)
  // has no `data` property, so only status+message are required markers. Raw
  // DTO `status` fields are strings ("accepted", "DRAFT"), never booleans.
  return (
    typeof value === "object" &&
    value !== null &&
    !Array.isArray(value) &&
    typeof (value as { status?: unknown }).status === "boolean" &&
    "message" in value
  );
}

// ── Refresh (single-flight) ──────────────────────────────────────────────────

let refreshInFlight: Promise<boolean> | null = null;

async function refreshOnce(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const res = await fetch(`${API_BASE}/identity/api/v1/auth/refresh`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        // Body is required; the lg_refresh HttpOnly cookie carries the token.
        body: JSON.stringify({ refreshToken: null }),
      });
      if (!res.ok) return false;
      const envelope = (await res.json()) as Envelope<TokenResponse>;
      if (!envelope.status || !envelope.data) return false;
      setAccessToken(envelope.data.accessToken);
      return true;
    } catch {
      return false;
    } finally {
      refreshInFlight = null;
    }
  })();
  return refreshInFlight;
}

/** Session bootstrap on app load: try the refresh cookie, return the new token. */
export async function tryRestoreSession(): Promise<string | null> {
  return (await refreshOnce()) ? accessToken : null;
}

// ── Core request ─────────────────────────────────────────────────────────────

export interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Query string parameters; null/undefined entries are dropped. */
  query?: Record<string, string | number | boolean | null | undefined>;
  idempotencyKey?: string;
  signal?: AbortSignal;
  /** Skip the 401→refresh→retry cycle (used by auth endpoints themselves). */
  skipRefresh?: boolean;
}

async function parseBody(res: Response): Promise<unknown> {
  if (res.status === 204) return null;
  const text = await res.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function toError(status: number, body: unknown): ApiError {
  if (isEnvelope(body)) return messageToError(status, body.message);
  if (typeof body === "object" && body !== null) {
    // RFC 9457 ProblemDetails (framework 401/403) or raw DTO rejections
    // (e.g. WaGateway SendMessageResultDto with status "rejected").
    const o = body as Record<string, unknown>;
    const text =
      (typeof o.detail === "string" && o.detail) ||
      (typeof o.title === "string" && o.title) ||
      (typeof o.errorMessage === "string" && o.errorMessage) ||
      `Request failed (${status})`;
    const code = (typeof o.errorCode === "string" && o.errorCode) || null;
    const fieldErrors =
      typeof o.errors === "object" && o.errors !== null
        ? (o.errors as Record<string, string[]>)
        : null;
    return new ApiError(status, text, code, fieldErrors);
  }
  return new ApiError(status, typeof body === "string" && body ? body : `Request failed (${status})`);
}

/**
 * Perform a request and return the payload: envelope responses are unwrapped
 * to their `data`, raw-DTO responses are returned as-is. Throws ApiError on
 * HTTP errors and on envelope { status: false }.
 */
export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, query, idempotencyKey, signal, skipRefresh = false } = options;

  let url = `${API_BASE}${path}`;
  if (query) {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) {
      if (value != null) params.set(key, String(value));
    }
    const qs = params.toString();
    if (qs) url += `?${qs}`;
  }

  const doFetch = () => {
    const headers: Record<string, string> = {};
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (idempotencyKey) headers["Idempotency-Key"] = idempotencyKey;
    const tenantOverride = getTenantOverride();
    if (tenantOverride) headers["X-Tenant-Id"] = tenantOverride;
    return fetch(url, {
      method,
      headers,
      credentials: "include",
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal,
    });
  };

  let res = await doFetch();

  if (res.status === 401 && !skipRefresh) {
    if (await refreshOnce()) {
      res = await doFetch();
    } else {
      onSessionExpired?.();
      throw new ApiError(401, "Session expired. Please sign in again.");
    }
  }

  const parsed = await parseBody(res);

  if (!res.ok && res.status !== 207) throw toError(res.status, parsed);

  if (isEnvelope(parsed)) {
    if (!parsed.status) throw messageToError(res.status, parsed.message);
    return parsed.data as T;
  }
  return parsed as T;
}
