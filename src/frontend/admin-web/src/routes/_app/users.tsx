import { createFileRoute, Link, Outlet, useNavigate } from "@tanstack/react-router";
import { Search, UserPlus } from "lucide-react";
import { usePeople } from "@/api/queries/users";
import { useAuth } from "@/auth/AuthContext";
import { PageHeader } from "@/components/shared/page-header";
import { EmptyState, ErrorState, LoadingRows } from "@/components/shared/states";
import { StatCard } from "@/components/shared/stat-card";
import { StatusBadge } from "@/components/shared/status-badge";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatDateTime } from "@/lib/utils";

interface UsersSearch {
  search?: string;
  page?: number;
}

/**
 * Layout route: the directory always renders; child routes (/invite, /$userId)
 * open as right-side sheets over it via <Outlet />.
 */
export const Route = createFileRoute("/_app/users")({
  validateSearch: (search): UsersSearch => ({
    search: typeof search.search === "string" ? search.search : undefined,
    page: typeof search.page === "number" ? search.page : undefined,
  }),
  component: UsersPage,
});

function UsersPage() {
  const { search, page } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const { hasPermission } = useAuth();
  const people = usePeople({ search, page: page ?? 1 });

  const setSearch = (value: string) =>
    void navigate({
      search: (prev: UsersSearch) => ({ ...prev, search: value || undefined, page: undefined }),
      replace: true,
    });

  const counts = people.data?.counts;
  const list = people.data?.people;

  return (
    <>
      <PageHeader
        title="Users"
        description="Everyone with access to this workspace — invite, suspend, and assign roles."
        actions={
          hasPermission("users.create") ? (
            <Button onClick={() => void navigate({ to: "/users/invite" })}>
              <UserPlus /> Invite user
            </Button>
          ) : undefined
        }
      />
      <div className="mb-4 grid gap-3 sm:grid-cols-3">
        <StatCard label="All people" value={counts?.all ?? "—"} />
        <StatCard label="Platform staff" value={counts?.platformStaff ?? "—"} hint="Work across every tenant" />
        <StatCard label="Tenant staff" value={counts?.tenantStaff ?? "—"} hint="Scoped to one tenant" />
      </div>
      <div className="relative mb-4 max-w-sm">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          className="pl-9"
          placeholder="Search by name, email, or role…"
          value={search ?? ""}
          onChange={(e) => setSearch(e.target.value)}
          aria-label="Search people"
        />
      </div>
      <Card>
        {people.isPending ? (
          <LoadingRows />
        ) : people.isError ? (
          <ErrorState error={people.error} />
        ) : list == null || list.list.length === 0 ? (
          <EmptyState
            title="No people found"
            description="Invite a teammate to give them access to the console."
          />
        ) : (
          <>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Person</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Scope</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Last active</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.list.map((p) => (
                  <TableRow key={p.id}>
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary">
                          {p.initials}
                        </span>
                        <div className="min-w-0">
                          <Link
                            to="/users/$userId"
                            params={{ userId: p.id }}
                            className="block truncate font-medium text-primary hover:underline"
                          >
                            {p.name}
                          </Link>
                          <p className="truncate text-xs text-muted-foreground">{p.email}</p>
                        </div>
                      </div>
                    </TableCell>
                    <TableCell>{p.roleName}</TableCell>
                    <TableCell>
                      <Badge variant={p.tier === "platform" ? "default" : "secondary"}>
                        {p.scopeLabel}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <StatusBadge status={p.status} />
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {p.lastActiveAt ? formatDateTime(p.lastActiveAt) : "Never"}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <div className="flex items-center justify-between border-t px-4 py-3 text-sm text-muted-foreground">
              <span>
                Page {list.pageNumber} of {Math.max(list.pageCount, 1)} · {list.totalCount} total
              </span>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={!list.hasPreviousPage}
                  onClick={() =>
                    void navigate({
                      search: (prev: UsersSearch) => ({ ...prev, page: (page ?? 1) - 1 }),
                    })
                  }
                >
                  Previous
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={!list.hasNextPage}
                  onClick={() =>
                    void navigate({
                      search: (prev: UsersSearch) => ({ ...prev, page: (page ?? 1) + 1 }),
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
