import { createFileRoute, Link, Outlet, useNavigate } from "@tanstack/react-router";
import { Plus } from "lucide-react";
import { useCampaigns } from "@/api/queries/campaigns";
import { useAuth } from "@/auth/AuthContext";
import { PageHeader } from "@/components/shared/page-header";
import { EmptyState, ErrorState, LoadingRows } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatDateTime, formatNumber } from "@/lib/utils";

const CAMPAIGN_STATUSES = ["draft", "scheduled", "running", "completed", "cancelled", "failed"];

/**
 * Layout route: the list always renders; child routes (/new, /$campaignId)
 * open as right-side sheets over it via <Outlet />.
 */
export const Route = createFileRoute("/_app/campaigns")({
  validateSearch: (search): { status?: string } => ({
    status: typeof search.status === "string" ? search.status : undefined,
  }),
  component: CampaignsPage,
});

function CampaignsPage() {
  const { status } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const { hasPermission } = useAuth();
  const campaigns = useCampaigns(status);

  return (
    <>
      <PageHeader
        title="Campaigns"
        description="Broadcast template messages to an audience with tier-aware sending."
        actions={
          hasPermission("campaigns.create") ? (
            <Button onClick={() => void navigate({ to: "/campaigns/new" })}>
              <Plus /> New campaign
            </Button>
          ) : undefined
        }
      />
      <div className="mb-4 w-44">
        <Select
          value={status ?? ""}
          onChange={(e) =>
            void navigate({ search: { status: e.target.value || undefined } })
          }
          aria-label="Filter by status"
        >
          <option value="">All statuses</option>
          {CAMPAIGN_STATUSES.map((s) => (
            <option key={s} value={s}>
              {s.toUpperCase()}
            </option>
          ))}
        </Select>
      </div>
      <Card>
        {campaigns.isPending ? (
          <LoadingRows />
        ) : campaigns.isError ? (
          <ErrorState error={campaigns.error} />
        ) : campaigns.data.length === 0 ? (
          <EmptyState
            title="No campaigns yet"
            description="Create a campaign to broadcast an approved template to your audience."
          />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Audience</TableHead>
                <TableHead className="text-right">Sent</TableHead>
                <TableHead className="text-right">Delivered</TableHead>
                <TableHead className="text-right">Read</TableHead>
                <TableHead className="text-right">Failed</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {campaigns.data.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>
                    <Link
                      to="/campaigns/$campaignId"
                      params={{ campaignId: c.id }}
                      className="font-medium text-primary hover:underline"
                    >
                      {c.name}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <StatusBadge status={c.status} />
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(c.audienceCount)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(c.sentCount)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(c.deliveredCount)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(c.readCount)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatNumber(c.failedCount)}</TableCell>
                  <TableCell className="text-muted-foreground">{formatDateTime(c.createdAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>
      <Outlet />
    </>
  );
}
