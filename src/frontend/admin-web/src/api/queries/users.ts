import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type {
  AccessPeoplePage,
  ChangeRoleRequest,
  InviteUserRequest,
  SetPersonStatusRequest,
  SetPersonStatusResult,
  UpdateUserRequest,
  UserDetail,
} from "@/api/types";

const USERS = "/identity/api/v1/admin/users";
const ACCESS = "/identity/api/v1/admin/access-control";

export interface PeopleFilters {
  search?: string;
  page?: number;
  pageSize?: number;
}

export const userKeys = {
  all: ["users"] as const,
  people: (filters: PeopleFilters) => ["users", "people", filters] as const,
  detail: (id: string) => ["users", "detail", id] as const,
};

/** Console-optimized directory: counts + role/scope per person, tenant-isolated server-side. */
export function usePeople(filters: PeopleFilters = {}, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: userKeys.people(filters),
    queryFn: ({ signal }) =>
      apiFetch<AccessPeoplePage>(`${ACCESS}/people`, {
        query: {
          search: filters.search,
          page: filters.page ?? 1,
          pageSize: filters.pageSize ?? 20,
        },
        signal,
      }),
    enabled: options.enabled ?? true,
  });
}

export function useUser(id: string) {
  return useQuery({
    queryKey: userKeys.detail(id),
    queryFn: ({ signal }) => apiFetch<UserDetail>(`${USERS}/${id}`, { signal }),
  });
}

export function useInviteUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: InviteUserRequest) =>
      apiFetch<UserDetail>(`${ACCESS}/invite`, { method: "POST", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: userKeys.all }),
  });
}

export function useUpdateUser(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateUserRequest) =>
      apiFetch<UserDetail>(`${USERS}/${id}`, { method: "PUT", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: userKeys.all }),
  });
}

/** activate (sets a temp password) / suspend / reactivate. */
export function useSetPersonStatus(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SetPersonStatusRequest) =>
      apiFetch<SetPersonStatusResult>(`${ACCESS}/people/${id}/status`, {
        method: "POST",
        body: request,
      }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: userKeys.all }),
  });
}

/** Replace the user's primary role — §8 step-up (memberships.grant, critical). */
export function useChangeRole(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: ChangeRoleRequest) =>
      apiFetch(`${USERS}/${id}/change-role`, { method: "POST", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: userKeys.all }),
  });
}

/** Admin-set password — §8 step-up (users.set_password, high). */
export function useSetUserPassword(id: string) {
  return useMutation({
    mutationFn: (newPassword: string) =>
      apiFetch(`${USERS}/${id}/set-password`, { method: "POST", body: { newPassword } }),
  });
}

export function useResendInvite(id: string) {
  return useMutation({
    mutationFn: () => apiFetch(`${USERS}/${id}/resend-invite`, { method: "POST" }),
  });
}
