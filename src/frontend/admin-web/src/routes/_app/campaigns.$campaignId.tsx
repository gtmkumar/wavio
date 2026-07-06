import { useState } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { Ban, Rocket } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import { useCampaign, useCancelCampaign, useLaunchCampaign } from "@/api/queries/campaigns";
import { useAuth } from "@/auth/AuthContext";
import { useStepUpGuard } from "@/auth/StepUpDialog";
import { ErrorState, LoadingRows } from "@/components/shared/states";
import { StatCard } from "@/components/shared/stat-card";
import { StatusBadge } from "@/components/shared/status-badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { Sheet } from "@/components/ui/sheet";
import { formatDateTime, formatNumber } from "@/lib/utils";

export const Route = createFileRoute("/_app/campaigns/$campaignId")({
  component: CampaignDetailPage,
});

const ACTIVE_STATUSES = new Set(["running", "launching", "scheduled"]);

function CampaignDetailPage() {
  const { campaignId } = Route.useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const { guard, stepUpDialog } = useStepUpGuard();
  const [confirm, setConfirm] = useState<"launch" | "cancel" | null>(null);

  const campaign = useCampaign(campaignId, {
    // Live progress while the campaign is actively sending.
    refetchInterval: (query) =>
      query.state.data && ACTIVE_STATUSES.has(query.state.data.status.toLowerCase()) ? 3000 : false,
  });
  const launch = useLaunchCampaign();
  const cancel = useCancelCampaign();

  const close = () => void navigate({ to: "/campaigns" });
  const c = campaign.data;
  const status = c?.status.toLowerCase() ?? "";

  async function run(action: "launch" | "cancel") {
    setConfirm(null);
    try {
      // Launch is a §8 step-up action — the guard prompts for OTP and retries.
      await guard(() => (action === "launch" ? launch : cancel).mutateAsync(campaignId));
      toast.success(action === "launch" ? "Campaign launched" : "Campaign cancelled");
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : `Could not ${action} the campaign.`);
    }
  }

  return (
    <Sheet
      open
      onClose={close}
      size="xl"
      title={c?.name ?? "Campaign"}
      description={c ? `Created for ${formatNumber(c.audienceCount)} recipients` : undefined}
      actions={
        c ? (
          <>
            <StatusBadge status={c.status} />
            {hasPermission("campaigns.launch") && (status === "draft" || status === "scheduled") ? (
              <Button size="sm" onClick={() => setConfirm("launch")} disabled={launch.isPending}>
                <Rocket /> Launch
              </Button>
            ) : null}
            {hasPermission("campaigns.cancel") && ACTIVE_STATUSES.has(status) ? (
              <Button size="sm" variant="destructive" onClick={() => setConfirm("cancel")} disabled={cancel.isPending}>
                <Ban /> Cancel
              </Button>
            ) : null}
          </>
        ) : undefined
      }
    >
      {campaign.isPending ? (
        <LoadingRows count={6} />
      ) : campaign.isError ? (
        <ErrorState error={campaign.error} />
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <StatCard label="Audience" value={formatNumber(c!.audienceCount)} hint={`${formatNumber(c!.suppressedCount)} suppressed`} />
            <StatCard label="Sent" value={formatNumber(c!.sentCount)} />
            <StatCard label="Delivered" value={formatNumber(c!.deliveredCount)} />
            <StatCard label="Read" value={formatNumber(c!.readCount)} />
            <StatCard label="Failed" value={formatNumber(c!.failedCount)} />
          </div>

          <div className="mt-6 grid gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Timing</CardTitle>
              </CardHeader>
              <CardContent className="space-y-2 text-sm">
                <InfoRow label="Scheduled" value={formatDateTime(c!.scheduledAt)} />
                <InfoRow label="Started" value={formatDateTime(c!.startedAt)} />
                <InfoRow label="Completed" value={formatDateTime(c!.completedAt)} />
                <InfoRow
                  label="Projected cost"
                  value={c!.projectedCost != null ? `${c!.projectedCost} ${c!.projectedCurrency ?? ""}` : "—"}
                />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Failure breakdown</CardTitle>
              </CardHeader>
              <CardContent className="text-sm">
                {c!.failureBreakdown && Object.keys(c!.failureBreakdown).length > 0 ? (
                  <div className="space-y-2">
                    {Object.entries(c!.failureBreakdown).map(([code, count]) => (
                      <InfoRow key={code} label={code} value={formatNumber(count)} />
                    ))}
                  </div>
                ) : (
                  <p className="text-muted-foreground">No failures recorded.</p>
                )}
              </CardContent>
            </Card>
          </div>
        </>
      )}

      <Dialog
        open={confirm !== null}
        onClose={() => setConfirm(null)}
        title={confirm === "launch" ? "Launch campaign?" : "Cancel campaign?"}
        description={
          confirm === "launch"
            ? `This starts sending to ${formatNumber(c?.audienceCount ?? 0)} recipients (minus suppressions) immediately.`
            : "Recipients not yet processed will be skipped. This cannot be undone."
        }
      >
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setConfirm(null)}>
            Keep as is
          </Button>
          <Button
            variant={confirm === "cancel" ? "destructive" : "default"}
            onClick={() => void run(confirm!)}
          >
            {confirm === "launch" ? "Launch now" : "Cancel campaign"}
          </Button>
        </div>
      </Dialog>
      {stepUpDialog}
    </Sheet>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium tabular-nums">{value}</span>
    </div>
  );
}
