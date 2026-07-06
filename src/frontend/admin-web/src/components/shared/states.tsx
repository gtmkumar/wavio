import type { LucideIcon } from "lucide-react";
import { AlertTriangle, Inbox } from "lucide-react";
import { ApiError } from "@/api/http";
import { Skeleton } from "@/components/ui/skeleton";

export function LoadingRows({ count = 4 }: { count?: number }) {
  return (
    <div className="space-y-2 p-4">
      {Array.from({ length: count }, (_, i) => (
        <Skeleton key={i} className="h-9 w-full" />
      ))}
    </div>
  );
}

export function EmptyState({
  icon: Icon = Inbox,
  title,
  description,
}: {
  icon?: LucideIcon;
  title: string;
  description?: string;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-12 text-center">
      <Icon className="size-8 text-muted-foreground/60" />
      <p className="font-medium">{title}</p>
      {description ? <p className="max-w-sm text-sm text-muted-foreground">{description}</p> : null}
    </div>
  );
}

export function ErrorState({ error }: { error: unknown }) {
  const message =
    error instanceof ApiError
      ? error.message
      : error instanceof Error
        ? error.message
        : "Something went wrong.";
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-12 text-center">
      <AlertTriangle className="size-8 text-destructive" />
      <p className="font-medium">Request failed</p>
      <p className="max-w-md text-sm text-muted-foreground">{message}</p>
    </div>
  );
}

/** Inline per-field validation errors from the backend's 422 dict. */
export function FieldErrors({
  errors,
  field,
}: {
  errors: Record<string, string[]> | null | undefined;
  field: string;
}) {
  // Backend field keys vary in casing (camelCase properties, sometimes
  // PascalCase from FluentValidation) — match case-insensitively.
  const messages =
    errors &&
    Object.entries(errors).find(([k]) => k.toLowerCase() === field.toLowerCase())?.[1];
  if (!messages?.length) return null;
  return <p className="mt-1 text-xs text-destructive">{messages.join(" ")}</p>;
}
