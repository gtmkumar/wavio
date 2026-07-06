import { useState, type FormEvent } from "react";
import { createFileRoute } from "@tanstack/react-router";
import { ApiError } from "@/api/http";
import { useSendMessage } from "@/api/queries/messages";
import { usePhoneNumbers } from "@/api/queries/waba";
import type { SendMessageResult } from "@/api/types";
import { PageHeader } from "@/components/shared/page-header";
import { FieldErrors } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";

export const Route = createFileRoute("/_app/messages")({
  component: SendMessagePage,
});

const PAYLOAD_STARTERS: Record<string, string> = {
  text: JSON.stringify({ body: "Hello from Wavio 👋" }, null, 2),
  template: JSON.stringify(
    { name: "template_name", language: { code: "en" }, components: [] },
    null,
    2,
  ),
};

function SendMessagePage() {
  const phoneNumbers = usePhoneNumbers();
  const send = useSendMessage();

  const [phoneNumberId, setPhoneNumberId] = useState("");
  const [toWaId, setToWaId] = useState("");
  const [messageType, setMessageType] = useState("text");
  const [payload, setPayload] = useState(PAYLOAD_STARTERS.text);
  // One key per logical send; regenerated after each outcome so a retry of a
  // failed attempt reuses the key while a fresh send never collides.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());
  const [result, setResult] = useState<SendMessageResult | null>(null);
  const [error, setError] = useState<ApiError | null>(null);

  function onTypeChange(type: string) {
    setMessageType(type);
    if (PAYLOAD_STARTERS[type]) setPayload(PAYLOAD_STARTERS[type]);
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setResult(null);
    let parsed: unknown;
    try {
      parsed = JSON.parse(payload);
    } catch {
      setError(new ApiError(0, "Payload must be valid JSON."));
      return;
    }
    try {
      const res = await send.mutateAsync({
        request: { phoneNumberId, toWaId: toWaId.trim(), messageType, payload: parsed },
        idempotencyKey,
      });
      setResult(res);
      setIdempotencyKey(crypto.randomUUID());
    } catch (err) {
      const apiErr = err instanceof ApiError ? err : new ApiError(0, "Could not reach the server.");
      setError(apiErr);
      // A rejected send is a settled outcome — mint a fresh key for the next one.
      if (apiErr.status === 422) setIdempotencyKey(crypto.randomUUID());
    }
  }

  return (
    <>
      <PageHeader
        title="Send message"
        description="Send a single message through the gateway — window rules, consent, and quotas apply exactly as in production."
      />
      <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
        <form onSubmit={onSubmit} noValidate>
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Message</CardTitle>
              <CardDescription>
                Free-form text needs an open 24h window; template messages need an approved template.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="from">Send from</Label>
                  <Select
                    id="from"
                    value={phoneNumberId}
                    onChange={(e) => setPhoneNumberId(e.target.value)}
                  >
                    <option value="">
                      {phoneNumbers.isPending ? "Loading numbers…" : "Select a phone number"}
                    </option>
                    {(phoneNumbers.data ?? []).map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.displayPhoneNumber} ({p.status})
                      </option>
                    ))}
                  </Select>
                  <FieldErrors errors={error?.fieldErrors} field="phoneNumberId" />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="to">To (WhatsApp ID)</Label>
                  <Input
                    id="to"
                    value={toWaId}
                    onChange={(e) => setToWaId(e.target.value)}
                    placeholder="919876543210"
                    aria-invalid={!!error?.fieldErrors?.toWaId}
                  />
                  <FieldErrors errors={error?.fieldErrors} field="toWaId" />
                </div>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="type">Message type</Label>
                <Select id="type" value={messageType} onChange={(e) => onTypeChange(e.target.value)}>
                  <option value="text">text</option>
                  <option value="template">template</option>
                  <option value="image">image</option>
                  <option value="document">document</option>
                </Select>
                <FieldErrors errors={error?.fieldErrors} field="messageType" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="payload">Payload (Meta message JSON)</Label>
                <Textarea
                  id="payload"
                  value={payload}
                  onChange={(e) => setPayload(e.target.value)}
                  rows={8}
                  className="font-mono text-xs"
                  aria-invalid={!!error?.fieldErrors?.payload}
                />
                <FieldErrors errors={error?.fieldErrors} field="payload" />
              </div>
              {error && !error.fieldErrors ? (
                <p className="text-sm text-destructive">{error.message}</p>
              ) : null}
              <div className="flex items-center justify-between gap-4">
                <p className="truncate text-xs text-muted-foreground" title={idempotencyKey}>
                  Idempotency-Key: {idempotencyKey}
                </p>
                <Button type="submit" disabled={send.isPending || !phoneNumberId || !toWaId.trim()}>
                  {send.isPending ? "Sending…" : "Send"}
                </Button>
              </div>
            </CardContent>
          </Card>
        </form>

        <Card className="h-fit">
          <CardHeader>
            <CardTitle className="text-base">Result</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            {result ? (
              <>
                <div className="flex items-center justify-between">
                  <span className="text-muted-foreground">Status</span>
                  <StatusBadge status={result.status} />
                </div>
                <div className="flex items-center justify-between gap-3">
                  <span className="text-muted-foreground">Message ID</span>
                  <span className="truncate font-mono text-xs" title={result.id}>{result.id}</span>
                </div>
                {result.wamid ? (
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-muted-foreground">WAMID</span>
                    <span className="truncate font-mono text-xs" title={result.wamid}>{result.wamid}</span>
                  </div>
                ) : null}
                {result.billableEstimate != null ? (
                  <div className="flex items-center justify-between">
                    <span className="text-muted-foreground">Billable</span>
                    <span>{result.billableEstimate ? "Yes" : "No"}</span>
                  </div>
                ) : null}
                {result.errorCode ? (
                  <p className="rounded-md bg-destructive/10 p-3 text-destructive">
                    {result.errorCode}: {result.errorMessage}
                  </p>
                ) : null}
              </>
            ) : error ? (
              <div className="space-y-2">
                {/* Gateway rejections put the machine code in errorMessage.code (e.g. WINDOW_CLOSED). */}
                {(error.fieldErrors?.code ?? (typeof error.code === "string" ? [error.code] : [])).map(
                  (code) => (
                    <p key={code} className="font-mono text-xs font-semibold text-destructive">
                      {code}
                    </p>
                  ),
                )}
                <p className="rounded-md bg-destructive/10 p-3 text-destructive">{error.message}</p>
              </div>
            ) : (
              <p className="text-muted-foreground">
                Send a message to see the gateway's accept/reject decision here.
              </p>
            )}
          </CardContent>
        </Card>
      </div>
    </>
  );
}
