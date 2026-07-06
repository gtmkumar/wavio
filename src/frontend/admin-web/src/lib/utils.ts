import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/** Render an ISO timestamp (or null) as local date + time; em dash when absent. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : dateTimeFormat.format(d);
}

const numberFormat = new Intl.NumberFormat();

export function formatNumber(n: number | null | undefined): string {
  return n == null ? "—" : numberFormat.format(n);
}

/** 0–1 ratio (e.g. delivery rate) as a percentage string. */
export function formatRate(rate: number | null | undefined): string {
  return rate == null ? "—" : `${(rate * 100).toFixed(1)}%`;
}

/** Amount + ISO currency code (e.g. billing ledger totals, per-message prices). */
export function formatMoney(
  amount: number | null | undefined,
  currency: string | null | undefined,
): string {
  if (amount == null) return "—";
  if (!currency) return numberFormat.format(amount);
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency,
      minimumFractionDigits: 2,
      maximumFractionDigits: 4,
    }).format(amount);
  } catch {
    // Unknown/garbage currency code from data — don't crash the page over formatting.
    return `${numberFormat.format(amount)} ${currency}`;
  }
}
