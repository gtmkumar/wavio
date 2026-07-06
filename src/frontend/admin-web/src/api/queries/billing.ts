import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type {
  CostEstimate,
  QuotaStatusEntry,
  RateCard,
  Reconciliation,
  UpsertRateCardRequest,
} from "@/api/types";

const BASE = "/billing/v1";

export interface RateCardFilters {
  currency?: string;
  status?: string;
}

export interface CostEstimateParams {
  category: string;
  country: string;
  windowOpen: boolean;
  phoneNumberId?: string;
}

export interface ReconciliationPeriod {
  periodStart: string; // yyyy-MM-dd (DateOnly)
  periodEnd: string;
}

export const billingKeys = {
  all: ["billing"] as const,
  rateCards: (filters: RateCardFilters) => ["billing", "rate-cards", filters] as const,
  quotas: ["billing", "quotas"] as const,
  estimate: (params: CostEstimateParams) => ["billing", "estimate", params] as const,
  reconciliation: (period: ReconciliationPeriod) => ["billing", "reconciliation", period] as const,
};

export function useRateCards(filters: RateCardFilters = {}) {
  return useQuery({
    queryKey: billingKeys.rateCards(filters),
    queryFn: ({ signal }) =>
      apiFetch<RateCard[]>(`${BASE}/rate-cards`, {
        query: { currency: filters.currency, status: filters.status },
        signal,
      }),
  });
}

/** POST creates when `id` is null, PUT replaces the card (header + full entry set) otherwise. */
export function useUpsertRateCard() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, request }: { id: string | null; request: UpsertRateCardRequest }) =>
      id
        ? apiFetch<RateCard>(`${BASE}/rate-cards/${id}`, { method: "PUT", body: request })
        : apiFetch<RateCard>(`${BASE}/rate-cards`, { method: "POST", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: billingKeys.all }),
  });
}

export function useQuotaStatus() {
  return useQuery({
    queryKey: billingKeys.quotas,
    queryFn: ({ signal }) => apiFetch<QuotaStatusEntry[]>(`${BASE}/quotas/status`, { signal }),
  });
}

export function useCostEstimate(params: CostEstimateParams | null) {
  return useQuery({
    queryKey: billingKeys.estimate(params ?? { category: "", country: "", windowOpen: false }),
    queryFn: ({ signal }) =>
      apiFetch<CostEstimate>(`${BASE}/costs/estimate`, {
        query: {
          category: params?.category,
          country: params?.country,
          windowOpen: params?.windowOpen,
          phoneNumberId: params?.phoneNumberId,
        },
        signal,
      }),
    enabled: params != null,
  });
}

export function useReconciliation(period: ReconciliationPeriod | null) {
  return useQuery({
    queryKey: billingKeys.reconciliation(period ?? { periodStart: "", periodEnd: "" }),
    queryFn: ({ signal }) =>
      apiFetch<Reconciliation>(`${BASE}/reconciliation`, {
        query: { periodStart: period?.periodStart, periodEnd: period?.periodEnd },
        signal,
      }),
    enabled: period != null,
  });
}
