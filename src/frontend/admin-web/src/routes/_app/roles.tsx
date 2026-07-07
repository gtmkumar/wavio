import { useMemo, useState, type FormEvent } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { Copy, Pencil, Plus, ShieldCheck, Trash2, Users } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import {
  useAccessRoles,
  useCloneRole,
  useCreateRole,
  useDeleteRole,
  useSetRoleCells,
  useUpdateRole,
} from "@/api/queries/roles";
import type { RoleSummary } from "@/api/types";
import { useAuth } from "@/auth/AuthContext";
import { useStepUpGuard } from "@/auth/StepUpDialog";
import { PageHeader } from "@/components/shared/page-header";
import { ErrorState, FieldErrors, LoadingRows } from "@/components/shared/states";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";

interface RolesSearch {
  role?: string;
}

export const Route = createFileRoute("/_app/roles")({
  validateSearch: (search): RolesSearch => ({
    role: typeof search.role === "string" ? search.role : undefined,
  }),
  component: RolesPage,
});

const ACTION_LABELS: Record<string, string> = {
  view: "View",
  create: "Create",
  edit: "Edit",
  delete: "Delete",
  manage: "Manage",
  approve: "Approve",
  export: "Export",
};

/** Fallback for actions the map doesn't know: "some_action" → "Some action". */
function actionLabel(action: string): string {
  return (
    ACTION_LABELS[action] ??
    (action.charAt(0).toUpperCase() + action.slice(1).replace(/_/g, " "))
  );
}

function normalizeCode(raw: string): string {
  return raw.toLowerCase().replace(/[^a-z0-9_]+/g, "_").replace(/_{2,}/g, "_");
}

type FormMode = "create" | "edit" | "clone";

