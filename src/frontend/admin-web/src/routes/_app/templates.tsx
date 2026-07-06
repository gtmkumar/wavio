import { createFileRoute, Link, Outlet, useNavigate } from "@tanstack/react-router";
import { Plus } from "lucide-react";
import { useTemplates } from "@/api/queries/templates";
import { useAuth } from "@/auth/AuthContext";
import { PageHeader } from "@/components/shared/page-header";
import { EmptyState, ErrorState, LoadingRows } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatDateTime } from "@/lib/utils";

const TEMPLATE_STATUSES = ["DRAFT", "PENDING", "APPROVED", "REJECTED", "PAUSED", "DISABLED"];
const CATEGORIES = ["MARKETING", "UTILITY", "AUTHENTICATION"];

interface TemplateSearch {
  status?: string;
  category?: string;
  page?: number;
}

/**
 * Layout route: the list always renders; child routes (/new, /$templateId)
 * open as right-side sheets over it via <Outlet />.
 */
export const Route = createFileRoute("/_app/templates")({
  validateSearch: (search): TemplateSearch => ({
    status: typeof search.status === "string" ? search.status : undefined,
    category: typeof search.category === "string" ? search.category : undefined,
    page: typeof search.page === "number" ? search.page : undefined,
  }),
  component: TemplatesPage,
});

function TemplatesPage() {
  const { status, category, page } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const { hasPermission } = useAuth();
  const templates = useTemplates({ status, category, page: page ?? 1 });

  const setSearch = (patch: Partial<TemplateSearch>) =>
    void navigate({ search: (prev: TemplateSearch) => ({ ...prev, page: undefined, ...patch }) });

  return (
    <>
      <PageHeader
        title="Templates"
        description="Message templates, compiled to Meta's format and submitted for review."
        actions={
          hasPermission("templates.create") ? (
            <Button onClick={() => void navigate({ to: "/templates/new" })}>
              <Plus /> New template
            </Button>
          ) : undefined
        }
      />
      <div className="mb-4 flex gap-3">
        <Select
          className="w-44"
          value={status ?? ""}
          onChange={(e) => setSearch({ status: e.target.value || undefined })}
          aria-label="Filter by status"
        >
          <option value="">All statuses</option>
          {TEMPLATE_STATUSES.map((s) => (
            <option key={s} value={s}>{s}</option>
          ))}
        </Select>
        <Select
          className="w-44"
          value={category ?? ""}
          onChange={(e) => setSearch({ category: e.target.value || undefined })}
          aria-label="Filter by category"
        >
          <option value="">All categories</option>
          {CATEGORIES.map((c) => (
            <option key={c} value={c}>{c}</option>
          ))}
        </Select>
      </div>
      <Card>
        {templates.isPending ? (
          <LoadingRows />
        ) : templates.isError ? (
          <ErrorState error={templates.error} />
        ) : templates.data.list.length === 0 ? (
          <EmptyState
            title="No templates found"
            description="Draft a template and submit it to Meta for approval."
          />
        ) : (
          <>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Language</TableHead>
                  <TableHead>Category</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Version</TableHead>
                  <TableHead>Updated</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {templates.data.list.map((t) => (
                  <TableRow key={t.id}>
                    <TableCell>
                      <Link
                        to="/templates/$templateId"
                        params={{ templateId: t.id }}
                        className="font-medium text-primary hover:underline"
                      >
                        {t.name}
                      </Link>
                    </TableCell>
                    <TableCell>{t.language}</TableCell>
                    <TableCell>{t.category}</TableCell>
                    <TableCell>
                      <StatusBadge status={t.status} />
                    </TableCell>
                    <TableCell className="tabular-nums">
                      {t.currentVersion ? `v${t.currentVersion.versionNumber}` : "—"}
                    </TableCell>
                    <TableCell className="text-muted-foreground">{formatDateTime(t.updatedAt)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <div className="flex items-center justify-between border-t px-4 py-3 text-sm text-muted-foreground">
              <span>
                Page {templates.data.pageNumber} of {Math.max(templates.data.pageCount, 1)} ·{" "}
                {templates.data.totalCount} total
              </span>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={!templates.data.hasPreviousPage}
                  onClick={() =>
                    void navigate({
                      search: (prev: TemplateSearch) => ({ ...prev, page: (page ?? 1) - 1 }),
                    })
                  }
                >
                  Previous
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={!templates.data.hasNextPage}
                  onClick={() =>
                    void navigate({
                      search: (prev: TemplateSearch) => ({ ...prev, page: (page ?? 1) + 1 }),
                    })
                  }
                >
                  Next
                </Button>
              </div>
            </div>
          </>
        )}
      </Card>
      <Outlet />
    </>
  );
}
