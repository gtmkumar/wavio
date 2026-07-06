import { useMemo, useRef, useState, type FormEvent } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { Braces, Link2, MessageSquareReply, Phone, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import { useCreateTemplate } from "@/api/queries/templates";
import { usePhoneNumbers } from "@/api/queries/waba";
import { TEMPLATE_CATEGORIES } from "@/api/types";
import type { TemplateComponent } from "@/api/types";
import { FieldErrors } from "@/components/shared/states";
import {
  extractVariables,
  TemplatePreview,
  type TemplateContent,
} from "@/components/shared/template-preview";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Sheet } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";

export const Route = createFileRoute("/_app/templates/new")({
  component: NewTemplatePage,
});

/** Meta template names must be lowercase snake_case — normalize as the user types. */
function normalizeName(raw: string): string {
  return raw.toLowerCase().replace(/[^a-z0-9_]+/g, "_").replace(/_{2,}/g, "_");
}

const CATEGORY_HELP: Record<string, string> = {
  MARKETING: "Promotions, offers, product news",
  UTILITY: "Order updates, reminders, account alerts",
  AUTHENTICATION: "One-time passcodes only",
};

/** Common Meta language codes — free-text stays possible via the datalist input. */
const LANGUAGES = [
  "en", "en_US", "en_GB", "hi", "bn", "mr", "ta", "te", "gu", "kn", "ml", "pa", "ur",
  "es", "pt_BR", "fr", "de", "id", "ar",
];

type ButtonKind = "quick_reply" | "url" | "phone";

interface ButtonDraft {
  kind: ButtonKind;
  text: string;
  /** URL or phone number depending on kind. */
  value: string;
}

const BUTTON_KINDS: { kind: ButtonKind; label: string; icon: typeof Link2 }[] = [
  { kind: "quick_reply", label: "Quick reply", icon: MessageSquareReply },
  { kind: "url", label: "Visit website", icon: Link2 },
  { kind: "phone", label: "Call us", icon: Phone },
];

/** Compile a button draft to Meta's button object shape (goes into extrasJson). */
function toMetaButton(b: ButtonDraft): Record<string, string> {
  switch (b.kind) {
    case "url":
      return { type: "URL", text: b.text, url: b.value };
    case "phone":
      return { type: "PHONE_NUMBER", text: b.text, phone_number: b.value };
    default:
      return { type: "QUICK_REPLY", text: b.text };
  }
}

