import { useMemo, useState, type FormEvent } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import { useCreateTemplate } from "@/api/queries/templates";
import { usePhoneNumbers } from "@/api/queries/waba";
import { TEMPLATE_CATEGORIES } from "@/api/types";
import { FieldErrors } from "@/components/shared/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Sheet } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";

export const Route = createFileRoute("/_app/templates/new")({
  component: NewTemplatePage,
});

function NewTemplatePage() {
  const navigate = useNavigate();
  const phoneNumbers = usePhoneNumbers();
  const create = useCreateTemplate();

  const [businessAccountId, setBusinessAccountId] = useState("");
  const [name, setName] = useState("");
  const [language, setLanguage] = useState("en");
  const [category, setCategory] = useState(0);
  const [headerText, setHeaderText] = useState("");
  const [bodyText, setBodyText] = useState("");
  const [footerText, setFooterText] = useState("");
  const [exampleValuesJson, setExampleValuesJson] = useState("");
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

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const components = [
      headerText.trim() ? { type: "header", text: headerText.trim(), extrasJson: null } : null,
      { type: "body", text: bodyText.trim(), extrasJson: null },
      footerText.trim() ? { type: "footer", text: footerText.trim(), extrasJson: null } : null,
    ].filter((c) => c !== null);

    try {
      const result = await create.mutateAsync({
        businessAccountId,
        definition: { name: name.trim(), language: language.trim(), category, components },
        exampleValuesJson: exampleValuesJson.trim() || null,
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
      title="New template"
      description="Created as a draft, compiled to Meta's component format, and submitted for review. Name must be lowercase snake_case."
    >
      <form onSubmit={onSubmit} noValidate className="space-y-4">
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
              onChange={(e) => setName(e.target.value)}
              placeholder="order_shipped_v1"
              aria-invalid={!!error?.fieldErrors?.name}
            />
            <FieldErrors errors={error?.fieldErrors} field="name" />
            <FieldErrors errors={error?.fieldErrors} field="definition" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="language">Language</Label>
            <Input
              id="language"
              value={language}
              onChange={(e) => setLanguage(e.target.value)}
              placeholder="en"
            />
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
              <option key={c} value={i}>{c}</option>
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
            placeholder="Your order is on its way"
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="body">Body</Label>
          <Textarea
            id="body"
            value={bodyText}
            onChange={(e) => setBodyText(e.target.value)}
            rows={4}
            placeholder="Hi {{1}}, your order {{2}} was shipped today."
            aria-invalid={!!error?.fieldErrors?.components}
          />
          <p className="text-xs text-muted-foreground">
            Body text supports {"{{1}}"} placeholders.
          </p>
          <FieldErrors errors={error?.fieldErrors} field="components" />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="footer">Footer (optional)</Label>
          <Input
            id="footer"
            value={footerText}
            onChange={(e) => setFooterText(e.target.value)}
            placeholder="Reply STOP to opt out"
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="examples">Example values (JSON, optional)</Label>
          <Textarea
            id="examples"
            value={exampleValuesJson}
            onChange={(e) => setExampleValuesJson(e.target.value)}
            placeholder='{"1": "Priya", "2": "#8123"}'
            className="font-mono text-xs"
          />
          <FieldErrors errors={error?.fieldErrors} field="exampleValuesJson" />
        </div>
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
      </form>
    </Sheet>
  );
}
