import { useEffect, useState } from "react";
import { createFileRoute } from "@tanstack/react-router";
import {
  ArrowRight,
  BadgeCheck,
  CheckCircle2,
  Circle,
  CircleAlert,
  Clock3,
  Link2,
  Phone,
  RefreshCw,
  Store,
} from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/api/http";
import {
  useBusinessProfile,
  useEmbeddedSignup,
  useOnboardingStatus,
  useRefreshOnboarding,
  useRegisterPhone,
  useRequestVerificationCode,
  useUpdateBusinessProfile,
  useVerifyCode,
} from "@/api/queries/onboarding";
import type { OnboardingCheck, OnboardingPhone, OnboardingStatus } from "@/api/types";
import { useAuth } from "@/auth/AuthContext";
import { useStepUpGuard } from "@/auth/StepUpDialog";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { FieldErrors } from "@/components/shared/states";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/_app/onboarding")({
  component: OnboardingPage,
});

// Full-page stepper, deliberately NOT a slide-over: onboarding is a journey, not an edit
// (docs/ONBOARDING_WIZARD_PLAN.md). Every step derives its state from GET /status, so closing
// the browser mid-flow loses nothing.

const STEPS = [
  { key: "connect", label: "Connect", icon: Link2 },
  { key: "number", label: "Number", icon: Phone },
  { key: "profile", label: "Profile", icon: Store },
  { key: "checks", label: "Checks", icon: BadgeCheck },
] as const;

/** Where the data says the user is — the first step with outstanding work. */
function deriveStep(status: OnboardingStatus | undefined): number {
  if (!status?.connected) return 0;
  const phone = status.phoneNumbers[0];
  if (!phone || phone.codeVerificationStatus !== "VERIFIED" || !phone.registeredAt) return 1;
  if (!phone.profileSet) return 2;
  return 3;
}

function OnboardingPage() {
  const { data: status, isPending, error } = useOnboardingStatus();
  const [visited, setVisited] = useState<number | null>(null);
  const derived = deriveStep(status);
  // The user may look back at completed steps, but can't jump ahead of the data.
  const current = visited === null ? derived : Math.min(visited, derived);
  const phone = status?.phoneNumbers[0] ?? null;

  if (isPending) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }
  if (error) {
    return (
      <p className="text-sm text-destructive">
        Could not load onboarding status: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold">WhatsApp onboarding</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Everything Meta needs, one step at a time — no Business Manager digging required.
        </p>
      </div>

      <nav aria-label="Onboarding steps" className="flex items-center gap-2">
        {STEPS.map((step, i) => {
          const done = i < derived;
          const active = i === current;
          const reachable = i <= derived;
          return (
            <div key={step.key} className="flex items-center gap-2">
              {i > 0 && <div className={cn("h-px w-6 sm:w-10", done || active ? "bg-primary" : "bg-border")} />}
              <button
                type="button"
                disabled={!reachable}
                onClick={() => setVisited(i)}
                className={cn(
                  "flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm transition-colors",
                  active && "border-primary bg-primary text-primary-foreground",
                  !active && done && "border-primary/40 text-primary",
                  !active && !done && "text-muted-foreground",
                  reachable && !active && "hover:bg-accent",
                  !reachable && "cursor-not-allowed opacity-60",
                )}
              >
                {done ? <CheckCircle2 className="size-4" /> : <step.icon className="size-4" />}
                {step.label}
              </button>
            </div>
          );
        })}
      </nav>

      {current === 0 && <ConnectStep />}
      {current === 1 && phone && <NumberStep phone={phone} onDone={() => setVisited(2)} />}
      {current === 1 && !phone && (
        <p className="text-sm text-muted-foreground">
          No phone number arrived with the connected account yet — try Refresh on the Checks step.
        </p>
      )}
      {current === 2 && phone && <ProfileStep phone={phone} onDone={() => setVisited(3)} />}
      {current === 3 && status && <ChecksStep status={status} />}
    </div>
  );
}

