import { useState, type FormEvent } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { KeyRound, MailPlus, Pencil, ShieldAlert, ShieldCheck, UserCheck } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import { useRoles } from "@/api/queries/roles";
import {
  useChangeRole,
  usePeople,
  useResendInvite,
  useSetPersonStatus,
  useSetUserPassword,
  useUpdateUser,
  useUser,
} from "@/api/queries/users";
import { useAuth } from "@/auth/AuthContext";
import { useStepUpGuard } from "@/auth/StepUpDialog";
import { ErrorState, FieldErrors, LoadingRows } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Sheet } from "@/components/ui/sheet";
import { formatDateTime } from "@/lib/utils";

export const Route = createFileRoute("/_app/users/$userId")({
  component: UserDetailPage,
});

function Row({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <span className="text-muted-foreground">{label}</span>
      <span className="truncate text-right">{value || "—"}</span>
    </div>
  );
}

function UserDetailPage() {
  const { userId } = Route.useParams();
  const navigate = useNavigate();
  const { hasPermission, user: session, tenantOverride } = useAuth();
  const user = useUser(userId);
  // The user endpoint has no membership info — look the person up in the
  // directory by email to show their current role and scope.
  const lookup = usePeople(
    { search: user.data?.email ?? "", pageSize: 5 },
    { enabled: !!user.data?.email },
  );
  const person = lookup.data?.people.list.find((p) => p.id === userId);

  const roles = useRoles();
  const update = useUpdateUser(userId);
  const setStatus = useSetPersonStatus(userId);
  const changeRole = useChangeRole(userId);
  const setPassword = useSetUserPassword(userId);
  const resendInvite = useResendInvite(userId);
  const { guard, stepUpDialog } = useStepUpGuard();

  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState({ firstName: "", lastName: "", designation: "", phone: "" });
  const [dialog, setDialog] = useState<null | "activate" | "suspend" | "password">(null);
  const [dialogPassword, setDialogPassword] = useState("");
  const [newRoleId, setNewRoleId] = useState("");
  const [error, setError] = useState<ApiError | null>(null);

  const close = () => void navigate({ to: "/users" });
  const u = user.data;
  const isSelf = session?.userId === userId;
  const name =
    u?.displayName || `${u?.firstName ?? ""} ${u?.lastName ?? ""}`.trim() || u?.email || "User";

  function startEdit() {
    setForm({
      firstName: u?.firstName ?? "",
      lastName: u?.lastName ?? "",
      designation: u?.designation ?? "",
      phone: u?.phoneE164 ?? "",
    });
    setError(null);
    setEditing(true);
  }

  async function onSaveProfile(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      // "" clears a field server-side; null would leave it unchanged.
      await update.mutateAsync({
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        designation: form.designation.trim(),
        phone: form.phone.trim(),
      });
      toast.success("Profile updated");
      setEditing(false);
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  async function onSetStatus(action: "activate" | "suspend" | "reactivate", password?: string) {
    setError(null);
    try {
      await setStatus.mutateAsync({ action, password: password || null });
      setDialog(null);
      setDialogPassword("");
      toast.success(
        action === "suspend"
          ? `${name} suspended — they can no longer sign in`
          : `${name} is now active`,
      );
    } catch (err) {
      const apiErr = err instanceof ApiError ? err : new ApiError(0, "Could not reach the server.");
      setError(apiErr);
      if (!apiErr.fieldErrors) toast.error(apiErr.message);
    }
  }

  async function onChangeRole() {
    const role = (roles.data ?? []).find((r) => r.id === newRoleId);
    if (!role) return;
    try {
      // memberships.grant is a §8 step-up action — the guard prompts for OTP and retries.
      await guard(() =>
        changeRole.mutateAsync({
          roleId: role.id,
          scopeType: role.scopeType,
          // Acting tenant for tenant-scoped roles (platform-admin override); the
          // backend falls back to the caller's own tenant claim when null.
          scopeId: role.scopeType === "tenant" ? (tenantOverride ?? null) : null,
        }),
      );
      toast.success(`Role changed to ${role.name}`);
      setNewRoleId("");
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Role change failed.");
    }
  }

  async function onSetPassword() {
    try {
      // users.set_password is a §8 step-up action.
      await guard(() => setPassword.mutateAsync(dialogPassword));
      setDialog(null);
      setDialogPassword("");
      toast.success("Password updated");
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Password change failed.");
    }
  }

  async function onResendInvite() {
    try {
      await resendInvite.mutateAsync();
      toast.success(`Invite re-sent to ${u?.email}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Could not resend the invite.");
    }
  }

  const canUpdate = hasPermission("users.update");

  return (
    <Sheet
      open
      onClose={close}
      size="lg"
      title={name}
      description={u ? `${u.email ?? u.phoneE164 ?? ""}${isSelf ? " · this is you" : ""}` : undefined}
      actions={
        u ? (
          <>
            <StatusBadge status={u.status} />
            {canUpdate && (u.status === "invited" || u.status === "locked") ? (
              <Button size="sm" onClick={() => setDialog("activate")}>
                <UserCheck /> Activate
              </Button>
            ) : null}
            {canUpdate && u.status === "active" && !isSelf ? (
              <Button size="sm" variant="destructive" onClick={() => setDialog("suspend")}>
                <ShieldAlert /> Suspend
              </Button>
            ) : null}
            {canUpdate && u.status === "suspended" ? (
              <Button size="sm" onClick={() => void onSetStatus("reactivate")}>
                <ShieldCheck /> Reactivate
              </Button>
            ) : null}
          </>
        ) : undefined
      }
    >
      {user.isPending ? (
        <LoadingRows count={6} />
      ) : user.isError ? (
        <ErrorState error={user.error} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader className="flex-row items-center justify-between space-y-0">
              <CardTitle className="text-base">Profile</CardTitle>
              {canUpdate && !editing ? (
                <Button size="sm" variant="outline" onClick={startEdit}>
                  <Pencil /> Edit
                </Button>
              ) : null}
            </CardHeader>
            <CardContent className="text-sm">
              {editing ? (
                <form onSubmit={onSaveProfile} noValidate className="space-y-3">
                  <div className="grid gap-3 sm:grid-cols-2">
                    <div className="space-y-1.5">
                      <Label htmlFor="edit-first">First name</Label>
                      <Input
                        id="edit-first"
                        value={form.firstName}
                        onChange={(e) => setForm((f) => ({ ...f, firstName: e.target.value }))}
                      />
                      <FieldErrors errors={error?.fieldErrors} field="firstName" />
                    </div>
                    <div className="space-y-1.5">
                      <Label htmlFor="edit-last">Last name</Label>
                      <Input
                        id="edit-last"
                        value={form.lastName}
                        onChange={(e) => setForm((f) => ({ ...f, lastName: e.target.value }))}
                      />
                      <FieldErrors errors={error?.fieldErrors} field="lastName" />
                    </div>
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="edit-designation">Designation</Label>
                    <Input
                      id="edit-designation"
                      value={form.designation}
                      onChange={(e) => setForm((f) => ({ ...f, designation: e.target.value }))}
                      placeholder="e.g. Support lead"
                    />
                    <FieldErrors errors={error?.fieldErrors} field="designation" />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="edit-phone">Phone</Label>
                    <Input
                      id="edit-phone"
                      value={form.phone}
                      onChange={(e) => setForm((f) => ({ ...f, phone: e.target.value }))}
                    />
                    <FieldErrors errors={error?.fieldErrors} field="phone" />
                  </div>
                  {error && !error.fieldErrors ? (
                    <p className="text-sm text-destructive">{error.message}</p>
                  ) : null}
                  <div className="flex justify-end gap-2">
                    <Button type="button" variant="ghost" size="sm" onClick={() => setEditing(false)}>
                      Cancel
                    </Button>
                    <Button type="submit" size="sm" disabled={update.isPending}>
                      {update.isPending ? "Saving…" : "Save"}
                    </Button>
                  </div>
                </form>
              ) : (
                <div className="space-y-3">
                  <Row label="Email" value={u!.email} />
                  <Row label="Phone" value={u!.phoneE164} />
                  <Row label="Designation" value={u!.designation} />
                  <Row label="Last sign-in" value={u!.lastLoginAt ? formatDateTime(u!.lastLoginAt) : "Never"} />
                  <Row label="Member since" value={formatDateTime(u!.createdAt)} />
                </div>
              )}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Access</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <Row label="Role" value={person ? person.roleName : lookup.isPending ? "…" : "No role"} />
              <Row label="Scope" value={person?.scopeLabel} />
              <Row label="Account type" value={u!.userType} />
              {hasPermission("memberships.grant") && !isSelf ? (
                <div className="space-y-1.5 rounded-md border bg-muted/40 p-3">
                  <Label htmlFor="new-role">Change role</Label>
                  <div className="flex gap-2">
                    <Select
                      id="new-role"
                      value={newRoleId}
                      onChange={(e) => setNewRoleId(e.target.value)}
                    >
                      <option value="">{roles.isPending ? "Loading…" : "Select new role"}</option>
                      {(roles.data ?? [])
                        .filter((r) => r.status === "active" && r.code !== person?.roleCode)
                        .map((r) => (
                          <option key={r.id} value={r.id}>
                            {r.name} · {r.scopeType === "platform" ? "Platform" : "Tenant"}
                          </option>
                        ))}
                    </Select>
                    <Button
                      size="sm"
                      disabled={!newRoleId || changeRole.isPending}
                      onClick={() => void onChangeRole()}
                    >
                      Apply
                    </Button>
                  </div>
                  <p className="text-xs text-muted-foreground">
                    Replaces their current role. You'll be asked to verify with a one-time code.
                  </p>
                </div>
              ) : null}
              <div className="flex flex-wrap gap-2 border-t pt-3">
                {hasPermission("users.set_password") && u!.status === "active" ? (
                  <Button size="sm" variant="outline" onClick={() => setDialog("password")}>
                    <KeyRound /> Set password
                  </Button>
                ) : null}
                {hasPermission("users.create") && u!.status === "invited" ? (
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={resendInvite.isPending}
                    onClick={() => void onResendInvite()}
                  >
                    <MailPlus /> Resend invite
                  </Button>
                ) : null}
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      <Dialog
        open={dialog === "activate"}
        onClose={() => setDialog(null)}
        title="Activate user"
        description={`Set a temporary password for ${name}. They must change it on first sign-in.`}
      >
        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="activate-password">Temporary password (min 8 characters)</Label>
            <Input
              id="activate-password"
              type="password"
              autoComplete="new-password"
              value={dialogPassword}
              onChange={(e) => setDialogPassword(e.target.value)}
            />
            <FieldErrors errors={error?.fieldErrors} field="password" />
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              disabled={dialogPassword.length < 8 || setStatus.isPending}
              onClick={() => void onSetStatus("activate", dialogPassword)}
            >
              Activate
            </Button>
          </div>
        </div>
      </Dialog>

      <Dialog
        open={dialog === "suspend"}
        onClose={() => setDialog(null)}
        title="Suspend user?"
        description={`${name} will be signed out and unable to sign in until reactivated.`}
      >
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setDialog(null)}>
            Keep active
          </Button>
          <Button
            variant="destructive"
            disabled={setStatus.isPending}
            onClick={() => void onSetStatus("suspend")}
          >
            Suspend
          </Button>
        </div>
      </Dialog>

      <Dialog
        open={dialog === "password"}
        onClose={() => setDialog(null)}
        title="Set a new password"
        description={`Replace ${name}'s password. You'll be asked to verify with a one-time code.`}
      >
        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="new-password">New password (min 8 characters)</Label>
            <Input
              id="new-password"
              type="password"
              autoComplete="new-password"
              value={dialogPassword}
              onChange={(e) => setDialogPassword(e.target.value)}
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              disabled={dialogPassword.length < 8 || setPassword.isPending}
              onClick={() => void onSetPassword()}
            >
              Update password
            </Button>
          </div>
        </div>
      </Dialog>
      {stepUpDialog}
    </Sheet>
  );
}
