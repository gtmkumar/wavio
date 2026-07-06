import { useState } from "react";
import { createFileRoute, Link, useNavigate } from "@tanstack/react-router";
import { ArrowLeft, SendHorizontal, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import {
  useDeleteTemplate,
  useSubmitTemplate,
  useTemplate,
  useTemplateStatus,
} from "@/api/queries/templates";
import { useAuth } from "@/auth/AuthContext";
import { useStepUpGuard } from "@/auth/StepUpDialog";
import { PageHeader } from "@/components/shared/page-header";
import { ErrorState, LoadingRows } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { formatDateTime } from "@/lib/utils";

export const Route = createFileRoute("/_app/templates/$templateId")({
  component: TemplateDetailPage,
});

function prettyJson(raw: string | null): string {
  if (!raw) return "";
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

function TemplateDetailPage() {
  const { templateId } = Route.useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const template = useTemplate(templateId);
  const status = useTemplateStatus(templateId);
  const submit = useSubmitTemplate();
  const remove = useDeleteTemplate();
  const { guard, stepUpDialog } = useStepUpGuard();
  const [confirmDelete, setConfirmDelete] = useState(false);

  if (template.isPending) return <LoadingRows count={6} />;
  if (template.isError) return <ErrorState error={template.error} />;
  const t = template.data;

  async function onSubmitToMeta() {
    try {
      await submit.mutateAsync(t.id);
      toast.success("Template submitted to Meta for review");
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Submit failed.");
    }
  }

  async function onDelete() {
    setConfirmDelete(false);
    try {
      // templates.delete is a §8 step-up action — the guard prompts for OTP and retries.
      await guard(() => remove.mutateAsync(t.id));
      toast.success("Template deleted");
      await navigate({ to: "/templates" });
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Delete failed.");
    }
  }

  return (
    <>
      <Link to="/templates" className="mb-4 inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" /> All templates
      </Link>
      <PageHeader
        title={t.name}
        description={`${t.category} · ${t.language}${t.metaTemplateId ? ` · Meta ID ${t.metaTemplateId}` : ""}`}
        actions={
          <>
            <StatusBadge status={t.status} />
            {hasPermission("templates.submit") && (t.status === "DRAFT" || t.status === "REJECTED") ? (
              <Button onClick={() => void onSubmitToMeta()} disabled={submit.isPending}>
                <SendHorizontal /> Submit to Meta
              </Button>
            ) : null}
            {hasPermission("templates.delete") ? (
              <Button variant="destructive" onClick={() => setConfirmDelete(true)} disabled={remove.isPending}>
                <Trash2 /> Delete
              </Button>
            ) : null}
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">
              Current version {t.currentVersion ? `(v${t.currentVersion.versionNumber})` : ""}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            {t.currentVersion ? (
              <>
                <div className="flex items-center justify-between">
                  <span className="text-muted-foreground">Version status</span>
                  <StatusBadge status={t.currentVersion.status} />
                </div>
                {t.currentVersion.rejectionReason ? (
                  <p className="rounded-md bg-destructive/10 p-3 text-destructive">
                    {t.currentVersion.rejectionReason}
                  </p>
                ) : null}
                <div>
                  <p className="mb-1 text-muted-foreground">Compiled components</p>
                  <pre className="max-h-72 overflow-auto rounded-md bg-muted p-3 font-mono text-xs">
                    {prettyJson(t.currentVersion.componentsJson)}
                  </pre>
                </div>
                {t.currentVersion.exampleValuesJson ? (
                  <div>
                    <p className="mb-1 text-muted-foreground">Example values</p>
                    <pre className="overflow-auto rounded-md bg-muted p-3 font-mono text-xs">
                      {prettyJson(t.currentVersion.exampleValuesJson)}
                    </pre>
                  </div>
                ) : null}
              </>
            ) : (
              <p className="text-muted-foreground">No version yet.</p>
            )}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Status history</CardTitle>
          </CardHeader>
          <CardContent className="text-sm">
            {status.isPending ? (
              <p className="text-muted-foreground">Loading…</p>
            ) : status.isError ? (
              <p className="text-muted-foreground">History unavailable.</p>
            ) : status.data.history.length === 0 ? (
              <p className="text-muted-foreground">No transitions recorded.</p>
            ) : (
              <ol className="space-y-3">
                {status.data.history.map((e) => (
                  <li key={e.id} className="flex items-start justify-between gap-4">
                    <div>
                      <p className="font-medium">
                        {e.oldStatus ? `${e.oldStatus} → ${e.newStatus}` : e.newStatus}
                      </p>
                      {e.reason ? <p className="text-xs text-muted-foreground">{e.reason}</p> : null}
                    </div>
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {formatDateTime(e.occurredAt)}
                    </span>
                  </li>
                ))}
              </ol>
            )}
          </CardContent>
        </Card>
      </div>

      <Dialog
        open={confirmDelete}
        onClose={() => setConfirmDelete(false)}
        title="Delete template?"
        description={`"${t.name}" will be removed. Campaigns already referencing its versions keep their history.`}
      >
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setConfirmDelete(false)}>
            Keep template
          </Button>
          <Button variant="destructive" onClick={() => void onDelete()}>
            Delete
          </Button>
        </div>
      </Dialog>
      {stepUpDialog}
    </>
  );
}
