import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type {
  CreateTemplateRequest,
  CreateTemplateResult,
  PaginatedList,
  Template,
  TemplateStatus,
  UpdateTemplateRequest,
} from "@/api/types";

const BASE = "/admin/v1/templates";

export interface TemplateListFilters {
  status?: string;
  category?: string;
  businessAccountId?: string;
  page?: number;
  pageSize?: number;
}

export const templateKeys = {
  all: ["templates"] as const,
  list: (filters: TemplateListFilters) => ["templates", "list", filters] as const,
  detail: (id: string) => ["templates", "detail", id] as const,
  status: (id: string) => ["templates", "status", id] as const,
};

export function useTemplates(filters: TemplateListFilters = {}) {
  return useQuery({
    queryKey: templateKeys.list(filters),
    queryFn: ({ signal }) =>
      apiFetch<PaginatedList<Template>>(BASE, {
        query: {
          status: filters.status,
          category: filters.category,
          businessAccountId: filters.businessAccountId,
          page: filters.page ?? 1,
          pageSize: filters.pageSize ?? 20,
        },
        signal,
      }),
  });
}

export function useTemplate(id: string) {
  return useQuery({
    queryKey: templateKeys.detail(id),
    queryFn: ({ signal }) => apiFetch<Template>(`${BASE}/${id}`, { signal }),
  });
}

export function useTemplateStatus(id: string) {
  return useQuery({
    queryKey: templateKeys.status(id),
    queryFn: ({ signal }) => apiFetch<TemplateStatus>(`${BASE}/${id}/status`, { signal }),
  });
}

export function useCreateTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateTemplateRequest) =>
      apiFetch<CreateTemplateResult>(BASE, { method: "POST", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: templateKeys.all }),
  });
}

export function useUpdateTemplate(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateTemplateRequest) =>
      apiFetch<Template>(`${BASE}/${id}`, { method: "PUT", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: templateKeys.all }),
  });
}

export function useSubmitTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiFetch<Template>(`${BASE}/${id}/submit`, { method: "POST" }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: templateKeys.all }),
  });
}

export function useDeleteTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiFetch<void>(`${BASE}/${id}`, { method: "DELETE" }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: templateKeys.all }),
  });
}