function NewTemplatePage() {
  const navigate = useNavigate();
  const phoneNumbers = usePhoneNumbers();
  const create = useCreateTemplate();
  const bodyRef = useRef<HTMLTextAreaElement>(null);

  const [businessAccountId, setBusinessAccountId] = useState("");
  const [name, setName] = useState("");
  const [language, setLanguage] = useState("en");
  const [category, setCategory] = useState(0);
  const [headerText, setHeaderText] = useState("");
  const [bodyText, setBodyText] = useState("");
  const [footerText, setFooterText] = useState("");
  const [buttons, setButtons] = useState<ButtonDraft[]>([]);
  const [samples, setSamples] = useState<Record<string, string>>({});
  const [error, setError] = useState<ApiError | null>(null);

  const close = () => void navigate({ to: "/templates" });

  // Business accounts aren't listed by any endpoint yet; derive the choices
  // from the tenant's phone numbers, which each carry their WABA id.
  const businessAccounts = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const p of phoneNumbers.data ?? []) {
      map.set(p.businessAccountId, [...(map.get(p.businessAccountId) ?? []), p.displayPhoneNumber]);
    }
    return [...map.entries()];
  }, [phoneNumbers.data]);

  const variables = useMemo(() => extractVariables(bodyText), [bodyText]);

  function insertVariable() {
    const next = variables.length === 0 ? 1 : Math.max(...variables.map(Number)) + 1;
    const token = `{{${next}}}`;
    const el = bodyRef.current;
    const at = el?.selectionStart ?? bodyText.length;
    setBodyText(bodyText.slice(0, at) + token + bodyText.slice(el?.selectionEnd ?? at));
    // Restore focus with the caret placed after the inserted token.
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(at + token.length, at + token.length);
    });
  }

  function patchButton(index: number, patch: Partial<ButtonDraft>) {
    setButtons((prev) => prev.map((b, i) => (i === index ? { ...b, ...patch } : b)));
  }

  // The exact payload the backend compiles for Meta — also feeds the technical view.
  const components = useMemo<TemplateComponent[]>(() => {
    const list: TemplateComponent[] = [];
    if (headerText.trim()) list.push({ type: "header", text: headerText.trim(), extrasJson: null });
    list.push({ type: "body", text: bodyText.trim(), extrasJson: null });
    if (footerText.trim()) list.push({ type: "footer", text: footerText.trim(), extrasJson: null });
    const valid = buttons.filter((b) => b.text.trim() && (b.kind === "quick_reply" || b.value.trim()));
    if (valid.length > 0)
      list.push({
        type: "buttons",
        text: null,
        extrasJson: JSON.stringify({ buttons: valid.map(toMetaButton) }),
      });
    return list;
  }, [headerText, bodyText, footerText, buttons]);

  const exampleValuesJson = useMemo(() => {
    const filled = variables.filter((v) => samples[v]?.trim());
    if (filled.length === 0) return null;
    return JSON.stringify(Object.fromEntries(filled.map((v) => [v, samples[v].trim()])));
  }, [variables, samples]);

  const preview: TemplateContent = {
    headerText: headerText.trim() || null,
    bodyText,
    footerText: footerText.trim() || null,
    buttons: buttons
      .filter((b) => b.text.trim())
      .map((b) => ({ type: toMetaButton(b).type, text: b.text })),
  };

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const result = await create.mutateAsync({
        businessAccountId,
        definition: { name: name.trim(), language: language.trim(), category, components },
        exampleValuesJson,
      });
      toast.success(
        result.submittedToMeta
          ? "Template created and submitted to Meta"
          : `Template created as draft${result.submissionError ? ` — submit failed: ${result.submissionError}` : ""}`,
      );
      await navigate({
        to: "/templates/$templateId",
        params: { templateId: result.template.id },
      });
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <Sheet
      open
      onClose={close}
      size="xl"
      title="New template"
      description="Design the message visually — it is compiled and submitted to Meta for review."
    >
      <form onSubmit={onSubmit} noValidate className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="waba">Business account</Label>
            <Select
              id="waba"
              value={businessAccountId}
              onChange={(e) => setBusinessAccountId(e.target.value)}
            >
              <option value="">
                {phoneNumbers.isPending ? "Loading…" : "Select a business account"}
              </option>
              {businessAccounts.map(([id, numbers]) => (
                <option key={id} value={id}>
                  {id.slice(0, 8)}… ({numbers.join(", ")})
                </option>
              ))}
            </Select>
            <FieldErrors errors={error?.fieldErrors} field="businessAccountId" />
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            <div className="space-y-1.5 sm:col-span-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(normalizeName(e.target.value))}
                placeholder="order_shipped"
                aria-invalid={!!error?.fieldErrors?.name}
              />
              <p className="text-xs text-muted-foreground">
                Lowercase with underscores — spaces are converted as you type.
              </p>
              <FieldErrors errors={error?.fieldErrors} field="name" />
              <FieldErrors errors={error?.fieldErrors} field="definition" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="language">Language</Label>
              <Input
                id="language"
                list="template-languages"
                value={language}
                onChange={(e) => setLanguage(e.target.value)}
                placeholder="en"
              />
              <datalist id="template-languages">
                {LANGUAGES.map((l) => (
                  <option key={l} value={l} />
                ))}
              </datalist>
              <FieldErrors errors={error?.fieldErrors} field="language" />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="category">Category</Label>
            <Select
              id="category"
              value={category}
              onChange={(e) => setCategory(Number(e.target.value))}
            >
              {TEMPLATE_CATEGORIES.map((c, i) => (
                <option key={c} value={i}>
                  {c.charAt(0) + c.slice(1).toLowerCase()} — {CATEGORY_HELP[c]}
                </option>
              ))}
            </Select>
            <FieldErrors errors={error?.fieldErrors} field="category" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="header">Header (optional)</Label>
            <Input
              id="header"
              value={headerText}
              onChange={(e) => setHeaderText(e.target.value)}
              maxLength={60}
              placeholder="Your order is on its way"
            />
          </div>
          <div className="space-y-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="body">Message</Label>
              <Button type="button" variant="outline" size="sm" onClick={insertVariable}>
                <Plus /> Insert variable
              </Button>
            </div>
            <Textarea
              id="body"
              ref={bodyRef}
              value={bodyText}
              onChange={(e) => setBodyText(e.target.value)}
              rows={5}
              placeholder="Hi {{1}}, your order {{2}} was shipped today."
              aria-invalid={!!error?.fieldErrors?.components}
            />
            <p className="text-xs text-muted-foreground">
              Variables like {"{{1}}"} are filled with a different value per recipient when you send.
            </p>
            <FieldErrors errors={error?.fieldErrors} field="components" />
          </div>
          {variables.length > 0 ? (
            <div className="space-y-2 rounded-md border bg-muted/40 p-3">
              <p className="text-sm font-medium">Sample values</p>
              <p className="text-xs text-muted-foreground">
                Shown in the preview and sent to Meta as examples — they speed up review.
              </p>
              <div className="grid gap-2 sm:grid-cols-2">
                {variables.map((v) => (
                  <div key={v} className="flex items-center gap-2">
                    <code className="shrink-0 rounded bg-muted px-1.5 py-0.5 font-mono text-xs">{`{{${v}}}`}</code>
                    <Input
                      aria-label={`Sample value for variable ${v}`}
                      value={samples[v] ?? ""}
                      onChange={(e) => setSamples((prev) => ({ ...prev, [v]: e.target.value }))}
                      placeholder={v === "1" ? "Priya" : "sample value"}
                    />
                  </div>
                ))}
              </div>
              <FieldErrors errors={error?.fieldErrors} field="exampleValuesJson" />
            </div>
          ) : null}
          <div className="space-y-1.5">
            <Label htmlFor="footer">Footer (optional)</Label>
            <Input
              id="footer"
              value={footerText}
              onChange={(e) => setFooterText(e.target.value)}
              maxLength={60}
              placeholder="Reply STOP to opt out"
            />
          </div>
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label>Buttons (optional)</Label>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={buttons.length >= 3}
                onClick={() => setButtons((prev) => [...prev, { kind: "quick_reply", text: "", value: "" }])}
              >
                <Plus /> Add button
              </Button>
            </div>
            {buttons.map((b, i) => (
              <div key={i} className="grid grid-cols-[9rem_1fr_auto] items-start gap-2">
                <Select
                  aria-label="Button type"
                  value={b.kind}
                  onChange={(e) => patchButton(i, { kind: e.target.value as ButtonKind, value: "" })}
                >
                  {BUTTON_KINDS.map((k) => (
                    <option key={k.kind} value={k.kind}>
                      {k.label}
                    </option>
                  ))}
                </Select>
                <div className="grid gap-2 sm:grid-cols-2">
                  <Input
                    aria-label="Button label"
                    value={b.text}
                    onChange={(e) => patchButton(i, { text: e.target.value })}
                    maxLength={25}
                    placeholder="Button label"
                  />
                  {b.kind !== "quick_reply" ? (
                    <Input
                      aria-label={b.kind === "url" ? "Button URL" : "Button phone number"}
                      value={b.value}
                      onChange={(e) => patchButton(i, { value: e.target.value })}
                      placeholder={b.kind === "url" ? "https://example.com/orders" : "+919876543210"}
                    />
                  ) : null}
                </div>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label="Remove button"
                  onClick={() => setButtons((prev) => prev.filter((_, j) => j !== i))}
                >
                  <Trash2 />
                </Button>
              </div>
            ))}
          </div>
          <details className="rounded-md border">
            <summary className="flex cursor-pointer items-center gap-1.5 px-3 py-2 text-sm text-muted-foreground">
              <Braces className="size-3.5" /> Technical view — JSON sent to the API
            </summary>
            <pre className="overflow-auto border-t bg-muted p-3 font-mono text-xs">
              {JSON.stringify({ components, exampleValuesJson }, null, 2)}
            </pre>
          </details>
          {error && !error.fieldErrors ? (
            <p className="text-sm text-destructive">{error.message}</p>
          ) : null}
          <div className="flex justify-end gap-2 border-t pt-4">
            <Button type="button" variant="ghost" onClick={close}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={create.isPending || !businessAccountId || !name || !bodyText.trim()}
            >
              {create.isPending ? "Creating…" : "Create template"}
            </Button>
          </div>
        </div>
        <div className="space-y-2 lg:sticky lg:top-0 lg:self-start">
          <p className="text-sm font-medium">Preview</p>
          <TemplatePreview content={preview} values={samples} />
          <p className="text-xs text-muted-foreground">
            How the message looks on the recipient's phone, using your sample values.
          </p>
        </div>
      </form>
    </Sheet>
  );
}