function RolesPage() {
  const { role: roleParam } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const { hasPermission } = useAuth();
  const matrix = useAccessRoles();
  const { guard, stepUpDialog } = useStepUpGuard();

  // Cell edits are collected locally and saved as one atomic batch.
  const [pending, setPending] = useState<Record<string, boolean>>({});
  const [formMode, setFormMode] = useState<FormMode | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const allRoles = useMemo(
    () => (matrix.data?.groups ?? []).flatMap((g) => g.roles),
    [matrix.data],
  );
  const selected: RoleSummary | undefined =
    allRoles.find((r) => r.id === roleParam) ?? allRoles[0];

  const setCells = useSetRoleCells(selected?.id ?? "");
  const deleteRole = useDeleteRole(selected?.id ?? "");

  const selectRole = (id: string) => {
    setPending({});
    void navigate({ search: { role: id }, replace: true });
  };

  const canEditCells =
    hasPermission("permissions.assign") && selected != null && selected.code !== "platform_admin";
  const canManage = hasPermission("roles.manage");

  const isOn = (cellKey: string) =>
    pending[cellKey] ?? (selected?.onCells.includes(cellKey) ?? false);

  const toggleCell = (cellKey: string) => {
    if (!selected) return;
    const baseline = selected.onCells.includes(cellKey);
    setPending((prev) => {
      const next = { ...prev };
      const value = !(prev[cellKey] ?? baseline);
      if (value === baseline) delete next[cellKey];
      else next[cellKey] = value;
      return next;
    });
  };

  const dirtyCount = Object.keys(pending).length;

  async function onSaveCells() {
    if (!selected || dirtyCount === 0) return;
    try {
      // permissions.assign is a §8 step-up action — the guard prompts for OTP and retries.
      await guard(() =>
        setCells.mutateAsync(
          Object.entries(pending).map(([cellKey, enabled]) => ({ cellKey, enabled })),
        ),
      );
      setPending({});
      toast.success(`Permissions updated for ${selected.name}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Saving permissions failed.");
    }
  }

  async function onDelete() {
    if (!selected) return;
    setConfirmDelete(false);
    try {
      await guard(() => deleteRole.mutateAsync());
      toast.success(`Role "${selected.name}" deleted`);
      void navigate({ search: {}, replace: true });
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Delete failed.");
    }
  }

  return (
    <>
      <PageHeader
        title="Roles & permissions"
        description="What each role is allowed to do. Tick a box to grant that ability."
        actions={
          canManage ? (
            <Button onClick={() => setFormMode("create")}>
              <Plus /> New role
            </Button>
          ) : undefined
        }
      />
      {matrix.isPending ? (
        <Card>
          <LoadingRows count={6} />
        </Card>
      ) : matrix.isError ? (
        <Card>
          <ErrorState error={matrix.error} />
        </Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[16rem_minmax(0,1fr)]">
          <div className="space-y-4">
            {(matrix.data.groups ?? [])
              .filter((g) => g.roles.length > 0)
              .map((g) => (
                <div key={g.tier}>
                  <p className="mb-1.5 px-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {g.tierLabel} roles
                  </p>
                  <div className="space-y-1">
                    {g.roles.map((r) => (
                      <button
                        key={r.id}
                        type="button"
                        onClick={() => selectRole(r.id)}
                        className={cn(
                          "flex w-full items-center justify-between gap-2 rounded-md border px-3 py-2 text-left text-sm transition-colors hover:bg-accent",
                          selected?.id === r.id && "border-primary bg-accent",
                        )}
                      >
                        <span className="min-w-0">
                          <span className="block truncate font-medium">{r.name}</span>
                          <span className="flex items-center gap-1 text-xs text-muted-foreground">
                            <Users className="size-3" /> {r.memberCount} member
                            {r.memberCount === 1 ? "" : "s"}
                          </span>
                        </span>
                        {r.isSystem ? <Badge variant="secondary">System</Badge> : null}
                      </button>
                    ))}
                  </div>
                </div>
              ))}
          </div>
          {selected ? (
            <Card>
              <CardHeader className="flex-row items-start justify-between space-y-0">
                <div>
                  <CardTitle className="text-base">{selected.name}</CardTitle>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {selected.description ||
                      (selected.isSystem ? "Built-in role." : "Custom role.")}
                  </p>
                </div>
                {canManage ? (
                  <div className="flex shrink-0 gap-2">
                    <Button size="sm" variant="outline" onClick={() => setFormMode("clone")}>
                      <Copy /> Clone
                    </Button>
                    {!selected.isSystem ? (
                      <>
                        <Button size="sm" variant="outline" onClick={() => setFormMode("edit")}>
                          <Pencil /> Edit
                        </Button>
                        <Button
                          size="sm"
                          variant="destructive"
                          onClick={() => setConfirmDelete(true)}
                        >
                          <Trash2 /> Delete
                        </Button>
                      </>
                    ) : null}
                  </div>
                ) : null}
              </CardHeader>
              <CardContent>
                {selected.code === "platform_admin" ? (
                  <p className="mb-4 flex items-center gap-2 rounded-md bg-muted/60 p-3 text-sm text-muted-foreground">
                    <ShieldCheck className="size-4 shrink-0 text-primary" />
                    Platform admins always have full access — this role's boxes can't be changed.
                  </p>
                ) : null}
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b">
                        <th className="py-2 pr-4 text-left font-medium text-muted-foreground">
                          Area
                        </th>
                        {matrix.data.actions.map((a) => (
                          <th key={a} className="px-2 py-2 text-center font-medium text-muted-foreground">
                            {actionLabel(a)}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {matrix.data.modules.map((m) => (
                        <tr key={m.key} className="border-b last:border-0">
                          <td className="py-2.5 pr-4 font-medium">{m.label}</td>
                          {matrix.data.actions.map((a) => {
                            const cellKey = `${m.key}:${a}`;
                            const exists = cellKey in matrix.data.cells;
                            if (!exists) {
                              return (
                                <td key={a} className="px-2 py-2.5 text-center text-muted-foreground/40">
                                  —
                                </td>
                              );
                            }
                            return (
                              <td key={a} className="px-2 py-2.5 text-center">
                                <input
                                  type="checkbox"
                                  className="size-4 cursor-pointer accent-primary disabled:cursor-not-allowed"
                                  checked={isOn(cellKey)}
                                  disabled={!canEditCells}
                                  onChange={() => toggleCell(cellKey)}
                                  aria-label={`${selected.name}: ${m.label} — ${actionLabel(a)}`}
                                  title={(matrix.data.cells[cellKey] ?? []).join(", ")}
                                />
                              </td>
                            );
                          })}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                {dirtyCount > 0 ? (
                  <div className="mt-4 flex items-center justify-between rounded-md border bg-muted/40 px-4 py-3">
                    <span className="text-sm">
                      {dirtyCount} unsaved change{dirtyCount === 1 ? "" : "s"}
                    </span>
                    <div className="flex gap-2">
                      <Button variant="ghost" size="sm" onClick={() => setPending({})}>
                        Discard
                      </Button>
                      <Button size="sm" disabled={setCells.isPending} onClick={() => void onSaveCells()}>
                        {setCells.isPending ? "Saving…" : "Save changes"}
                      </Button>
                    </div>
                  </div>
                ) : null}
              </CardContent>
            </Card>
          ) : (
            <Card>
              <CardContent className="py-12 text-center text-sm text-muted-foreground">
                No roles yet — create one to get started.
              </CardContent>
            </Card>
          )}
        </div>
      )}

      {formMode && (formMode === "create" || selected) ? (
        <RoleFormSheet
          mode={formMode}
          source={formMode === "create" ? null : selected!}
          onClose={() => setFormMode(null)}
          onSaved={(id) => {
            setFormMode(null);
            if (id) selectRole(id);
          }}
          guard={guard}
        />
      ) : null}

      <Dialog
        open={confirmDelete}
        onClose={() => setConfirmDelete(false)}
        title="Delete role?"
        description={`"${selected?.name ?? "This role"}" will be removed. People still holding it lose the access it granted.`}
      >
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setConfirmDelete(false)}>
            Keep role
          </Button>
          <Button variant="destructive" onClick={() => void onDelete()}>
            Delete
          </Button>
        </div>
      </Dialog>
      {stepUpDialog}
    </>
  );
}

/** Create / edit / clone share one small form — all three are step-up (roles.manage). */
function RoleFormSheet({
  mode,
  source,
  onClose,
  onSaved,
  guard,
}: {
  mode: FormMode;
  source: RoleSummary | null;
  onClose: () => void;
  onSaved: (newRoleId: string | null) => void;
  guard: <T>(fn: () => Promise<T>) => Promise<T>;
}) {
  const { isPlatformAdmin, tenantOverride } = useAuth();
  const create = useCreateRole();
  const update = useUpdateRole(source?.id ?? "");
  const clone = useCloneRole(source?.id ?? "");

  const [code, setCode] = useState(mode === "clone" ? `${source?.code ?? ""}_copy` : "");
  const [name, setName] = useState(
    mode === "edit" ? (source?.name ?? "") : mode === "clone" ? `${source?.name ?? ""} (copy)` : "",
  );
  const [description, setDescription] = useState(mode === "edit" ? (source?.description ?? "") : "");
  const [error, setError] = useState<ApiError | null>(null);

  const pending = create.isPending || update.isPending || clone.isPending;
  // Create/clone bind the new role to the acting tenant server-side.
  const needsTenant = mode !== "edit" && isPlatformAdmin && !tenantOverride;

  const titles: Record<FormMode, string> = {
    create: "New role",
    edit: `Edit ${source?.name ?? "role"}`,
    clone: `Clone ${source?.name ?? "role"}`,
  };

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      let newId: string | null = null;
      if (mode === "create") {
        const created = await guard(() =>
          create.mutateAsync({
            code,
            name: name.trim(),
            description: description.trim() || null,
            scopeType: "tenant",
          }),
        );
        newId = created.id;
        toast.success(`Role "${created.name}" created — now tick what it may do`);
      } else if (mode === "edit") {
        await guard(() =>
          update.mutateAsync({ name: name.trim(), description: description.trim() || null }),
        );
        toast.success("Role updated");
      } else {
        const cloned = await guard(() =>
          clone.mutateAsync({ code, name: name.trim(), description: description.trim() || null }),
        );
        newId = cloned.id;
        toast.success(`Role "${cloned.name}" created with the same permissions`);
      }
      onSaved(newId);
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <Sheet
      open
      onClose={onClose}
      size="md"
      title={titles[mode]}
      description={
        mode === "clone"
          ? "Starts with the same permissions as the original."
          : mode === "create"
            ? "A custom role for this tenant's staff."
            : undefined
      }
    >
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {needsTenant ? (
          <p className="rounded-md bg-amber-500/10 p-3 text-sm text-amber-700 dark:text-amber-400">
            Custom roles belong to a tenant — pick one with "Acting as platform" in the top bar
            first.
          </p>
        ) : null}
        {mode !== "edit" ? (
          <div className="space-y-1.5">
            <Label htmlFor="role-code">Code</Label>
            <Input
              id="role-code"
              value={code}
              onChange={(e) => setCode(normalizeCode(e.target.value))}
              placeholder="support_agent"
              className="font-mono"
            />
            <p className="text-xs text-muted-foreground">
              Lowercase identifier, letters/numbers/underscores. Can't be changed later.
            </p>
            <FieldErrors errors={error?.fieldErrors} field="code" />
          </div>
        ) : null}
        <div className="space-y-1.5">
          <Label htmlFor="role-name">Name</Label>
          <Input
            id="role-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Support agent"
          />
          <FieldErrors errors={error?.fieldErrors} field="name" />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="role-description">Description (optional)</Label>
          <Textarea
            id="role-description"
            rows={3}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Answers customer chats; can't touch billing."
          />
          <FieldErrors errors={error?.fieldErrors} field="description" />
        </div>
        {error && !error.fieldErrors ? (
          <p className="text-sm text-destructive">{error.message}</p>
        ) : null}
        <div className="flex justify-end gap-2 border-t pt-4">
          <Button type="button" variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            disabled={pending || !name.trim() || (mode !== "edit" && !code) || needsTenant}
          >
            {pending ? "Saving…" : titles[mode]}
          </Button>
        </div>
      </form>
    </Sheet>
  );
}
