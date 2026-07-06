import { useState } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useCostEstimate, useQuotaStatus, useReconciliation } from "@/api/queries/billing";
import { usePhoneNumbers } from "@/api/queries/waba";
import type { CostEstimateParams, ReconciliationPeriod } from "@/api/queries/billing";
import { RATE_CARD_CATEGORIES } from "@/api/types";
import { useAuth } from "@/auth/AuthContext";
import { PageHeader } from "@/components/shared/page-header";
import { EmptyState, ErrorState, LoadingRows } from "@/components/shared/states";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatMoney, formatNumber } from "@/lib/utils";

export const Route = createFileRoute("/_app/billing")({
  component: BillingPage,
});

function BillingPage() {
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  return (
    <>
      <PageHeader
        title="Billing"
        description="Quota consumption, pre-send cost estimates, and ledger-vs-invoice reconciliation for the acting tenant."
        actions={
          hasPermission("billing.rate_cards.read") ? (
            <Button variant="outline" onClick={() => void navigate({ to: "/rate-cards" })}>
              Rate cards
            </Button>
          ) : undefined
        }
      />
      <div className="grid gap-4">
        <QuotaStatusCard />
        <div className="grid gap-4 lg:grid-cols-2">
          <CostEstimatorCard />
          <ReconciliationCard />
        </div>
      </div>
    </>
  );
}

