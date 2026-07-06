import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type { HealthReport, TierAdvisor, WindowState } from "@/api/types";

export function useQualityHealth(phoneNumberId?: string) {
  return useQuery({
    queryKey: ["quality", "health", phoneNumberId ?? "all"],
    queryFn: ({ signal }) =>
      apiFetch<HealthReport>("/intel/api/v1/quality/health", {
        query: { phoneNumberId },
        signal,
      }),
  });
}

export function useTierAdvisor(phoneNumberId: string | null) {
  return useQuery({
    queryKey: ["quality", "tier-advisor", phoneNumberId],
    queryFn: ({ signal }) =>
      apiFetch<TierAdvisor>(`/intel/api/v1/quality/tier-advisor/${phoneNumberId}`, { signal }),
    enabled: phoneNumberId != null,
  });
}

export function useWindowState(waId: string | null, phoneNumberId: string | null) {
  return useQuery({
    queryKey: ["windows", waId, phoneNumberId],
    queryFn: ({ signal }) =>
      apiFetch<WindowState>(`/intel/api/v1/windows/${waId}`, {
        query: { phoneNumberId },
        signal,
      }),
    enabled: waId != null && phoneNumberId != null,
  });
}
