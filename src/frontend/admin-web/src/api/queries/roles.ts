import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type {
  AccessRoles,
  CloneRoleRequest,
  CreateRoleRequest,
  Role,
  RoleCellChange,
  RoleSummary,
  UpdateRoleRequest,
} from "@/api/types";

const ROLES = "/identity/api/v1/admin/roles";
const ACCESS_ROLES = "/identity/api/v1/admin/access-control/roles";

export const roleKeys = {
  all: ["roles"] as const,
  list: ["roles", "list"] as const,
  matrix: ["roles", "matrix"] as const,
};

/** Flat role list for pickers (invite, change-role). */
export function useRoles() {
  return useQuery({
    queryKey: roleKeys.list,
    queryFn: ({ signal }) => apiFetch<Role[]>(ROLES, { query: { pageSize: 50 }, signal }),
  });
}

/** Modules × actions permission matrix with per-role granted cells. */
export function useAccessRoles() {
  return useQuery({
    queryKey: roleKeys.matrix,
    queryFn: ({ signal }) => apiFetch<AccessRoles>(ACCESS_ROLES, { signal }),
  });
}

/** Batch-apply matrix cell toggles — §8 step-up (permissions.assign, critical). */
export function useSetRoleCells(roleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (changes: RoleCellChange[]) =>
      apiFetch(`${ACCESS_ROLES}/${roleId}/cells`, { method: "POST", body: { changes } }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  });
}

// Role CRUD — all §8 step-up (roles.manage, critical). Create/clone are
// tenant-scoped server-side: a platform admin must act on a tenant first.
export function useCreateRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateRoleRequest) =>
      apiFetch<RoleSummary>(ACCESS_ROLES, { method: "POST", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  });
}

export function useUpdateRole(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateRoleRequest) =>
      apiFetch(`${ACCESS_ROLES}/${id}`, { method: "PUT", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  });
}

export function useCloneRole(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CloneRoleRequest) =>
      apiFetch<RoleSummary>(`${ACCESS_ROLES}/${id}/clone`, { method: "POST", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  });
}

export function useDeleteRole(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiFetch(`${ACCESS_ROLES}/${id}`, { method: "DELETE" }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  });
}
