import { useState } from "react";
import { createFileRoute } from "@tanstack/react-router";
import { useQualityHealth, useTierAdvisor, useWindowState } from "@/api/queries/quality";
import { usePhoneNumbers } from "@/api/queries/waba";
import { PageHeader } from "@/components/shared/page-header";
import { EmptyState, ErrorState, LoadingRows } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatDateTime, formatNumber, formatRate } from "@/lib/utils";

export const Route = createFileRoute("/_app/quality")({
  component: QualityPage,
});

function QualityPage() {
  const phoneNumbers = usePhoneNumbers();
  const [phoneNumberId, setPhoneNumberId] = useState("");
  const health = useQualityHealth(phoneNumberId || undefined);
  const advisor = useTierAdvisor(phoneNumberId || null);

  const numberLabel = (id: string) =>
    phoneNumbers.data?.find((p) => p.id === id)?.displayPhoneNumber ?? `${id.slice(0, 8)}…`;

  return (
    <>
      <PageHeader
        title="Quality"
        description="Weekly health snapshots, Guardian incidents, and tier growth guidance per number."
      />
      <div className="mb-4 w-64">
        <Select
          value={phoneNumberId}
          onChange={(e) => setPhoneNumberId(e.target.value)}
          aria-label="Phone number"
        >
          <option value="">All phone numbers</option>
          {(phoneNumbers.data ?? []).map((p) => (
            <option key={p.id} value={p.id}>
              {p.displayPhoneNumber}
            </option>
          ))}
        </Select>
      </div>

      {phoneNumberId ? (
        <Card className="mb-4">
          <CardHeader>
            <CardTitle className="text-base">Tier advisor — {numberLabel(phoneNumberId)}</CardTitle>
            <CardDescription>Safe daily-volume plan for growing to the next messaging tier.</CardDescription>
          </CardHeader>
          <CardContent className="text-sm">
            {advisor.isPending ? (
              <p className="text-muted-foreground">Loading…</p>
            ) : advisor.isError ? (
              <ErrorState error={advisor.error} />
            ) : (
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                <div>
                  <p className="text-muted-foreground">Current tier</p>
                  <p className="font-semibold">{advisor.data.currentTier}</p>
                </div>
                <div>
                  <p className="text-muted-foreground">Daily limit</p>
                  <p className="font-semibold tabular-nums">{formatNumber(advisor.data.currentDailyLimit)}</p>
                </div>
                <div>
                  <p className="text-muted-foreground">Recent avg volume</p>
                  <p className="font-semibold tabular-nums">{formatNumber(advisor.data.recentAverageDailyVolume)}</p>
                </div>
                <div>
                  <p className="text-muted-foreground">Recommended volume</p>
                  <p className="font-semibold tabular-nums">{formatNumber(advisor.data.recommendedDailyVolume)}</p>
                </div>
                <div className="sm:col-span-2 lg:col-span-4">
                  <Badge variant={advisor.data.readyToGrow ? "success" : "secondary"}>
                    {advisor.data.readyToGrow ? "READY TO GROW" : "HOLD"}
                    {advisor.data.nextTier ? ` → ${advisor.data.nextTier}` : ""}
                  </Badge>
                  <p className="mt-2 text-muted-foreground">{advisor.data.recommendation}</p>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      ) : null}

      <div className="grid gap-4">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Weekly snapshots</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            {health.isPending ? (
              <LoadingRows />
            ) : health.isError ? (
              <ErrorState error={health.error} />
            ) : health.data.snapshots.length === 0 ? (
              <EmptyState
                title="No snapshots yet"
                description="Snapshots are computed weekly per phone number once traffic flows."
              />
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Number</TableHead>
                    <TableHead>Period</TableHead>
                    <TableHead>Rating</TableHead>
                    <TableHead>Tier</TableHead>
                    <TableHead className="text-right">Sent</TableHead>
                    <TableHead className="text-right">Delivery</TableHead>
                    <TableHead className="text-right">Read</TableHead>
                    <TableHead className="text-right">Block proxy</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {health.data.snapshots.map((s) => (
                    <TableRow key={`${s.phoneNumberId}-${s.periodStart}`}>
                      <TableCell className="font-medium">{numberLabel(s.phoneNumberId)}</TableCell>
                      <TableCell className="text-muted-foreground">
                        {s.periodStart} → {s.periodEnd}
                      </TableCell>
                      <TableCell><StatusBadge status={s.qualityRating} /></TableCell>
                      <TableCell>{s.messagingTier ?? "—"}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatNumber(s.messagesSent)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatRate(s.deliveryRate)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatRate(s.readRate)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatRate(s.blockProxyRate)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Open Guardian incidents</CardTitle>
            <CardDescription>Auto-throttles applied when Meta reports quality degradation.</CardDescription>
          </CardHeader>
          <CardContent className="p-0">
            {health.data && health.data.openIncidents.length === 0 ? (
              <EmptyState title="No open incidents" description="All numbers are sending normally." />
            ) : health.data ? (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Number</TableHead>
                    <TableHead>Type</TableHead>
                    <TableHead>Severity</TableHead>
                    <TableHead>Throttle</TableHead>
                    <TableHead>Trigger</TableHead>
                    <TableHead>Opened</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {health.data.openIncidents.map((i) => (
                    <TableRow key={i.id}>
                      <TableCell className="font-medium">{numberLabel(i.phoneNumberId)}</TableCell>
                      <TableCell>{i.incidentType}</TableCell>
                      <TableCell><StatusBadge status={i.severity} /></TableCell>
                      <TableCell>{i.throttleAction}</TableCell>
                      <TableCell><StatusBadge status={i.triggerRating} /></TableCell>
                      <TableCell className="text-muted-foreground">{formatDateTime(i.openedAt)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : null}
          </CardContent>
        </Card>

        <WindowChecker phoneNumberId={phoneNumberId} />
      </div>
    </>
  );
}

/** 24h customer-service window lookup for a recipient. */
function WindowChecker({ phoneNumberId }: { phoneNumberId: string }) {
  const [waId, setWaId] = useState("");
  const [lookup, setLookup] = useState<{ waId: string; phoneNumberId: string } | null>(null);
  const window_ = useWindowState(lookup?.waId ?? null, lookup?.phoneNumberId ?? null);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Conversation window lookup</CardTitle>
        <CardDescription>
          Check whether the 24-hour customer-service window is open for a recipient.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1.5">
            <Label htmlFor="window-waid">Recipient WhatsApp ID</Label>
            <Input
              id="window-waid"
              className="w-56"
              value={waId}
              onChange={(e) => setWaId(e.target.value)}
              placeholder="919876543210"
            />
          </div>
          <Button
            disabled={!waId.trim() || !phoneNumberId}
            onClick={() => setLookup({ waId: waId.trim(), phoneNumberId })}
          >
            Check window
          </Button>
          {!phoneNumberId ? (
            <p className="text-xs text-muted-foreground">Select a phone number above first.</p>
          ) : null}
        </div>
        {lookup ? (
          window_.isPending ? (
            <p className="text-muted-foreground">Checking…</p>
          ) : window_.isError ? (
            <ErrorState error={window_.error} />
          ) : (
            <div className="flex flex-wrap items-center gap-3">
              <Badge variant={window_.data.csOpen ? "success" : "secondary"}>
                CS window {window_.data.csOpen ? "OPEN" : "CLOSED"}
              </Badge>
              {window_.data.csExpiresAt ? (
                <span className="text-muted-foreground">
                  expires {formatDateTime(window_.data.csExpiresAt)}
                </span>
              ) : null}
              <Badge variant={window_.data.ctwaOpen ? "success" : "secondary"}>
                CTWA window {window_.data.ctwaOpen ? "OPEN" : "CLOSED"}
              </Badge>
              <span className="text-muted-foreground">origin: {window_.data.origin}</span>
            </div>
          )
        ) : null}
      </CardContent>
    </Card>
  );
}