function QuotaStatusCard() {
  const quotas = useQuotaStatus();
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Quota status</CardTitle>
        <CardDescription>
          Current usage vs. configured limits — a hard marketing breach blocks sends at the gateway.
        </CardDescription>
      </CardHeader>
      <CardContent className="p-0">
        {quotas.isPending ? (
          <LoadingRows />
        ) : quotas.isError ? (
          <ErrorState error={quotas.error} />
        ) : quotas.data.length === 0 ? (
          <EmptyState
            title="No quotas configured"
            description="This tenant has no metering rules yet; sends are unmetered."
          />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Category</TableHead>
                <TableHead>Period</TableHead>
                <TableHead>Unit</TableHead>
                <TableHead className="text-right">Used</TableHead>
                <TableHead className="text-right">Soft limit</TableHead>
                <TableHead className="text-right">Hard limit</TableHead>
                <TableHead>State</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {quotas.data.map((q) => (
                <TableRow key={`${q.category}-${q.period}`}>
                  <TableCell className="font-medium">{q.category}</TableCell>
                  <TableCell>{q.period}</TableCell>
                  <TableCell className="text-muted-foreground">{q.limitUnit}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(q.currentValue)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(q.softLimit)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(q.hardLimit)}</TableCell>
                  <TableCell>
                    {q.hardLimitBlocked ? (
                      <Badge variant="destructive">BLOCKED</Badge>
                    ) : q.softLimitAlerted ? (
                      <Badge variant="warning">SOFT LIMIT</Badge>
                    ) : (
                      <Badge variant="success">OK</Badge>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

function CostEstimatorCard() {
  const phoneNumbers = usePhoneNumbers();
  const [category, setCategory] = useState<string>("marketing");
  const [country, setCountry] = useState("IN");
  const [windowOpen, setWindowOpen] = useState(false);
  const [phoneNumberId, setPhoneNumberId] = useState("");
  const [params, setParams] = useState<CostEstimateParams | null>(null);
  const estimate = useCostEstimate(params);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Cost estimator</CardTitle>
        <CardDescription>
          Per-message billable estimate from the active rate card — what a send would cost right now.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="est-category">Category</Label>
            <Select id="est-category" value={category} onChange={(e) => setCategory(e.target.value)}>
              {RATE_CARD_CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="est-country">Country (market)</Label>
            <Input
              id="est-country"
              value={country}
              onChange={(e) => setCountry(e.target.value)}
              maxLength={2}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="est-window">24h window</Label>
            <Select
              id="est-window"
              value={windowOpen ? "open" : "closed"}
              onChange={(e) => setWindowOpen(e.target.value === "open")}
            >
              <option value="closed">closed</option>
              <option value="open">open</option>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="est-phone">Phone number (volume tier)</Label>
            <Select
              id="est-phone"
              value={phoneNumberId}
              onChange={(e) => setPhoneNumberId(e.target.value)}
            >
              <option value="">Any</option>
              {(phoneNumbers.data ?? []).map((p) => (
                <option key={p.id} value={p.id}>
                  {p.displayPhoneNumber}
                </option>
              ))}
            </Select>
          </div>
        </div>
        <Button
          disabled={!country.trim()}
          onClick={() =>
            setParams({
              category,
              country: country.trim().toUpperCase(),
              windowOpen,
              phoneNumberId: phoneNumberId || undefined,
            })
          }
        >
          Estimate
        </Button>
        {params ? (
          estimate.isPending ? (
            <p className="text-muted-foreground">Estimating…</p>
          ) : estimate.isError ? (
            <ErrorState error={estimate.error} />
          ) : (
            <div className="space-y-2 rounded-md border p-3">
              <div className="flex items-center justify-between">
                <span className="text-muted-foreground">Estimate</span>
                {estimate.data.found ? (
                  <span className="text-lg font-semibold tabular-nums">
                    {estimate.data.billable
                      ? formatMoney(estimate.data.amount, estimate.data.currency)
                      : "Free"}
                  </span>
                ) : (
                  <Badge variant="warning">UNPRICED</Badge>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <Badge variant={estimate.data.billable ? "default" : "success"}>
                  {estimate.data.billable ? "BILLABLE" : "NOT BILLABLE"}
                </Badge>
                {estimate.data.volumeTier ? (
                  <Badge variant="secondary">tier {estimate.data.volumeTier}</Badge>
                ) : null}
              </div>
              <p className="text-xs text-muted-foreground">{estimate.data.reason}</p>
            </div>
          )
        ) : null}
      </CardContent>
    </Card>
  );
}

/** First day of the current month, as a yyyy-MM-dd DateOnly string. */
function monthStart(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-01`;
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function ReconciliationCard() {
  const [periodStart, setPeriodStart] = useState(monthStart);
  const [periodEnd, setPeriodEnd] = useState(today);
  const [period, setPeriod] = useState<ReconciliationPeriod | null>(null);
  const report = useReconciliation(period);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Reconciliation</CardTitle>
        <CardDescription>
          Ledger billable total vs. Meta invoice feed for a period — target variance &lt; 0.5%.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1.5">
            <Label htmlFor="rec-start">From</Label>
            <Input
              id="rec-start"
              type="date"
              value={periodStart}
              onChange={(e) => setPeriodStart(e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="rec-end">To</Label>
            <Input
              id="rec-end"
              type="date"
              value={periodEnd}
              onChange={(e) => setPeriodEnd(e.target.value)}
            />
          </div>
          <Button
            disabled={!periodStart || !periodEnd}
            onClick={() => setPeriod({ periodStart, periodEnd })}
          >
            Run report
          </Button>
        </div>
        {period ? (
          report.isPending ? (
            <p className="text-muted-foreground">Comparing…</p>
          ) : report.isError ? (
            <ErrorState error={report.error} />
          ) : (
            <div className="space-y-3 rounded-md border p-3">
              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <p className="text-muted-foreground">Ledger</p>
                  <p className="font-semibold tabular-nums">
                    {formatNumber(report.data.ledgerTotal)}{" "}
                    <span className="font-normal text-muted-foreground">
                      ({formatNumber(report.data.ledgerRowCount)} rows)
                    </span>
                  </p>
                </div>
                <div>
                  <p className="text-muted-foreground">Invoice feed</p>
                  <p className="font-semibold tabular-nums">
                    {formatNumber(report.data.invoiceTotal)}{" "}
                    <span className="font-normal text-muted-foreground">
                      ({formatNumber(report.data.invoiceRowCount)} rows)
                    </span>
                  </p>
                </div>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={report.data.withinTarget ? "success" : "destructive"}>
                  {report.data.withinTarget ? "WITHIN TARGET" : "VARIANCE EXCEEDED"}
                </Badge>
                <span className="tabular-nums text-muted-foreground">
                  Δ {formatNumber(report.data.varianceAmount)}
                  {report.data.variancePercent != null
                    ? ` (${report.data.variancePercent.toFixed(2)}%)`
                    : ""}
                </span>
              </div>
            </div>
          )
        ) : null}
      </CardContent>
    </Card>
  );
}
