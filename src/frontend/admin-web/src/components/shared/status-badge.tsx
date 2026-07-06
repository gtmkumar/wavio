import { Badge } from "@/components/ui/badge";

/**
 * One color mapping for every status vocabulary in the ops slice:
 * campaigns (draft/scheduled/running/completed/cancelled/failed),
 * templates (DRAFT/PENDING/APPROVED/REJECTED/PAUSED/DISABLED),
 * phone numbers (CONNECTED/FLAGGED/RESTRICTED/BANNED/PENDING),
 * quality ratings (GREEN/YELLOW/RED) and incidents (open/resolved).
 */
const STATUS_VARIANTS: Record<string, "default" | "secondary" | "success" | "warning" | "destructive"> = {
  draft: "secondary",
  scheduled: "default",
  running: "default",
  launching: "default",
  completed: "success",
  approved: "success",
  connected: "success",
  green: "success",
  healthy: "success",
  accepted: "success",
  resolved: "success",
  open: "warning",
  pending: "warning",
  paused: "warning",
  flagged: "warning",
  yellow: "warning",
  degraded: "warning",
  cancelled: "secondary",
  disabled: "secondary",
  rejected: "destructive",
  failed: "destructive",
  restricted: "destructive",
  banned: "destructive",
  red: "destructive",
  unhealthy: "destructive",
};

export function StatusBadge({ status }: { status: string | null | undefined }) {
  if (!status) return <span className="text-muted-foreground">—</span>;
  return (
    <Badge variant={STATUS_VARIANTS[status.toLowerCase()] ?? "outline"}>
      {status.toUpperCase()}
    </Badge>
  );
}
