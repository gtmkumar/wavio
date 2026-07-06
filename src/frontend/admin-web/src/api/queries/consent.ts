import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type {
  ConsentState,
  CreateErasureRequestRequest,
  ErasureRequest,
  OptInEvent,
  OptOutEvent,
  RecordManualOptOutRequest,
  RecordOptInRequest,
  RetentionPolicy,
  UpsertRetentionPolicyRequest,
} from "@/api/types";

const BASE = "/admin/v1/consent";

export const consentKeys = {
  all: ["consent"] as const,
  state: (waId: string) => ["consent", "state", waId] as const,
  erasureRequest: (id: string) => ["consent", "erasure-request", id] as const,
  retentionPolicies: ["consent", "retention-policies"] as const,
};

export function useConsentState(waId: string | null) {
  return useQuery({
    queryKey: consentKeys.state(waId ?? ""),
    queryFn: ({ signal }) => apiFetch<ConsentState>(`${BASE}/${waId}`, { signal }),
    enabled: waId != null && waId.length > 0,
  });
}

export function useRecordOptIn() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: RecordOptInRequest) =>
      apiFetch<OptInEvent>(`${BASE}/opt-in`, { method: "POST", body: request }),
    onSuccess: (event) =>
      void queryClient.invalidateQueries({ queryKey: consentKeys.state(event.waId) }),
  });
}

export function useRecordOptOut() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: RecordManualOptOutRequest) =>
      apiFetch<OptOutEvent>(`${BASE}/opt-out`, { method: "POST", body: request }),
    onSuccess: (event) =>
      void queryClient.invalidateQueries({ queryKey: consentKeys.state(event.waId) }),
  });
}

export function useErasureRequest(id: string | null) {
  return useQuery({
    queryKey: consentKeys.erasureRequest(id ?? ""),
    queryFn: ({ signal }) => apiFetch<ErasureRequest>(`${BASE}/requests/${id}`, { signal }),
    enabled: id != null && id.length > 0,
  });
}

export function useCreateErasureRequest() {
  return useMutation({
    mutationFn: (request: CreateErasureRequestRequest) =>
      apiFetch<ErasureRequest>(`${BASE}/requests`, { method: "POST", body: request }),
  });
}

export function useRetentionPolicies() {
  return useQuery({
    queryKey: consentKeys.retentionPolicies,
    queryFn: ({ signal }) => apiFetch<RetentionPolicy[]>(`${BASE}/retention-policies`, { signal }),
  });
}

export function useUpsertRetentionPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpsertRetentionPolicyRequest) =>
      apiFetch<RetentionPolicy>(`${BASE}/retention-policies`, { method: "PUT", body: request }),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: consentKeys.retentionPolicies }),
  });
}