// ── Step 1: Connect ──────────────────────────────────────────────────────────

function ConnectStep() {
  const { user, tenantOverride } = useAuth();
  const { guard, stepUpDialog } = useStepUpGuard();
  const signup = useEmbeddedSignup();

  // Stub mode: a deterministic per-tenant code stands in for the Embedded Signup popup's
  // real authorization code, so re-connecting upserts the same simulated WABA instead of
  // minting a new one. Real mode later: the FB JS SDK popup supplies code + sessionInfo,
  // and the Facebook login happens inside Meta's popup — credentials never touch Wavio.
  const tenantId = tenantOverride ?? user?.tenantId ?? "no-tenant";

  async function connect() {
    try {
      await guard(() =>
        signup.mutateAsync({ code: `SIMCODE-${tenantId}`, wabaId: null, phoneNumberId: null }),
      );
      toast.success("WhatsApp Business Account connected.");
    } catch (err) {
      if (err instanceof ApiError) toast.error(err.message);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Connect your WhatsApp Business Account</CardTitle>
        <CardDescription>
          This opens Meta&apos;s sign-in window where you approve access — your Facebook password
          stays with Meta, never with Wavio. Afterwards we save your account, find your phone
          number, and switch on message notifications automatically.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <Button onClick={() => void connect()} disabled={signup.isPending}>
          <Link2 />
          {signup.isPending ? "Connecting…" : "Connect WhatsApp (simulated)"}
        </Button>
        <p className="text-xs text-muted-foreground">
          Development mode: the Meta popup is simulated against the local stub. Once the real Meta
          app is approved, this button opens the genuine Embedded Signup window — nothing else
          about the flow changes.
        </p>
        {signup.error instanceof ApiError && !signup.error.isStepUpRequired && (
          <FieldErrors errors={signup.error.fieldErrors} field="code" />
        )}
      </CardContent>
      {stepUpDialog}
    </Card>
  );
}

// ── Step 2: Number (OTP verify + two-step pin registration) ─────────────────

function NumberStep({ phone, onDone }: { phone: OnboardingPhone; onDone: () => void }) {
  const { guard, stepUpDialog } = useStepUpGuard();
  const requestCode = useRequestVerificationCode();
  const verifyCode = useVerifyCode();
  const register = useRegisterPhone();

  const [codeMethod, setCodeMethod] = useState("SMS");
  const [otp, setOtp] = useState("");
  const [pin, setPin] = useState("");
  const [codeSent, setCodeSent] = useState(false);

  const verified = phone.codeVerificationStatus === "VERIFIED";
  const registered = phone.registeredAt !== null;

  async function run(fn: () => Promise<unknown>, successMessage: string) {
    try {
      await guard(fn);
      toast.success(successMessage);
    } catch (err) {
      if (err instanceof ApiError) toast.error(err.message);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Set up {phone.displayPhoneNumber}</CardTitle>
        <CardDescription>
          Two quick things: prove you own this number (one-time code), then protect it with a
          6-digit PIN and activate it for the WhatsApp API.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <section className="space-y-3">
          <h3 className="flex items-center gap-2 text-sm font-medium">
            {verified ? <CheckCircle2 className="size-4 text-emerald-600" /> : <Circle className="size-4 text-muted-foreground" />}
            1. Verify ownership
          </h3>
          {verified ? (
            <p className="text-sm text-muted-foreground">This number is verified — nothing to do here.</p>
          ) : (
            <div className="space-y-3">
              <div className="flex flex-wrap items-end gap-2">
                <div className="space-y-1.5">
                  <Label htmlFor="code-method">Send the code by</Label>
                  <Select
                    id="code-method"
                    value={codeMethod}
                    onChange={(e) => setCodeMethod(e.target.value)}
                    className="w-36"
                  >
                    <option value="SMS">Text message</option>
                    <option value="VOICE">Phone call</option>
                  </Select>
                </div>
                <Button
                  variant="outline"
                  disabled={requestCode.isPending}
                  onClick={() =>
                    void run(async () => {
                      await requestCode.mutateAsync({ phoneId: phone.id, codeMethod });
                      setCodeSent(true);
                    }, "Verification code sent.")
                  }
                >
                  {codeSent ? "Send again" : "Send code"}
                </Button>
              </div>
              {codeSent && (
                <div className="flex flex-wrap items-end gap-2">
                  <div className="space-y-1.5">
                    <Label htmlFor="otp">Code you received</Label>
                    <Input
                      id="otp"
                      inputMode="numeric"
                      placeholder="000000"
                      className="w-36"
                      value={otp}
                      onChange={(e) => setOtp(e.target.value)}
                      aria-invalid={!!(verifyCode.error instanceof ApiError && verifyCode.error.fieldErrors?.code)}
                    />
                  </div>
                  <Button
                    disabled={verifyCode.isPending || otp.trim().length === 0}
                    onClick={() =>
                      void run(
                        () => verifyCode.mutateAsync({ phoneId: phone.id, code: otp.trim() }),
                        "Number verified.",
                      )
                    }
                  >
                    Verify
                  </Button>
                </div>
              )}
              {verifyCode.error instanceof ApiError && (
                <FieldErrors errors={verifyCode.error.fieldErrors} field="code" />
              )}
            </div>
          )}
        </section>

        <section className="space-y-3">
          <h3 className="flex items-center gap-2 text-sm font-medium">
            {registered ? <CheckCircle2 className="size-4 text-emerald-600" /> : <Circle className="size-4 text-muted-foreground" />}
            2. Activate with a security PIN
          </h3>
          {registered ? (
            <p className="text-sm text-muted-foreground">
              Activated — this number can send and receive through the API.
            </p>
          ) : (
            <div className="space-y-3">
              <p className="text-sm text-muted-foreground">
                Pick a 6-digit PIN. Meta asks for it if this number is ever moved to another
                system, so note it somewhere safe.
              </p>
              <div className="flex flex-wrap items-end gap-2">
                <div className="space-y-1.5">
                  <Label htmlFor="pin">6-digit PIN</Label>
                  <Input
                    id="pin"
                    inputMode="numeric"
                    placeholder="••••••"
                    className="w-36"
                    value={pin}
                    onChange={(e) => setPin(e.target.value)}
                    aria-invalid={!!(register.error instanceof ApiError && register.error.fieldErrors?.pin)}
                  />
                </div>
                <Button
                  disabled={register.isPending || !verified || pin.trim().length === 0}
                  onClick={() =>
                    void run(
                      () => register.mutateAsync({ phoneId: phone.id, pin: pin.trim() }),
                      "Number activated.",
                    )
                  }
                >
                  Activate number
                </Button>
              </div>
              {!verified && (
                <p className="text-xs text-muted-foreground">Verify ownership first — then this unlocks.</p>
              )}
              {register.error instanceof ApiError && (
                <FieldErrors errors={register.error.fieldErrors} field="pin" />
              )}
            </div>
          )}
        </section>

        {verified && registered && (
          <Button onClick={onDone}>
            Continue to profile <ArrowRight />
          </Button>
        )}
      </CardContent>
      {stepUpDialog}
    </Card>
  );
}

// ── Step 3: Business profile ─────────────────────────────────────────────────

const VERTICALS = [
  ["", "Choose a category…"],
  ["RETAIL", "Retail & shopping"],
  ["RESTAURANT", "Restaurant / food"],
  ["HEALTH", "Health & medical"],
  ["EDU", "Education"],
  ["FINANCE", "Finance"],
  ["TRAVEL", "Travel & transport"],
  ["BEAUTY", "Beauty & personal care"],
  ["PROF_SERVICES", "Professional services"],
  ["OTHER", "Other"],
] as const;

function ProfileStep({ phone, onDone }: { phone: OnboardingPhone; onDone: () => void }) {
  const { guard, stepUpDialog } = useStepUpGuard();
  const { data: profile, isPending } = useBusinessProfile(phone.id);
  const save = useUpdateBusinessProfile();

  const [about, setAbout] = useState("");
  const [description, setDescription] = useState("");
  const [address, setAddress] = useState("");
  const [email, setEmail] = useState("");
  const [website, setWebsite] = useState("");
  const [vertical, setVertical] = useState("");
  const [loaded, setLoaded] = useState(false);

  // Seed the form from the live profile exactly once (Meta may already have values).
  useEffect(() => {
    if (profile && !loaded) {
      setAbout(profile.about ?? "");
      setDescription(profile.description ?? "");
      setAddress(profile.address ?? "");
      setEmail(profile.email ?? "");
      setWebsite(profile.websites[0] ?? "");
      setVertical(profile.vertical ?? "");
      setLoaded(true);
    }
  }, [profile, loaded]);

  const fieldErrors = save.error instanceof ApiError ? save.error.fieldErrors : null;

  async function submit() {
    try {
      await guard(() =>
        save.mutateAsync({
          phoneId: phone.id,
          request: {
            about: about.trim() || null,
            description: description.trim() || null,
            address: address.trim() || null,
            email: email.trim() || null,
            websites: website.trim() ? [website.trim()] : null,
            vertical: vertical || null,
            profilePictureUrl: null,
          },
        }),
      );
      toast.success("Business profile saved to WhatsApp.");
      onDone();
    } catch (err) {
      if (err instanceof ApiError) toast.error(err.message);
    }
  }

  if (isPending) return <Skeleton className="h-72 w-full" />;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tell customers who you are</CardTitle>
        <CardDescription>
          This is the public profile people see when they open your chat in WhatsApp
          ({phone.displayPhoneNumber}). Everything here is optional and editable later.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="about">Short tagline</Label>
            <Input
              id="about"
              maxLength={139}
              placeholder="e.g. Fresh groceries, delivered in 30 minutes"
              value={about}
              onChange={(e) => setAbout(e.target.value)}
            />
            <FieldErrors errors={fieldErrors} field="about" />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="description">About your business</Label>
            <Textarea
              id="description"
              maxLength={512}
              rows={3}
              placeholder="What you do, opening hours, anything customers should know."
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
            <FieldErrors errors={fieldErrors} field="description" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="email">Contact email</Label>
            <Input
              id="email"
              type="email"
              placeholder="hello@yourbusiness.in"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-invalid={!!fieldErrors?.email}
            />
            <FieldErrors errors={fieldErrors} field="email" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="website">Website</Label>
            <Input
              id="website"
              placeholder="https://yourbusiness.in"
              value={website}
              onChange={(e) => setWebsite(e.target.value)}
            />
            <FieldErrors errors={fieldErrors} field="websites" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="address">Address</Label>
            <Input
              id="address"
              maxLength={256}
              placeholder="Shop 12, MG Road, Bengaluru"
              value={address}
              onChange={(e) => setAddress(e.target.value)}
            />
            <FieldErrors errors={fieldErrors} field="address" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="vertical">Business category</Label>
            <Select id="vertical" value={vertical} onChange={(e) => setVertical(e.target.value)}>
              {VERTICALS.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </Select>
          </div>
        </div>
        <Button onClick={() => void submit()} disabled={save.isPending}>
          {save.isPending ? "Saving…" : "Save profile"}
        </Button>
      </CardContent>
      {stepUpDialog}
    </Card>
  );
}

// ── Step 4: Checks ───────────────────────────────────────────────────────────

const CHECK_COPY: Record<string, { title: string; explain: Partial<Record<string, string>> }> = {
  connected: {
    title: "WhatsApp account connected",
    explain: { todo: "Run the Connect step to link your WhatsApp Business Account." },
  },
  webhooks: {
    title: "Message notifications switched on",
    explain: { todo: "Re-run Connect — subscribing to notifications failed last time." },
  },
  number_verified: {
    title: "Phone number ownership verified",
    explain: { todo: "Go to the Number step and verify with the one-time code." },
  },
  number_registered: {
    title: "Number activated for the API",
    explain: { todo: "Go to the Number step and activate with your 6-digit PIN." },
  },
  profile: {
    title: "Business profile filled in",
    explain: { todo: "Add your public profile in the Profile step." },
  },
  name_review: {
    title: "Display name approved by Meta",
    explain: {
      waiting: "Meta is reviewing your display name — usually minutes to a few hours. Nothing to do.",
      attention: "Meta declined the display name. Pick a name that matches your business and re-submit.",
      todo: "The name review starts automatically once the number is activated.",
    },
  },
  business_verification: {
    title: "Business verified by Meta",
    explain: {
      waiting: "Meta is reviewing your business documents — this can take a few days. Nothing to do.",
      attention: "Meta needs more from you — check Business Manager's Security Centre for what to upload.",
      todo: "Business verification starts from Meta Business Manager (one-time).",
    },
  },
  quality: {
    title: "Number quality healthy",
    explain: {
      attention: "Meta flagged message quality — slow down sends and review what customers are blocking.",
      waiting: "Quality rating appears after your first messages.",
      todo: "Quality rating appears once the number is activated.",
    },
  },
};

function CheckIcon({ state }: { state: string }) {
  if (state === "done") return <CheckCircle2 className="size-5 shrink-0 text-emerald-600" />;
  if (state === "waiting") return <Clock3 className="size-5 shrink-0 text-amber-500" />;
  if (state === "attention") return <CircleAlert className="size-5 shrink-0 text-destructive" />;
  return <Circle className="size-5 shrink-0 text-muted-foreground" />;
}

function ChecksStep({ status }: { status: OnboardingStatus }) {
  const refresh = useRefreshOnboarding();
  const allDone = status.checks.every((c) => c.state === "done");
  const anyWaiting = status.checks.some((c) => c.state === "waiting");

  // While Meta reviews are pending, quietly re-pull from the Graph API so the ambers flip to
  // green without the user hammering the button (15s is far above the stub's advance windows
  // and harmless against real Meta).
  const refreshMutate = refresh.mutate;
  useEffect(() => {
    if (!anyWaiting) return;
    const id = setInterval(() => refreshMutate(), 15_000);
    return () => clearInterval(id);
  }, [anyWaiting, refreshMutate]);

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4">
        <div>
          <CardTitle>{allDone ? "You're ready to send 🎉" : "Almost there"}</CardTitle>
          <CardDescription>
            {allDone
              ? "Every check is green — head to Templates or Campaigns to send your first message."
              : "Green is done, amber means Meta is still reviewing (no action needed), red needs you."}
          </CardDescription>
        </div>
        <Button variant="outline" size="sm" disabled={refresh.isPending} onClick={() => refresh.mutate()}>
          <RefreshCw className={cn(refresh.isPending && "animate-spin")} />
          Refresh from Meta
        </Button>
      </CardHeader>
      <CardContent>
        <ul className="divide-y">
          {status.checks.map((check: OnboardingCheck) => {
            const copy = CHECK_COPY[check.key];
            const explain = copy?.explain[check.state];
            return (
              <li key={check.key} className="flex items-start gap-3 py-3">
                <CheckIcon state={check.state} />
                <div className="min-w-0">
                  <p className="text-sm font-medium">{copy?.title ?? check.key}</p>
                  {check.state !== "done" && explain && (
                    <p className="text-xs text-muted-foreground">{explain}</p>
                  )}
                  {check.detail && (
                    <p className="mt-0.5 text-[11px] text-muted-foreground/70">Meta says: {check.detail}</p>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      </CardContent>
    </Card>
  );
}
