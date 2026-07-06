import { useMutation } from "@tanstack/react-query";
import { apiFetch } from "@/api/http";
import type { SendMessageRequest, SendMessageResult } from "@/api/types";

/**
 * POST /messaging/api/v1/messages requires an Idempotency-Key header (24h
 * dedupe; 422 without it). One UUID per logical send — retries of the same
 * send reuse it, a new send mints a new one.
 */
export function useSendMessage() {
  return useMutation({
    mutationFn: ({
      request,
      idempotencyKey,
    }: {
      request: SendMessageRequest;
      idempotencyKey: string;
    }) =>
      apiFetch<SendMessageResult>("/messaging/api/v1/messages", {
        method: "POST",
        body: request,
        idempotencyKey,
      }),
  });
}
