import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type { ServicesHealth } from "@/api/types";

/** Gateway fan-out health: 200 all-healthy, 207 when any service is down. */
export function useServicesHealth() {
  return useQuery({
    queryKey: ["health", "services"],
    queryFn: ({ signal }) => apiFetch<ServicesHealth>("/health/services", { signal }),
    refetchInterval: 30_000,
  });
}
