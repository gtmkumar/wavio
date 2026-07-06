import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type { PhoneNumberSummary } from "@/api/types";

/** Sender phone numbers for pickers (campaign create, send console, quality). */
export function usePhoneNumbers() {
  return useQuery({
    queryKey: ["waba", "phone-numbers"],
    queryFn: ({ signal }) =>
      apiFetch<PhoneNumberSummary[]>("/admin/v1/waba/phone-numbers", { signal }),
    staleTime: 5 * 60 * 1000,
  });
}
