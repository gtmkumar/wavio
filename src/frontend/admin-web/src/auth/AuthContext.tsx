import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import {
  apiFetch,
  getTenantOverride,
  setAccessToken,
  setSessionExpiredHandler,
  setTenantOverride,
  tryRestoreSession,
} from "@/api/http";
import type { TokenResponse } from "@/api/types";
import { decodeClaims, type SessionClaims } from "./claims";

interface AuthContextValue {
  /** null until the initial cookie-refresh bootstrap completes. */
  ready: boolean;
  user: SessionClaims | null;
  /** Active tenant override (platform_admin only, sent as X-Tenant-Id). */
  tenantOverride: string | null;
  login: (identifier: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  hasPermission: (code: string) => boolean;
  isPlatformAdmin: boolean;
  switchTenant: (tenantId: string | null) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [user, setUser] = useState<SessionClaims | null>(null);
  const [tenantOverride, setTenantOverrideState] = useState<string | null>(getTenantOverride());

  const applyToken = useCallback((accessToken: string | null) => {
    setAccessToken(accessToken);
    setUser(accessToken ? decodeClaims(accessToken) : null);
  }, []);

  // Restore the session from the HttpOnly lg_refresh cookie on first load.
  useEffect(() => {
    let cancelled = false;
    void tryRestoreSession().then((token) => {
      if (cancelled) return;
      applyToken(token);
      setReady(true);
    });
    setSessionExpiredHandler(() => applyToken(null));
    return () => {
      cancelled = true;
    };
  }, [applyToken]);

  const login = useCallback(
    async (identifier: string, password: string) => {
      const token = await apiFetch<TokenResponse>("/identity/api/v1/auth/password/login", {
        method: "POST",
        body: { identifier, password },
        skipRefresh: true,
      });
      applyToken(token.accessToken);
    },
    [applyToken],
  );

  const logout = useCallback(async () => {
    try {
      await apiFetch("/identity/api/v1/auth/logout", { method: "POST", skipRefresh: true });
    } catch {
      // Local logout must succeed even if the server call fails.
    }
    setTenantOverride(null);
    setTenantOverrideState(null);
    applyToken(null);
  }, [applyToken]);

  const switchTenant = useCallback((tenantId: string | null) => {
    setTenantOverride(tenantId);
    setTenantOverrideState(tenantId);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      ready,
      user,
      tenantOverride,
      login,
      logout,
      // Mirrors wavio.Utilities/Auth/PermissionHandler.cs: platform_admin
      // bypasses individual permission checks.
      hasPermission: (code: string) =>
        user != null && (user.userType === "platform_admin" || user.permissions.has(code)),
      isPlatformAdmin: user?.userType === "platform_admin",
      switchTenant,
    }),
    [ready, user, tenantOverride, login, logout, switchTenant],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
