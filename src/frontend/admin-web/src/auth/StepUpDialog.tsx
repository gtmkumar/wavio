import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { ShieldCheck } from "lucide-react";
import { ApiError, apiFetch, setAccessToken } from "@/api/http";
import { useAuth } from "@/auth/AuthContext";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface StepUpVerifiedResponse {
  accessToken: string;
  expiresInSeconds: number | string;
}

/**
 * Spec §8 step-up: high/critical actions (campaign launch, template delete, …)
 * demand a fresh OTP re-verification even for platform admins. The backend
 * answers 403 step_up_required; this dialog sends the OTP (purpose
 * sensitive_action), verifies it, and adopts the UPGRADED access token so the
 * caller can retry. In Development the OTP is logged by the identity service
 * (DevLogOtpSender), not delivered externally.
 */
function StepUpDialog({
  onVerified,
  onCancel,
}: {
  onVerified: () => void;
  onCancel: () => void;
}) {
  const { user } = useAuth();
  const [code, setCode] = useState("");
  const [sendState, setSendState] = useState<"sending" | "sent" | "failed">("sending");
  const [error, setError] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);
  const sentOnce = useRef(false);

  const sendOtp = useCallback(async () => {
    if (!user?.email) {
      setSendState("failed");
      setError("Your account has no email to receive a verification code.");
      return;
    }
    setSendState("sending");
    setError(null);
    try {
      await apiFetch("/identity/api/v1/auth/otp/send", {
        method: "POST",
        body: { identifier: user.email, identifierType: "email", purpose: "sensitive_action" },
      });
      setSendState("sent");
    } catch (err) {
      setSendState("failed");
      setError(err instanceof ApiError ? err.message : "Could not send the verification code.");
    }
  }, [user?.email]);

  useEffect(() => {
    if (sentOnce.current) return;
    sentOnce.current = true;
    void sendOtp();
  }, [sendOtp]);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setVerifying(true);
    setError(null);
    try {
      const result = await apiFetch<StepUpVerifiedResponse>("/identity/api/v1/auth/step-up/verify", {
        method: "POST",
        body: { identifierType: "email", code: code.trim() },
      });
      setAccessToken(result.accessToken);
      onVerified();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Verification failed.");
    } finally {
      setVerifying(false);
    }
  }

  return (
    <Dialog
      open
      onClose={onCancel}
      title="Verify it's you"
      description="This is a sensitive action. Enter the one-time code sent to your email to continue."
    >
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <ShieldCheck className="size-4 text-primary" />
          {sendState === "sending"
            ? "Sending code…"
            : sendState === "sent"
              ? `Code sent to ${user?.email}`
              : "Code could not be sent."}
          <Button variant="link" size="sm" className="h-auto p-0" onClick={() => void sendOtp()}>
            Resend
          </Button>
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="stepup-code">Verification code</Label>
          <Input
            id="stepup-code"
            autoFocus
            inputMode="numeric"
            autoComplete="one-time-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
          />
        </div>
        {error ? <p className="text-sm text-destructive">{error}</p> : null}
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="submit" disabled={verifying || !code.trim()}>
            {verifying ? "Verifying…" : "Verify & continue"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

/**
 * Wrap a sensitive mutation: on 403 step_up_required, prompt for OTP and
 * retry once with the upgraded token.
 *
 *   const { guard, stepUpDialog } = useStepUpGuard();
 *   await guard(() => launch.mutateAsync(id));
 *   …
 *   {stepUpDialog}
 */
export function useStepUpGuard(): {
  guard: <T>(fn: () => Promise<T>) => Promise<T>;
  stepUpDialog: ReactNode;
} {
  const [prompt, setPrompt] = useState<{
    resolve: () => void;
    reject: (err: unknown) => void;
  } | null>(null);

  const guard = useCallback(async <T,>(fn: () => Promise<T>): Promise<T> => {
    try {
      return await fn();
    } catch (err) {
      if (!(err instanceof ApiError) || !err.isStepUpRequired) throw err;
      await new Promise<void>((resolve, reject) => setPrompt({ resolve, reject }));
      return await fn();
    }
  }, []);

  const stepUpDialog = prompt ? (
    <StepUpDialog
      onVerified={() => {
        prompt.resolve();
        setPrompt(null);
      }}
      onCancel={() => {
        prompt.reject(new ApiError(403, "Verification cancelled."));
        setPrompt(null);
      }}
    />
  ) : null;

  return { guard, stepUpDialog };
}
