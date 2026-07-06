import type { ReactNode } from "react";
import { Phone, Reply, SquareArrowOutUpRight } from "lucide-react";
import { cn } from "@/lib/utils";

/** One button of a BUTTONS component (Meta shape: QUICK_REPLY | URL | PHONE_NUMBER). */
export interface TemplateButton {
  type: string;
  text: string;
}

/** The renderable content of a template, independent of where it came from. */
export interface TemplateContent {
  headerText: string | null;
  bodyText: string;
  footerText: string | null;
  buttons: TemplateButton[];
}

/** Unique {{n}} placeholders in first-appearance order (returns the bare numbers). */
export function extractVariables(text: string): string[] {
  const seen = new Set<string>();
  for (const match of text.matchAll(/\{\{(\d+)\}\}/g)) seen.add(match[1]);
  return [...seen];
}

/** Parse the compiled Meta components JSON stored on a template version (uppercase types). */
export function parseComponentsJson(raw: string | null): TemplateContent | null {
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return null;
    const content: TemplateContent = { headerText: null, bodyText: "", footerText: null, buttons: [] };
    for (const item of parsed) {
      const c = item as { type?: string; text?: string; buttons?: { type?: string; text?: string }[] };
      switch (c.type?.toUpperCase()) {
        case "HEADER":
          content.headerText = c.text ?? null;
          break;
        case "BODY":
          content.bodyText = c.text ?? "";
          break;
        case "FOOTER":
          content.footerText = c.text ?? null;
          break;
        case "BUTTONS":
          content.buttons = (c.buttons ?? []).map((b) => ({ type: b.type ?? "", text: b.text ?? "" }));
          break;
      }
    }
    return content;
  } catch {
    return null;
  }
}

/** Parse an exampleValuesJson / paramsJson object string into a plain string map. */
export function parseValuesJson(raw: string | null): Record<string, string> {
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return {};
    return Object.fromEntries(Object.entries(parsed).map(([k, v]) => [k, String(v)]));
  } catch {
    return {};
  }
}

/** Substitute {{n}} tokens: filled values render as text, missing ones as amber chips. */
function renderWithValues(text: string, values: Record<string, string>): ReactNode {
  return text.split(/(\{\{\d+\}\})/g).map((part, i) => {
    const match = /^\{\{(\d+)\}\}$/.exec(part);
    if (!match) return part;
    const value = values[match[1]]?.trim();
    return value ? (
      <span key={i}>{value}</span>
    ) : (
      <span
        key={i}
        className="mx-0.5 rounded bg-amber-200 px-1 font-mono text-[0.8em] text-amber-900"
        title={`Variable ${match[1]} — no value yet`}
      >
        {part}
      </span>
    );
  });
}

function buttonIcon(type: string) {
  switch (type.toUpperCase()) {
    case "URL":
      return <SquareArrowOutUpRight className="size-3.5" />;
    case "PHONE_NUMBER":
      return <Phone className="size-3.5" />;
    default:
      return <Reply className="size-3.5" />;
  }
}

/**
 * WhatsApp-style message bubble preview. Deliberately keeps WhatsApp's real light palette
 * in both app themes — it previews the recipient's phone, not the console.
 */
export function TemplatePreview({
  content,
  values = {},
  className,
}: {
  content: TemplateContent;
  values?: Record<string, string>;
  className?: string;
}) {
  const empty = !content.headerText && !content.bodyText.trim() && !content.footerText && content.buttons.length === 0;
  return (
    <div className={cn("rounded-xl border bg-[#e5ddd5] p-4 dark:border-transparent", className)}>
      {empty ? (
        <p className="py-6 text-center text-sm text-[#54656f]">
          Your message preview appears here as you type.
        </p>
      ) : (
        <div className="max-w-full rounded-lg bg-white shadow-sm">
          <div className="space-y-1 px-3 pt-2 pb-1">
            {content.headerText ? (
              <p className="text-[15px] font-semibold break-words text-gray-900">
                {renderWithValues(content.headerText, values)}
              </p>
            ) : null}
            {content.bodyText.trim() ? (
              <p className="text-sm break-words whitespace-pre-wrap text-gray-800">
                {renderWithValues(content.bodyText, values)}
              </p>
            ) : null}
            {content.footerText ? (
              <p className="text-xs break-words text-gray-500">{content.footerText}</p>
            ) : null}
            <p className="text-right text-[10px] text-gray-400">09:41</p>
          </div>
          {content.buttons.length > 0 ? (
            <div className="divide-y divide-gray-100 border-t border-gray-100">
              {content.buttons.map((b, i) => (
                <div
                  key={i}
                  className="flex items-center justify-center gap-1.5 py-2.5 text-sm font-medium text-[#00a5f4]"
                >
                  {buttonIcon(b.type)}
                  {b.text || "Button"}
                </div>
              ))}
            </div>
          ) : null}
        </div>
      )}
    </div>
  );
}
