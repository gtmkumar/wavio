import { useMemo, useState, type FormEvent } from "react";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import { useCreateCampaign } from "@/api/queries/campaigns";
import { useTemplates } from "@/api/queries/templates";
import { usePhoneNumbers } from "@/api/queries/waba";
import { PageHeader } from "@/components/shared/page-header";
import { FieldErrors } from "@/components/shared/states";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";

export const Route = createFileRoute("/_app/campaigns/new")({
  component: NewCampaignPage,
});

/**
 * Audience textarea format: one recipient per line —
 *   <waId>            use the campaign-level params for this recipient
 *   <waId> | {json}   per-recipient template params override
 */
function parseAudience(raw: string): { waId: string; paramsJson: string | null }[] {
  return raw
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const sep = line.indexOf("|");
      if (sep === -1) return { waId: line, paramsJson: null };
      return {
        waId: line.slice(0, sep).trim(),
        paramsJson: line.slice(sep + 1).trim() || null,
      };
    });
}

function NewCampaignPage() {
  const navigate = useNavigate();
  const phoneNumbers = usePhoneNumbers();
  // Only APPROVED templates with a current version can be broadcast.
  const templates = useTemplates({ status: "APPROVED", pageSize: 100 });
  const create = useCreateCampaign();

  const [name, setName] = useState("");
  const [phoneNumberId, setPhoneNumberId] = useState("");
  const [templateId, setTemplateId] = useState("");
  const [paramsJson, setParamsJson] = useState("");
  const [audienceRaw, setAudienceRaw] = useState("");
  const [scheduledAt, setScheduledAt] = useState("");
  const [country, setCountry] = useState("IN");
  const [error, setError] = useState<ApiError | null>(null);

  const approvedTemplates = useMemo(
    () => (templates.data?.list ?? []).filter((t) => t.currentVersionId != null),
    [templates.data],
  );
  const audience = useMemo(() => parseAudience(audienceRaw), [audienceRaw]);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const template = approvedTemplates.find((t) => t.id === templateId);
    if (!template?.currentVersionId) {
      setError(new ApiError(0, "Pick an approved template."));
      return;
    }
    try {
      const campaign = await create.mutateAsync({
        name,
        phoneNumberId,
        templateVersionId: template.currentVersionId,
        paramsJson: paramsJson.trim() || null,
        audience,
        scheduledAt: scheduledAt ? new Date(scheduledAt).toISOString() : null,
        country: country.trim() || null,
      });
      toast.success(`Campaign "${campaign.name}" created`);
      await navigate({ to: "/campaigns/$campaignId", params: { campaignId: campaign.id } });
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Could not reach the server."));
    }
  }

  return (
    <>
      <PageHeader
        title="New campaign"
        description="Create a draft broadcast; you launch it explicitly from the campaign page."
      />
      <form onSubmit={onSubmit} noValidate>
        <Card className="max-w-2xl">
          <CardHeader>
            <CardTitle className="text-base">Campaign details</CardTitle>
            <CardDescription>
              The pre-launch spend estimate uses the selected country's rate card.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="July product launch"
                aria-invalid={!!error?.fieldErrors?.name}
              />
              <FieldErrors errors={error?.fieldErrors} field="name" />
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label htmlFor="phone">Send from</Label>
                <Select
                  id="phone"
                  value={phoneNumberId}
                  onChange={(e) => setPhoneNumberId(e.target.value)}
                >
                  <option value="">
                    {phoneNumbers.isPending ? "Loading numbers…" : "Select a phone number"}
                  </option>
                  {(phoneNumbers.data ?? []).map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.displayPhoneNumber} ({p.status})
                    </option>
                  ))}
                </Select>
                <FieldErrors errors={error?.fieldErrors} field="phoneNumberId" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="template">Template</Label>
                <Select
                  id="template"
                  value={templateId}
                  onChange={(e) => setTemplateId(e.target.value)}
                >
                  <option value="">
                    {templates.isPending ? "Loading templates…" : "Select an approved template"}
                  </option>
                  {approvedTemplates.map((t) => (
                    <option key={t.id} value={t.id}>
                      {/* List projection omits currentVersion content — show it only when present. */}
                      {t.name} · {t.language}
                      {t.currentVersion ? ` · v${t.currentVersion.versionNumber}` : ""}
                    </option>
                  ))}
                </Select>
                <FieldErrors errors={error?.fieldErrors} field="templateVersionId" />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="params">Default template params (JSON, optional)</Label>
              <Textarea
                id="params"
                value={paramsJson}
                onChange={(e) => setParamsJson(e.target.value)}
                placeholder='{"1": "Hello", "2": "20% off"}'
                className="font-mono text-xs"
                aria-invalid={!!error?.fieldErrors?.paramsJson}
              />
              <FieldErrors errors={error?.fieldErrors} field="paramsJson" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="audience">Audience — one WhatsApp ID per line</Label>
              <Textarea
                id="audience"
                value={audienceRaw}
                onChange={(e) => setAudienceRaw(e.target.value)}
                rows={6}
                placeholder={"919876543210\n919812345678 | {\"1\": \"Priya\"}"}
                className="font-mono text-xs"
                aria-invalid={!!error?.fieldErrors?.audience}
              />
              <p className="text-xs text-muted-foreground">
                {audience.length} recipient{audience.length === 1 ? "" : "s"} — append{" "}
                <code>| {"{...}"}</code> for a per-recipient params override.
              </p>
              <FieldErrors errors={error?.fieldErrors} field="audience" />
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label htmlFor="scheduledAt">Schedule (optional)</Label>
                <Input
                  id="scheduledAt"
                  type="datetime-local"
                  value={scheduledAt}
                  onChange={(e) => setScheduledAt(e.target.value)}
                />
                <FieldErrors errors={error?.fieldErrors} field="scheduledAt" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="country">Country (rate card)</Label>
                <Input
                  id="country"
                  value={country}
                  onChange={(e) => setCountry(e.target.value)}
                  maxLength={2}
                />
                <FieldErrors errors={error?.fieldErrors} field="country" />
              </div>
            </div>
            {error && !error.fieldErrors ? (
              <p className="text-sm text-destructive">{error.message}</p>
            ) : null}
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => void navigate({ to: "/campaigns" })}>
                Cancel
              </Button>
              <Button type="submit" disabled={create.isPending || !name || !phoneNumberId || !templateId || audience.length === 0}>
                {create.isPending ? "Creating…" : "Create draft"}
              </Button>
            </div>
          </CardContent>
        </Card>
      </form>
    </>
  );
}
