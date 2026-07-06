import { createFileRoute, Link } from "@tanstack/react-router";
import { ArrowRight, Megaphone, MessageSquareText, PhoneCall } from "lucide-react";
import { useCampaigns } from "@/api/queries/campaigns";
import { useServicesHealth } from "@/api/queries/health";
import { useQualityHealth } from "@/api/queries/quality";
import { useTemplates } from "@/api/queries/templates";
import { usePhoneNumbers } from "@/api/queries/waba";
import { useAuth } from "@/auth/AuthContext";
import { PageHeader } from "@/components/shared/page-header";
import { StatCard } from "@/components/shared/stat-card";
import { StatusBadge } from "@/components/shared/status-badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatNumber, formatRate } from "@/lib/utils";

export const Route = createFileRoute("/_app/")({
  component: OverviewPage,
});

function OverviewPage() {
  const { user, hasPermission } = useAuth();
  const campaigns = useCampaigns(undefined);
  const templates = useTemplates({ pageSize: 1 });
  const phoneNumbers = usePhoneNumbers();
  const quality = useQualityHealth();
  const health = useServicesHealth();

  const running = campaigns.data?.filter((c) => c.status.toLowerCase() === "running").length;
  const latestSnapshot = quality.data?.snapshots[0];
  const openIncidents = quality.data?.openIncidents.length ?? 0;

  return (
    <>
      <PageHeader
        title="Overview"
        description={`Signed in as ${user?.email ?? "user"} — live platform status at a glance.`}
      />
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="Campaigns"
          value={campaigns.data ? formatNumber(campaigns.data.length) : "…"}
          hint={running != null ? `${running} running` : undefined}
        />
        <StatCard
          label="Templates"
          value={templates.data ? formatNumber(templates.data.totalCount) : "…"}
        />
        <StatCard
          label="Phone numbers"
          value={phoneNumbers.data ? formatNumber(phoneNumbers.data.length) : "…"}
        />
        <StatCard
          label="Open quality incidents"
          value={quality.data ? formatNumber(openIncidents) : "…"}
          hint={latestSnapshot ? `Delivery ${formatRate(latestSnapshot.deliveryRate)}` : undefined}
        />
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Quick actions</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-2">
            {hasPermission("campaigns.create") ? (
              <QuickLink to="/campaigns/new" icon={<Megaphone className="size-4" />} label="Create a broadcast campaign" />
            ) : null}
            {hasPermission("templates.create") ? (
              <QuickLink to="/templates/new" icon={<MessageSquareText className="size-4" />} label="Draft a message template" />
            ) : null}
            {hasPermission("messages.send") ? (
              <QuickLink to="/messages" icon={<PhoneCall className="size-4" />} label="Send a single message" />
            ) : null}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Service health</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Service</TableHead>
                  <TableHead>Status</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(health.data?.services ?? []).map((s) => (
                  <TableRow key={s.service}>
                    <TableCell className="font-medium">{s.service}</TableCell>
                    <TableCell>
                      <StatusBadge status={s.status} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>
    </>
  );
}

function QuickLink({ to, icon, label }: { to: string; icon: React.ReactNode; label: string }) {
  return (
    <Link
      to={to}
      className="flex items-center justify-between rounded-lg border p-3 text-sm transition-colors hover:bg-accent"
    >
      <span className="flex items-center gap-2.5">{icon}{label}</span>
      <ArrowRight className="size-4 text-muted-foreground" />
    </Link>
  );
}
