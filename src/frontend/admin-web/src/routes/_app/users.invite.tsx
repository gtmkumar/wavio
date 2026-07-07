import { useMemo, useState, type FormEvent } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import { useRoles } from "@/api/queries/roles";
import { useInviteUser } from "@/api/queries/users";
import { USER_TYPES } from "@/api/types";
import { useAuth } from "@/auth/AuthContext";
import { FieldErrors } from "@/components/shared/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Sheet } from "@/components/ui/sheet";

export const Route = createFileRoute("/_app/users/invite")({
  component: InviteUserPage,
});

const TYPE_LABELS: Record<string, string> = {
  platform_admin: "Platform admin",
  tenant_admin: "Tenant admin",
  staff: "Staff",
  auditor: "Auditor",
  support: "Support",
};

function InviteUserPage() {
  const navigate = useNavigate();
  const { isPlatformAdmin, tenantOverride } = useAuth();
  const roles = useRoles();
  const invite = useInviteUser();

  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [roleId, setRoleId] = useState("");
  const [userType, setUserType] = useState("staff");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<ApiError | null>(null);

  const close = () => void navigate({ to: "/users" });

  // Tenant-scoped admins may only hand out tenant-scoped roles; the backend
  // enforces this — hiding platform roles here just avoids guaranteed rejections.
  const assignableRoles = useMemo(
    () =>
      (roles.data ?? []).filter(
        (r) => r.status === "active" && (isPlatformAdmin || r.scopeType === "tenant"),
      ),
    [roles.data, isPlatformAdmin],
  );
  const selectedRole = assignableRoles.find((r) => r.id === roleId);
  // A tenant-scoped role must bind to a concrete tenant; a platform admin
  // acting platform-wide has none, and the backend rejects the grant.
  const needsTenant =
    isPlatformAdmin && !tenantOverride && selectedRole?.scopeType === "tenant";

  function onRoleChange(id: string) {
    setRoleId(id);
    // System role codes double as account types — keep them in sync by default.
    const role = assignableRoles.find((r) => r.id === id);
    if (role && (USER_TYPES as readonly string[]).includes(role.code)) setUserType(role.code);
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (!selectedRole) {
      setError(new ApiError(0, "Pick a role for the new user."));
      return;
    }
    try {
      await invite.mutateAsync({
        email: email.trim(),
        phone: phone.trim() || null,
        firstName: firstName.trim() || null,
        lastName: lastName.trim() || null,
        userType,
        roleId: selectedRole.id,
        scopeType: selectedRole.scopeType,
        // Tenant-scoped roles bind to the acting tenant: the platform admin's
        // X-Tenant-Id override here, or (null) the caller's own tenant claim.
        scopeId: selectedRole.scopeType === "tenant" ? (tenantOverride ?? null) : null,
        password: password || null,
      });
      toast.success(
        password
          ? `${email.trim()} created — they can sign in with the password you set`
          : `Invitation sent to ${email.trim()}`,
      );
      await navigate({ to: "/users" });
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <Sheet
      open
      onClose={close}
      size="md"
      title="Invite user"
      description="They receive an email with a link to set their password and sign in."
    >
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="firstName">First name</Label>
            <Input id="firstName" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            <FieldErrors errors={error?.fieldErrors} field="firstName" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="lastName">Last name</Label>
            <Input id="lastName" value={lastName} onChange={(e) => setLastName(e.target.value)} />
            <FieldErrors errors={error?.fieldErrors} field="lastName" />
          </div>
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="email">Email</Label>
          <Input
            id="email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="teammate@company.com"
            aria-invalid={!!error?.fieldErrors?.email}
          />
          <FieldErrors errors={error?.fieldErrors} field="email" />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="phone">Phone (optional)</Label>
          <Input
            id="phone"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder="+91 98765 43210"
          />
          <FieldErrors errors={error?.fieldErrors} field="phone" />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="role">Role</Label>
          <Select id="role" value={roleId} onChange={(e) => onRoleChange(e.target.value)}>
            <option value="">{roles.isPending ? "Loading roles…" : "Select a role"}</option>
            {assignableRoles.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name} · {r.scopeType === "platform" ? "Platform" : "Tenant"}
              </option>
            ))}
          </Select>
          <p className="text-xs text-muted-foreground">
            The role decides what they can see and do. You can change it later.
          </p>
          {needsTenant ? (
            <p className="rounded-md bg-amber-500/10 p-2 text-xs text-amber-700 dark:text-amber-400">
              This role belongs to a tenant — pick one with "Acting as platform" in the top bar
              first.
            </p>
          ) : null}
          <FieldErrors errors={error?.fieldErrors} field="roleId" />
          <FieldErrors errors={error?.fieldErrors} field="scopeId" />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="userType">Account type</Label>
          <Select id="userType" value={userType} onChange={(e) => setUserType(e.target.value)}>
            {USER_TYPES.filter((t) => isPlatformAdmin || t !== "platform_admin").map((t) => (
              <option key={t} value={t}>
                {TYPE_LABELS[t]}
              </option>
            ))}
          </Select>
          <FieldErrors errors={error?.fieldErrors} field="userType" />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="password">Temporary password (optional)</Label>
          <Input
            id="password"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="Leave empty to send an email invite"
          />
          <p className="text-xs text-muted-foreground">
            If set, the account is active immediately and you share the password yourself.
          </p>
          <FieldErrors errors={error?.fieldErrors} field="password" />
        </div>
        {error && !error.fieldErrors ? (
          <p className="text-sm text-destructive">{error.message}</p>
        ) : null}
        <div className="flex justify-end gap-2 border-t pt-4">
          <Button type="button" variant="ghost" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" disabled={invite.isPending || !email.trim() || !roleId || needsTenant}>
            {invite.isPending ? "Inviting…" : password ? "Create user" : "Send invite"}
          </Button>
        </div>
      </form>
    </Sheet>
  );
}
