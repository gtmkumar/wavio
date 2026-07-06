import { useState, type FormEvent } from "react";
import { createFileRoute } from "@tanstack/react-router";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import { useRateCards, useUpsertRateCard } from "@/api/queries/billing";
import { RATE_CARD_CATEGORIES } from "@/api/types";
import type { RateCard, UpsertRateCardEntryRequest, UpsertRateCardRequest } from "@/api/types";
import { useAuth } from "@/auth/AuthContext";
import { useStepUpGuard } from "@/auth/StepUpDialog";
import { PageHeader } from "@/components/shared/page-header";
import { EmptyState, ErrorState, FieldErrors, LoadingRows } from "@/components/shared/states";
import { StatusBadge } from "@/components/shared/status-badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { formatMoney } from "@/lib/utils";

export const Route = createFileRoute("/_app/rate-cards")({
  component: RateCardsPage,
});

function RateCardsPage() {
  const { hasPermission } = useAuth();
  const rateCards = useRateCards();
  const [editing, setEditing] = useState<RateCard | null | "new">(null);
  const canManage = hasPermission("billing.rate_cards.manage");

  return (
    <>
      <PageHeader
        title="Rate cards"
        description="Meta's versioned per-message pricing — platform-wide per currency, drives estimates and the cost ledger."
        actions={
          canManage ? (
            <Button onClick={() => setEditing("new")}>
              <Plus /> New rate card
            </Button>
          ) : undefined
        }
      />
      {rateCards.isPending ? (
        <LoadingRows />
      ) : rateCards.isError ? (
        <ErrorState error={rateCards.error} />
      ) : rateCards.data.length === 0 ? (
        <EmptyState
          title="No rate cards yet"
          description="Create one to price sends — estimates report UNPRICED until an active card covers the category and market."
        />
      ) : (
        <div className="grid gap-4">
          {rateCards.data.map((card) => (
            <Card key={card.id}>
              <CardHeader className="flex-row items-start justify-between space-y-0">
                <div>
                  <CardTitle className="flex items-center gap-2 text-base">
                    {card.name} <StatusBadge status={card.status} />
                  </CardTitle>
                  <CardDescription>
                    {card.currency} · source {card.source} · effective {card.effectiveFrom}
                    {card.effectiveTo ? ` → ${card.effectiveTo}` : " (open-ended)"}
                    {card.notes ? ` — ${card.notes}` : ""}
                  </CardDescription>
                </div>
                {canManage ? (
                  <Button variant="outline" size="sm" onClick={() => setEditing(card)}>
                    <Pencil /> Edit
                  </Button>
                ) : null}
              </CardHeader>
              <CardContent className="p-0">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Category</TableHead>
                      <TableHead>Market</TableHead>
                      <TableHead>Volume tier</TableHead>
                      <TableHead className="text-right">Price / message</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {card.entries.map((e) => (
                      <TableRow key={e.id}>
                        <TableCell className="font-medium">{e.category}</TableCell>
                        <TableCell>{e.market}</TableCell>
                        <TableCell className="text-muted-foreground">{e.volumeTier ?? "—"}</TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatMoney(e.pricePerMessage, e.currency)}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
      {editing !== null ? (
        <RateCardEditor card={editing === "new" ? null : editing} onClose={() => setEditing(null)} />
      ) : null}
    </>
  );
}

interface EntryDraft {
  category: string;
  market: string;
  volumeTier: string;
  price: string;
}

const EMPTY_ENTRY: EntryDraft = { category: "marketing", market: "IN", volumeTier: "", price: "" };

function toDrafts(card: RateCard | null): EntryDraft[] {
  if (!card) return [{ ...EMPTY_ENTRY }];
  return card.entries.map((e) => ({
    category: e.category,
    market: e.market,
    volumeTier: e.volumeTier ?? "",
    price: String(e.pricePerMessage),
  }));
}

/** Create/edit dialog — PUT replaces the card's entire entry set (backend upsert semantics). */
function RateCardEditor({ card, onClose }: { card: RateCard | null; onClose: () => void }) {
  const upsert = useUpsertRateCard();
  const { guard, stepUpDialog } = useStepUpGuard();

  const [name, setName] = useState(card?.name ?? "");
  const [currency, setCurrency] = useState(card?.currency ?? "INR");
  const [source, setSource] = useState(card?.source ?? "manual");
  const [status, setStatus] = useState(card?.status ?? "draft");
  const [effectiveFrom, setEffectiveFrom] = useState(card?.effectiveFrom ?? "");
  const [effectiveTo, setEffectiveTo] = useState(card?.effectiveTo ?? "");
  const [notes, setNotes] = useState(card?.notes ?? "");
  const [entries, setEntries] = useState<EntryDraft[]>(() => toDrafts(card));
  const [error, setError] = useState<ApiError | null>(null);

  function patchEntry(index: number, patch: Partial<EntryDraft>) {
    setEntries((prev) => prev.map((e, i) => (i === index ? { ...e, ...patch } : e)));
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const request: UpsertRateCardRequest = {
      name,
      currency: currency.trim().toUpperCase(),
      source,
      effectiveFrom,
      effectiveTo: effectiveTo || null,
      status,
      notes: notes.trim() || null,
      entries: entries.map<UpsertRateCardEntryRequest>((d) => ({
        category: d.category,
        market: d.market.trim().toUpperCase(),
        volumeTier: d.volumeTier.trim() || null,
        pricePerMessage: Number(d.price),
      })),
    };
    try {
      // billing.rate_cards.manage is critical-risk — §8 step-up prompts for OTP and retries.
      await guard(() => upsert.mutateAsync({ id: card?.id ?? null, request }));
      toast.success(card ? "Rate card updated" : "Rate card created");
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <>
      <Dialog
        open
        onClose={onClose}
        title={card ? `Edit ${card.name}` : "New rate card"}
        description="Saving replaces the card's header and its entire entry set."
      >
        <form onSubmit={onSubmit} noValidate className="max-h-[70vh] space-y-4 overflow-y-auto pr-1">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="rc-name">Name</Label>
              <Input id="rc-name" value={name} onChange={(e) => setName(e.target.value)} />
              <FieldErrors errors={error?.fieldErrors} field="name" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="rc-currency">Currency</Label>
              <Input
                id="rc-currency"
                value={currency}
                onChange={(e) => setCurrency(e.target.value)}
                maxLength={3}
              />
              <FieldErrors errors={error?.fieldErrors} field="currency" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="rc-source">Source</Label>
              <Select id="rc-source" value={source} onChange={(e) => setSource(e.target.value)}>
                <option value="manual">manual</option>
                <option value="meta">meta</option>
              </Select>
              <FieldErrors errors={error?.fieldErrors} field="source" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="rc-status">Status</Label>
              <Select id="rc-status" value={status} onChange={(e) => setStatus(e.target.value)}>
                <option value="draft">draft</option>
                <option value="active">active</option>
                <option value="superseded">superseded</option>
              </Select>
              <FieldErrors errors={error?.fieldErrors} field="status" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="rc-from">Effective from</Label>
              <Input
                id="rc-from"
                type="date"
                value={effectiveFrom}
                onChange={(e) => setEffectiveFrom(e.target.value)}
              />
              <FieldErrors errors={error?.fieldErrors} field="effectiveFrom" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="rc-to">Effective to (optional)</Label>
              <Input
                id="rc-to"
                type="date"
                value={effectiveTo}
                onChange={(e) => setEffectiveTo(e.target.value)}
              />
              <FieldErrors errors={error?.fieldErrors} field="effectiveTo" />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="rc-notes">Notes (optional)</Label>
            <Textarea id="rc-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} />
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label>Entries</Label>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setEntries((prev) => [...prev, { ...EMPTY_ENTRY }])}
              >
                <Plus /> Add entry
              </Button>
            </div>
            {entries.map((entry, i) => (
              <div key={i} className="grid grid-cols-[1fr_5rem_5rem_6rem_auto] items-center gap-2">
                <Select
                  aria-label="Category"
                  value={entry.category}
                  onChange={(e) => patchEntry(i, { category: e.target.value })}
                >
                  {RATE_CARD_CATEGORIES.map((c) => (
                    <option key={c} value={c}>
                      {c}
                    </option>
                  ))}
                </Select>
                <Input
                  aria-label="Market"
                  placeholder="IN"
                  value={entry.market}
                  onChange={(e) => patchEntry(i, { market: e.target.value })}
                />
                <Input
                  aria-label="Volume tier"
                  placeholder="tier"
                  value={entry.volumeTier}
                  onChange={(e) => patchEntry(i, { volumeTier: e.target.value })}
                />
                <Input
                  aria-label="Price per message"
                  placeholder="0.7846"
                  inputMode="decimal"
                  value={entry.price}
                  onChange={(e) => patchEntry(i, { price: e.target.value })}
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label="Remove entry"
                  disabled={entries.length === 1}
                  onClick={() => setEntries((prev) => prev.filter((_, j) => j !== i))}
                >
                  <Trash2 />
                </Button>
              </div>
            ))}
            <FieldErrors errors={error?.fieldErrors} field="entries" />
          </div>

          {error && !error.fieldErrors ? (
            <p className="text-sm text-destructive">{error.message}</p>
          ) : null}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={
                upsert.isPending ||
                !name.trim() ||
                !effectiveFrom ||
                entries.some((e) => !e.market.trim() || e.price.trim() === "" || Number.isNaN(Number(e.price)))
              }
            >
              {upsert.isPending ? "Saving…" : card ? "Save changes" : "Create rate card"}
            </Button>
          </div>
        </form>
      </Dialog>
      {stepUpDialog}
    </>
  );
}
