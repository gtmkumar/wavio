import { useState, type FormEvent } from "react";
import { createFileRoute } from "@tanstack/react-router";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import {
  useConsentState,
  useCreateErasureRequest,
  useErasureRequest,
  useRecordOptIn,
  useRecordOptOut,
  useRetentionPolicies,
  useUpsertRetentionPolicy,
} from "@/api/queries/consent";
import {
  CONSENT_CAPTURE_CHANNELS,
  CONSENT_PURPOSES,
  ERASURE_REQUEST_TYPES,
  OPT_OUT_REASONS,
  OPT_OUT_SCOPES,
} from "@/api/types";
import type { ErasureRequest, RetentionPolicy } from "@/api/types";
import { useAuth } from "@/auth/AuthContext";
import { useStepUpGuard } from "@/auth/StepUpDialog";
import { PageHeader } from "@/components/shared/page-header";
import { EmptyState, ErrorState, FieldErrors, LoadingRows } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Sheet } from "@/components/ui/sheet";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { formatDateTime } from "@/lib/utils";

export const Route = createFileRoute("/_app/consent")({
  component: ConsentPage,
});

function ConsentPage() {
  const { hasPermission } = useAuth();
  // One wa_id drives the whole page: look it up, then record events against it.
  const [waIdInput, setWaIdInput] = useState("");
  const [waId, setWaId] = useState<string | null>(null);

  return (
    <>
      <PageHeader
        title="Consent & DPDP"
        description="Opt-in evidence ledger, suppression state, data-principal requests, and retention policies."
      />
      <div className="grid gap-4">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Consent state</CardTitle>
            <CardDescription>
              Current per-purpose consent and marketing suppression for a WhatsApp ID.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            <form
              className="flex flex-wrap items-end gap-3"
              onSubmit={(e) => {
                e.preventDefault();
                setWaId(waIdInput.trim() || null);
              }}
            >
              <div className="space-y-1.5">
                <Label htmlFor="consent-waid">WhatsApp ID</Label>
                <Input
                  id="consent-waid"
                  className="w-56"
                  value={waIdInput}
                  onChange={(e) => setWaIdInput(e.target.value)}
                  placeholder="919876543210"
                />
              </div>
              <Button type="submit" disabled={!waIdInput.trim()}>
                Look up
              </Button>
            </form>
            {waId ? <ConsentStatePanel waId={waId} /> : null}
          </CardContent>
        </Card>

        {hasPermission("consent.write") ? (
          <div className="grid gap-4 lg:grid-cols-2">
            <OptInCard waId={waIdInput.trim()} />
            <OptOutCard waId={waIdInput.trim()} />
          </div>
        ) : null}

        {hasPermission("consent.requests.read") || hasPermission("consent.requests.manage") ? (
          <ErasureRequestsCard waId={waIdInput.trim()} />
        ) : null}

        {hasPermission("consent.retention.read") ? <RetentionPoliciesCard /> : null}
      </div>
    </>
  );
}

