import { useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import {
  Activity,
  Building2,
  KeyRound,
  LayoutDashboard,
  LogOut,
  Megaphone,
  MessageSquareText,
  Moon,
  Send,
  ShieldCheck,
  Sun,
  UsersRound,
  Wallet,
} from "lucide-react";
import { useServicesHealth } from "@/api/queries/health";
import { useAuth } from "@/auth/AuthContext";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";
import { applyTheme, getTheme, type Theme } from "./theme";

const NAV_ITEMS = [
  { to: "/", label: "Overview", icon: LayoutDashboard, permission: null },
  { to: "/campaigns", label: "Campaigns", icon: Megaphone, permission: "campaigns.list" },
  { to: "/templates", label: "Templates", icon: MessageSquareText, permission: "templates.list" },
  { to: "/quality", label: "Quality", icon: Activity, permission: "quality.health.read" },
  { to: "/billing", label: "Billing", icon: Wallet, permission: "billing.quotas.read" },
  { to: "/consent", label: "Consent", icon: ShieldCheck, permission: "consent.read" },
  { to: "/users", label: "Users", icon: UsersRound, permission: "users.list" },
  { to: "/roles", label: "Roles", icon: KeyRound, permission: "roles.list" },
  { to: "/messages", label: "Send Message", icon: Send, permission: "messages.send" },
] as const;

function HealthDot() {
  const { data } = useServicesHealth();
  const overall = data?.overall ?? "Unknown";
  const color =
    overall === "Healthy"
      ? "bg-emerald-500"
      : overall === "Unknown"
        ? "bg-zinc-400"
        : "bg-amber-500";
  const down = data?.services.filter((s) => s.status !== "Healthy") ?? [];
  return (
    <div
      className="flex items-center gap-2 text-xs text-sidebar-muted"
      title={down.length ? `Degraded: ${down.map((s) => s.service).join(", ")}` : "All services healthy"}
    >
      <span className={cn("size-2 rounded-full", color)} />
      {overall === "Healthy" ? "All systems normal" : down.length ? `${down.length} service(s) degraded` : overall}
    </div>
  );
}

function TenantSwitcher() {
  const { isPlatformAdmin, tenantOverride, switchTenant } = useAuth();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState(tenantOverride ?? "");
  if (!isPlatformAdmin) return null;

  function apply(tenantId: string | null) {
    switchTenant(tenantId);
    // Every cached query was fetched under the previous tenant context.
    void queryClient.invalidateQueries();
    setOpen(false);
  }
  return (
    <>
      <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
        <Building2 />
        {tenantOverride ? `Tenant ${tenantOverride.slice(0, 8)}…` : "Acting as platform"}
      </Button>
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="Act on a tenant"
        description="Platform admins can send X-Tenant-Id to work within a tenant's data. Leave empty to act platform-wide."
      >
        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="tenant-id">Tenant ID (UUID)</Label>
            <Input
              id="tenant-id"
              placeholder="00000000-0000-0000-0000-000000000000"
              value={value}
              onChange={(e) => setValue(e.target.value)}
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button
              variant="ghost"
              onClick={() => {
                setValue("");
                apply(null);
              }}
            >
              Clear override
            </Button>
            <Button onClick={() => apply(value.trim() || null)}>Apply</Button>
          </div>
        </div>
      </Dialog>
    </>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { user, logout, hasPermission } = useAuth();
  const [theme, setTheme] = useState<Theme>(getTheme);

  function toggleTheme() {
    const next = theme === "dark" ? "light" : "dark";
    applyTheme(next);
    setTheme(next);
  }

  return (
    <div className="flex min-h-screen">
      <aside className="fixed inset-y-0 left-0 z-40 flex w-60 flex-col bg-sidebar text-sidebar-foreground">
        <div className="flex h-14 items-center gap-2.5 border-b border-sidebar-border px-4">
          <div className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <MessageSquareText className="size-4.5" />
          </div>
          <div className="leading-tight">
            <p className="text-sm font-semibold text-white">Wavio</p>
            <p className="text-[11px] text-sidebar-muted">WhatsApp Platform</p>
          </div>
        </div>
        <nav className="flex-1 space-y-1 overflow-y-auto p-3">
          {NAV_ITEMS.filter((item) => !item.permission || hasPermission(item.permission)).map(
            (item) => (
              <Link
                key={item.to}
                to={item.to}
                activeOptions={{ exact: item.to === "/" }}
                className="flex items-center gap-3 rounded-md px-3 py-2 text-sm text-sidebar-muted transition-colors hover:bg-sidebar-accent hover:text-sidebar-foreground [&.active]:bg-sidebar-accent [&.active]:font-medium [&.active]:text-sidebar-accent-foreground"
              >
                <item.icon className="size-4" />
                {item.label}
              </Link>
            ),
          )}
        </nav>
        <div className="space-y-3 border-t border-sidebar-border p-4">
          <HealthDot />
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0">
              <p className="truncate text-xs font-medium">{user?.email ?? user?.userId}</p>
              <Badge variant="secondary" className="mt-1 text-[10px]">
                {user?.userType ?? "user"}
              </Badge>
            </div>
            <div className="flex shrink-0 items-center">
              <Button
                variant="ghost"
                size="icon"
                onClick={toggleTheme}
                className="text-sidebar-muted hover:bg-sidebar-accent hover:text-sidebar-foreground"
                aria-label="Toggle theme"
              >
                {theme === "dark" ? <Sun /> : <Moon />}
              </Button>
              <Button
                variant="ghost"
                size="icon"
                onClick={() => void logout()}
                className="text-sidebar-muted hover:bg-sidebar-accent hover:text-sidebar-foreground"
                aria-label="Sign out"
              >
                <LogOut />
              </Button>
            </div>
          </div>
        </div>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col pl-60">
        <header className="sticky top-0 z-30 flex h-14 items-center justify-end gap-3 border-b bg-background/80 px-6 backdrop-blur">
          <TenantSwitcher />
        </header>
        <main className="mx-auto w-full max-w-6xl flex-1 p-6">{children}</main>
      </div>
    </div>
  );
}
