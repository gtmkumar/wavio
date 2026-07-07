import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type {
  BusinessProfile,
  EmbeddedSignupRequest,
  OnboardingPhone,
  OnboardingStatus,
  UpdateBusinessProfileRequest,
} from "@/api/types";

const BASE = "/admin/v1/onboarding";

export const onboardingKeys = {
  status: ["onboarding", "status"] as const,
  profile: (phoneId: string) => ["onboarding", "profile", phoneId] as const,
};

/** DB-only snapshot — cheap, safe to poll; POST /refresh is the Graph-backed pull. */
export function useOnboardingStatus() {
  return useQuery({
    queryKey: onboardingKeys.status,
    queryFn: ({ signal }) => apiFetch<OnboardingStatus>(`${BASE}/status`, { signal }),
  });
}

export function useRefreshOnboarding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiFetch<OnboardingStatus>(`${BASE}/refresh`, { method: "POST" }),
    onSuccess: (status) => queryClient.setQueryData(onboardingKeys.status, status),
  });
}

export function useEmbeddedSignup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: EmbeddedSignupRequest) =>
      apiFetch<OnboardingStatus>(`${BASE}/embedded-signup`, { method: "POST", body: request }),
    onSuccess: (status) => {
      queryClient.setQueryData(onboardingKeys.status, status);
      // The signup may have added sender numbers the pickers cache.
      void queryClient.invalidateQueries({ queryKey: ["waba", "phone-numbers"] });
    },
  });
}

export function useRequestVerificationCode() {
  return useMutation({
    mutationFn: (args: { phoneId: string; codeMethod: string }) =>
      apiFetch(`${BASE}/phone-numbers/${args.phoneId}/request-code`, {
        method: "POST",
        body: { codeMethod: args.codeMethod, language: "en_US" },
      }),
  });
}

export function useVerifyCode() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: { phoneId: string; code: string }) =>
      apiFetch<OnboardingPhone>(`${BASE}/phone-numbers/${args.phoneId}/verify-code`, {
        method: "POST",
        body: { code: args.code },
      }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: onboardingKeys.status }),
  });
}

export function useRegisterPhone() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: { phoneId: string; pin: string }) =>
      apiFetch<OnboardingPhone>(`${BASE}/phone-numbers/${args.phoneId}/register`, {
        method: "POST",
        body: { pin: args.pin },
      }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: onboardingKeys.status }),
  });
}

export function useBusinessProfile(phoneId: string | null) {
  return useQuery({
    queryKey: onboardingKeys.profile(phoneId ?? ""),
    queryFn: ({ signal }) =>
      apiFetch<BusinessProfile>(`${BASE}/phone-numbers/${phoneId}/profile`, { signal }),
    enabled: phoneId != null,
  });
}

export function useUpdateBusinessProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: { phoneId: string; request: UpdateBusinessProfileRequest }) =>
      apiFetch<BusinessProfile>(`${BASE}/phone-numbers/${args.phoneId}/profile`, {
        method: "PUT",
        body: args.request,
      }),
    onSuccess: (profile, args) => {
      queryClient.setQueryData(onboardingKeys.profile(args.phoneId), profile);
      void queryClient.invalidateQueries({ queryKey: onboardingKeys.status });
    },
  });
}
