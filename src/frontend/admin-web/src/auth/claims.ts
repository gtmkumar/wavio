/**
 * Access-token claims as issued by core.Infrastructure/Auth/JwtTokenService.cs:
 * sub, user_type, email, phone, tenant_id, scope_type, scope_id,
 * permissions (space-separated codes), scope_nodes, step_up_perms, amr.
 */
export interface SessionClaims {
  userId: string;
  email: string | null;
  userType: string;
  tenantId: string | null;
  scopeType: string | null;
  permissions: ReadonlySet<string>;
  /** Permission codes that additionally demand recent step-up auth. */
  stepUpPermissions: ReadonlySet<string>;
  expiresAt: number;
}

interface RawClaims {
  sub?: string;
  email?: string;
  user_type?: string;
  tenant_id?: string;
  scope_type?: string;
  permissions?: string;
  step_up_perms?: string;
  exp?: number;
}

function splitCodes(value: string | undefined): ReadonlySet<string> {
  return new Set((value ?? "").split(" ").filter(Boolean));
}

/** Decode the JWT payload (no signature check — the backend enforces authz). */
export function decodeClaims(accessToken: string): SessionClaims | null {
  const payload = accessToken.split(".")[1];
  if (!payload) return null;
  try {
    const json = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
    const raw = JSON.parse(json) as RawClaims;
    if (!raw.sub) return null;
    return {
      userId: raw.sub,
      email: raw.email ?? null,
      userType: raw.user_type ?? "",
      tenantId: raw.tenant_id ?? null,
      scopeType: raw.scope_type ?? null,
      permissions: splitCodes(raw.permissions),
      stepUpPermissions: splitCodes(raw.step_up_perms),
      expiresAt: (raw.exp ?? 0) * 1000,
    };
  } catch {
    return null;
  }
}