function ConsentStatePanel({ waId }: { waId: string }) {
  const state = useConsentState(waId);
  if (state.isPending) return <LoadingRows count={2} />;
  if (state.isError) return <ErrorState error={state.error} />;
  return (
    <div className="space-y-3 rounded-md border p-3">
      <div className="flex items-center gap-2">
        <span className="font-mono text-xs">{state.data.waId}</span>
        <Badge variant={state.data.suppressed ? "destructive" : "success"}>
          {state.data.suppressed ? "SUPPRESSED" : "NOT SUPPRESSED"}
        </Badge>
      </div>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Purpose</TableHead>
            <TableHead>Consent</TableHead>
            <TableHead>Last opt-in</TableHead>
            <TableHead>Last opt-out</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {state.data.purposes.map((p) => (
            <TableRow key={p.purpose}>
              <TableCell className="font-medium">{p.purpose}</TableCell>
              <TableCell>
                <Badge variant={p.optedIn ? "success" : "secondary"}>
                  {p.optedIn ? "OPTED IN" : "NOT OPTED IN"}
                </Badge>
              </TableCell>
              <TableCell className="text-muted-foreground">{formatDateTime(p.lastOptInAt)}</TableCell>
              <TableCell className="text-muted-foreground">{formatDateTime(p.lastOptOutAt)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function OptInCard({ waId: waIdDefault }: { waId: string }) {
  const optIn = useRecordOptIn();
  const [waId, setWaId] = useState(waIdDefault);
  const [purpose, setPurpose] = useState<string>("marketing");
  const [captureChannel, setCaptureChannel] = useState<string>("in_person");
  const [onBehalfOfName, setOnBehalfOfName] = useState("");
  const [evidenceProofRef, setEvidenceProofRef] = useState("");
  const [error, setError] = useState<ApiError | null>(null);
  // Keep the field following the lookup box until the operator edits it directly.
  const [touched, setTouched] = useState(false);
  const effectiveWaId = touched ? waId : waIdDefault;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const event = await optIn.mutateAsync({
        waId: effectiveWaId.trim(),
        purpose,
        captureChannel,
        onBehalfOfWaId: null,
        onBehalfOfName: onBehalfOfName.trim() || null,
        evidenceProofRef: evidenceProofRef.trim() || null,
        evidenceWamid: null,
        actor: null,
      });
      toast.success(`Opt-in recorded for ${event.waId} (${event.purpose})`);
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Record opt-in</CardTitle>
        <CardDescription>Writes a DPDP evidence row — who consented, to what, captured how.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="space-y-4 text-sm">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="oi-waid">WhatsApp ID</Label>
              <Input
                id="oi-waid"
                value={effectiveWaId}
                onChange={(e) => {
                  setTouched(true);
                  setWaId(e.target.value);
                }}
                placeholder="919876543210"
              />
              <FieldErrors errors={error?.fieldErrors} field="waId" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="oi-purpose">Purpose</Label>
              <Select id="oi-purpose" value={purpose} onChange={(e) => setPurpose(e.target.value)}>
                {CONSENT_PURPOSES.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </Select>
              <FieldErrors errors={error?.fieldErrors} field="purpose" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="oi-channel">Capture channel</Label>
              <Select
                id="oi-channel"
                value={captureChannel}
                onChange={(e) => setCaptureChannel(e.target.value)}
              >
                {CONSENT_CAPTURE_CHANNELS.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </Select>
              <FieldErrors errors={error?.fieldErrors} field="captureChannel" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="oi-behalf">On behalf of (optional)</Label>
              <Input
                id="oi-behalf"
                value={onBehalfOfName}
                onChange={(e) => setOnBehalfOfName(e.target.value)}
                placeholder="Consenting party's name"
              />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="oi-proof">Evidence proof ref (optional)</Label>
            <Input
              id="oi-proof"
              value={evidenceProofRef}
              onChange={(e) => setEvidenceProofRef(e.target.value)}
              placeholder="e.g. crm://case/1234 or a signed-form scan ref"
            />
          </div>
          {error && !error.fieldErrors ? (
            <p className="text-sm text-destructive">{error.message}</p>
          ) : null}
          <div className="flex justify-end">
            <Button type="submit" disabled={optIn.isPending || !effectiveWaId.trim()}>
              {optIn.isPending ? "Recording…" : "Record opt-in"}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function OptOutCard({ waId: waIdDefault }: { waId: string }) {
  const optOut = useRecordOptOut();
  const [waId, setWaId] = useState(waIdDefault);
  const [scope, setScope] = useState<string>("marketing");
  const [reason, setReason] = useState<string>("manual");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<ApiError | null>(null);
  const [touched, setTouched] = useState(false);
  const effectiveWaId = touched ? waId : waIdDefault;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const event = await optOut.mutateAsync({
        waId: effectiveWaId.trim(),
        scope,
        reason,
        notes: notes.trim() || null,
      });
      toast.success(`Opt-out recorded for ${event.waId} (scope ${event.scope})`);
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Record opt-out</CardTitle>
        <CardDescription>
          Suppresses marketing sends immediately. STOP-keyword opt-outs are captured automatically by the listener.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="space-y-4 text-sm">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="oo-waid">WhatsApp ID</Label>
              <Input
                id="oo-waid"
                value={effectiveWaId}
                onChange={(e) => {
                  setTouched(true);
                  setWaId(e.target.value);
                }}
                placeholder="919876543210"
              />
              <FieldErrors errors={error?.fieldErrors} field="waId" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="oo-scope">Scope</Label>
              <Select id="oo-scope" value={scope} onChange={(e) => setScope(e.target.value)}>
                {OPT_OUT_SCOPES.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </Select>
              <FieldErrors errors={error?.fieldErrors} field="scope" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="oo-reason">Reason</Label>
              <Select id="oo-reason" value={reason} onChange={(e) => setReason(e.target.value)}>
                {OPT_OUT_REASONS.map((r) => (
                  <option key={r} value={r}>
                    {r}
                  </option>
                ))}
              </Select>
              <FieldErrors errors={error?.fieldErrors} field="reason" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="oo-notes">Notes (optional)</Label>
              <Input id="oo-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
          </div>
          {error && !error.fieldErrors ? (
            <p className="text-sm text-destructive">{error.message}</p>
          ) : null}
          <div className="flex justify-end">
            <Button
              type="submit"
              variant="destructive"
              disabled={optOut.isPending || !effectiveWaId.trim()}
            >
              {optOut.isPending ? "Recording…" : "Record opt-out"}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function ErasureRequestsCard({ waId: waIdDefault }: { waId: string }) {
  const { hasPermission } = useAuth();
  const create = useCreateErasureRequest();
  const { guard, stepUpDialog } = useStepUpGuard();

  const [waId, setWaId] = useState(waIdDefault);
  const [touched, setTouched] = useState(false);
  const effectiveWaId = touched ? waId : waIdDefault;
  const [requestType, setRequestType] = useState<string>("erasure");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<ApiError | null>(null);
  const [created, setCreated] = useState<ErasureRequest | null>(null);

  const [lookupInput, setLookupInput] = useState("");
  const [lookupId, setLookupId] = useState<string | null>(null);
  const lookup = useErasureRequest(lookupId);

  async function onCreate(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      // consent.requests.manage is critical-risk — §8 step-up prompts for OTP and retries.
      const request = await guard(() =>
        create.mutateAsync({
          waId: effectiveWaId.trim(),
          requestType,
          reason: reason.trim() || null,
          requestedBy: null,
        }),
      );
      setCreated(request);
      setLookupInput(request.id);
      toast.success(`${request.requestType} request raised — status ${request.status}`);
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Data-principal requests</CardTitle>
        <CardDescription>
          DPDP erasure / export requests. Content redaction runs asynchronously; billing and consent ledgers are retained.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-6 text-sm lg:grid-cols-2">
        {hasPermission("consent.requests.manage") ? (
          <form onSubmit={onCreate} noValidate className="space-y-4">
            <p className="font-medium">Raise a request</p>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label htmlFor="er-waid">WhatsApp ID</Label>
                <Input
                  id="er-waid"
                  value={effectiveWaId}
                  onChange={(e) => {
                    setTouched(true);
                    setWaId(e.target.value);
                  }}
                  placeholder="919876543210"
                />
                <FieldErrors errors={error?.fieldErrors} field="waId" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="er-type">Type</Label>
                <Select
                  id="er-type"
                  value={requestType}
                  onChange={(e) => setRequestType(e.target.value)}
                >
                  {ERASURE_REQUEST_TYPES.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </Select>
                <FieldErrors errors={error?.fieldErrors} field="requestType" />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="er-reason">Reason (optional)</Label>
              <Textarea
                id="er-reason"
                rows={2}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
            {error && !error.fieldErrors ? (
              <p className="text-sm text-destructive">{error.message}</p>
            ) : null}
            <div className="flex items-center justify-between gap-3">
              {created ? (
                <span className="truncate font-mono text-xs text-muted-foreground" title={created.id}>
                  Created: {created.id}
                </span>
              ) : (
                <span />
              )}
              <Button type="submit" disabled={create.isPending || !effectiveWaId.trim()}>
                {create.isPending ? "Raising…" : "Raise request"}
              </Button>
            </div>
          </form>
        ) : (
          <EmptyState
            title="No permission to raise requests"
            description="consent.requests.manage is required."
          />
        )}

        <div className="space-y-4">
          <p className="font-medium">Check a request</p>
          <form
            className="flex items-end gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              setLookupId(lookupInput.trim() || null);
            }}
          >
            <div className="min-w-0 flex-1 space-y-1.5">
              <Label htmlFor="er-lookup">Request ID</Label>
              <Input
                id="er-lookup"
                value={lookupInput}
                onChange={(e) => setLookupInput(e.target.value)}
                placeholder="00000000-0000-0000-0000-000000000000"
              />
            </div>
            <Button type="submit" variant="outline" disabled={!lookupInput.trim()}>
              Check
            </Button>
          </form>
          {lookupId ? (
            lookup.isPending ? (
              <p className="text-muted-foreground">Loading…</p>
            ) : lookup.isError ? (
              <ErrorState error={lookup.error} />
            ) : (
              <div className="space-y-2 rounded-md border p-3">
                <div className="flex items-center gap-2">
                  <Badge variant="secondary">{lookup.data.requestType.toUpperCase()}</Badge>
                  <StatusBadge status={lookup.data.status} />
                  <span className="font-mono text-xs">{lookup.data.waId}</span>
                </div>
                <p className="text-xs text-muted-foreground">
                  Raised {formatDateTime(lookup.data.createdAt)}
                  {lookup.data.contentErasedAt
                    ? ` · content erased ${formatDateTime(lookup.data.contentErasedAt)}`
                    : ""}
                  {lookup.data.completedAt
                    ? ` · completed ${formatDateTime(lookup.data.completedAt)}`
                    : ""}
                  {lookup.data.exportRef ? ` · export ${lookup.data.exportRef}` : ""}
                </p>
                {lookup.data.reason ? (
                  <p className="text-xs text-muted-foreground">Reason: {lookup.data.reason}</p>
                ) : null}
              </div>
            )
          ) : null}
        </div>
      </CardContent>
      {stepUpDialog}
    </Card>
  );
}

function RetentionPoliciesCard() {
  const { hasPermission } = useAuth();
  const policies = useRetentionPolicies();
  const [editing, setEditing] = useState<RetentionPolicy | null>(null);
  const canManage = hasPermission("consent.retention.manage");

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Retention policies</CardTitle>
        <CardDescription>
          How long each data class is kept. Editing writes a tenant override; platform defaults stay untouched.
        </CardDescription>
      </CardHeader>
      <CardContent className="p-0">
        {policies.isPending ? (
          <LoadingRows />
        ) : policies.isError ? (
          <ErrorState error={policies.error} />
        ) : policies.data.length === 0 ? (
          <EmptyState title="No retention policies" description="Platform defaults have not been seeded yet." />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Data class</TableHead>
                <TableHead className="text-right">Retention (days)</TableHead>
                <TableHead>Basis</TableHead>
                <TableHead>Scope</TableHead>
                <TableHead>Enabled</TableHead>
                <TableHead>Updated</TableHead>
                {canManage ? <TableHead className="w-24" /> : null}
              </TableRow>
            </TableHeader>
            <TableBody>
              {policies.data.map((p) => (
                <TableRow key={p.id}>
                  <TableCell className="font-medium">{p.dataClass}</TableCell>
                  <TableCell className="text-right tabular-nums">{p.retentionDays}</TableCell>
                  <TableCell className="text-muted-foreground">{p.basis ?? "—"}</TableCell>
                  <TableCell>
                    <Badge variant={p.tenantId ? "default" : "secondary"}>
                      {p.tenantId ? "TENANT OVERRIDE" : "PLATFORM DEFAULT"}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant={p.enabled ? "success" : "secondary"}>
                      {p.enabled ? "ENABLED" : "DISABLED"}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{formatDateTime(p.updatedAt)}</TableCell>
                  {canManage ? (
                    <TableCell className="text-right">
                      <Button variant="outline" size="sm" onClick={() => setEditing(p)}>
                        Override
                      </Button>
                    </TableCell>
                  ) : null}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
      {editing ? <RetentionPolicyEditor policy={editing} onClose={() => setEditing(null)} /> : null}
    </Card>
  );
}

function RetentionPolicyEditor({
  policy,
  onClose,
}: {
  policy: RetentionPolicy;
  onClose: () => void;
}) {
  const upsert = useUpsertRetentionPolicy();
  const { guard, stepUpDialog } = useStepUpGuard();
  const [retentionDays, setRetentionDays] = useState(String(policy.retentionDays));
  const [basis, setBasis] = useState(policy.basis ?? "");
  const [enabled, setEnabled] = useState(policy.enabled);
  const [error, setError] = useState<ApiError | null>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      // consent.retention.manage is critical-risk — §8 step-up prompts for OTP and retries.
      await guard(() =>
        upsert.mutateAsync({
          dataClass: policy.dataClass,
          retentionDays: Number(retentionDays),
          basis: basis.trim() || null,
          enabled,
        }),
      );
      toast.success(`Retention override saved for ${policy.dataClass}`);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <>
      <Sheet
        open
        onClose={onClose}
        size="md"
        title={`Retention — ${policy.dataClass}`}
        description="Saves this tenant's override for the data class."
      >
        <form onSubmit={onSubmit} noValidate className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="rp-days">Retention days</Label>
              <Input
                id="rp-days"
                inputMode="numeric"
                value={retentionDays}
                onChange={(e) => setRetentionDays(e.target.value)}
              />
              <FieldErrors errors={error?.fieldErrors} field="retentionDays" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="rp-enabled">Enabled</Label>
              <Select
                id="rp-enabled"
                value={enabled ? "yes" : "no"}
                onChange={(e) => setEnabled(e.target.value === "yes")}
              >
                <option value="yes">yes</option>
                <option value="no">no</option>
              </Select>
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="rp-basis">Basis (optional)</Label>
            <Input
              id="rp-basis"
              value={basis}
              onChange={(e) => setBasis(e.target.value)}
              placeholder="e.g. DPDP §8(7) / contractual requirement"
            />
            <FieldErrors errors={error?.fieldErrors} field="basis" />
          </div>
          {error && !error.fieldErrors ? (
            <p className="text-sm text-destructive">{error.message}</p>
          ) : null}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={
                upsert.isPending ||
                retentionDays.trim() === "" ||
                !Number.isInteger(Number(retentionDays)) ||
                Number(retentionDays) < 0
              }
            >
              {upsert.isPending ? "Saving…" : "Save override"}
            </Button>
          </div>
        </form>
      </Sheet>
      {stepUpDialog}
    </>
  );
}
