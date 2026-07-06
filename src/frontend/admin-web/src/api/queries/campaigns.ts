import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type { Campaign, CampaignListItem, CreateCampaignRequest } from "@/api/types";

const BASE = "/messaging/api/v1/campaigns";

export const campaignKeys = {
  all: ["campaigns"] as const,
  list: (status: string | undefined) => ["campaigns", "list", status ?? "all"] as const,
  detail: (id: string) => ["campaigns", "detail", id] as const,
};

export function useCampaigns(status?: string) {
  return useQuery({
    queryKey: campaignKeys.list(status),
    queryFn: ({ signal }) => apiFetch<CampaignListItem[]>(BASE, { query: { status }, signal }),
  });
}

type RefetchInterval =
  | number
  | false
  | ((query: { state: { data?: Campaign } }) => number | false);

export function useCampaign(id: string, options?: { refetchInterval?: RefetchInterval }) {
  return useQuery({
    queryKey: campaignKeys.detail(id),
    queryFn: ({ signal }) => apiFetch<Campaign>(`${BASE}/${id}`, { signal }),
    refetchInterval: options?.refetchInterval,
  });
}

export function useCreateCampaign() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateCampaignRequest) =>
      apiFetch<Campaign>(BASE, { method: "POST", body: request }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: campaignKeys.all }),
  });
}

export function useLaunchCampaign() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiFetch<Campaign>(`${BASE}/${id}/launch`, { method: "POST" }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: campaignKeys.all }),
  });
}

export function useCancelCampaign() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiFetch<Campaign>(`${BASE}/${id}/cancel`, { method: "POST" }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: campaignKeys.all }),
  });
}
