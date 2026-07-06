/**
 * Response contracts for services whose OpenAPI documents don't type their
 * response bodies (minimal APIs without Produces<T>). Each type mirrors the
 * C# record it deserializes — the source file is cited so drift is auditable.
 *
 * core / WaAdmin / WaBilling wrap responses in the { status, message, data }
 * envelope; WaGateway and WaIntel return the DTO raw.
 */

// ── Envelope (wavio.Utilities Response/Message/SingleResponse/…) ─────────────

export interface ApiMessage {
  errorTypeCode?: number;
  errorMessage?: Record<string, string[]> | null;
  responseMessage?: string | null;
}

export interface Envelope<T> {
  status: boolean;
  message: ApiMessage | null;
  data: T | null;
}

export interface PaginatedList<T> {
  list: T[];
  pageNumber: number;
  pageCount: number;
  totalCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

// ── Identity (core.Application — envelope-wrapped) ───────────────────────────

export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresInSeconds: number | string;
  tokenType?: string;
}

// ── Messaging / WaGateway (raw DTOs) ─────────────────────────────────────────

// WaGateway.Application/Messages/Dtos/SendMessageResultDto.cs
export interface SendMessageResult {
  id: string;
  status: "accepted" | "rejected" | string;
  wamid: string | null;
  billableEstimate: boolean | null;
  errorCode: string | null;
  errorMessage: string | null;
}

// WaGateway.Application/Campaigns/Dtos/CampaignDtos.cs
export interface CampaignListItem {
  id: string;
  name: string;
  status: string;
  audienceCount: number;
  sentCount: number;
  deliveredCount: number;
  readCount: number;
  failedCount: number;
  createdAt: string;
}

export interface Campaign {
  id: string;
  name: string;
  phoneNumberId: string;
  templateVersionId: string;
  status: string;
  scheduledAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  audienceCount: number;
  suppressedCount: number;
  sentCount: number;
  deliveredCount: number;
  readCount: number;
  failedCount: number;
  projectedCost: number | null;
  projectedCurrency: string | null;
  failureBreakdown: Record<string, number> | null;
}

export interface CampaignAudienceMember {
  waId: string;
  paramsJson: string | null;
}

export interface CreateCampaignRequest {
  name: string;
  phoneNumberId: string;
  templateVersionId: string;
  paramsJson: string | null;
  audience: CampaignAudienceMember[];
  scheduledAt: string | null;
  country: string | null;
}

export interface SendMessageRequest {
  phoneNumberId: string;
  toWaId: string;
  messageType: string;
  payload: unknown;
}

// ── Templates / WaAdmin (envelope-wrapped) ───────────────────────────────────

// WaAdmin.Application/Templates/Dtos/TemplateDtos.cs
export interface TemplateVersion {
  id: string;
  versionNumber: number;
  componentsJson: string;
  exampleValuesJson: string | null;
  status: string;
  rejectionReason: string | null;
  submittedAt: string | null;
  reviewedAt: string | null;
  createdAt: string;
}

export interface Template {
  id: string;
  businessAccountId: string;
  name: string;
  language: string;
  category: string;
  metaTemplateId: string | null;
  status: string;
  currentVersionId: string | null;
  pausedUntil: string | null;
  pauseCount: number;
  createdAt: string;
  updatedAt: string;
  currentVersion: TemplateVersion | null;
}

export interface TemplateStatusEvent {
  id: string;
  oldStatus: string | null;
  newStatus: string;
  reason: string | null;
  occurredAt: string;
}

export interface TemplateStatus {
  templateId: string;
  status: string;
  metaTemplateId: string | null;
  pausedUntil: string | null;
  pauseCount: number;
  history: TemplateStatusEvent[];
}

export interface CreateTemplateResult {
  template: Template;
  submittedToMeta: boolean;
  submissionError: string | null;
}

// WaPlatform.Contracts TemplateDsl (request-side, mirrored by admin OpenAPI)
export interface TemplateComponent {
  type: string;
  text: string | null;
  extrasJson: string | null;
}

/** Category enum order per WaAdmin OpenAPI TemplateCategory (integer). */
export const TEMPLATE_CATEGORIES = ["MARKETING", "UTILITY", "AUTHENTICATION"] as const;

export interface TemplateDefinition {
  name: string;
  language: string;
  category: number;
  components: TemplateComponent[];
}

export interface CreateTemplateRequest {
  businessAccountId: string;
  definition: TemplateDefinition;
  exampleValuesJson: string | null;
}

export interface UpdateTemplateRequest {
  definition: TemplateDefinition;
  exampleValuesJson: string | null;
}

// ── Waba (WaAdmin, envelope-wrapped) ─────────────────────────────────────────

// wavio.SharedDataModel/Entities/Waba/WabaPhoneNumber.cs (read-only projection)
export interface PhoneNumberSummary {
  id: string;
  businessAccountId: string;
  displayPhoneNumber: string;
  status: string;
  qualityRating: string | null;
  messagingTier: string | null;
}

// ── Quality & Windows / WaIntel (raw DTOs) ───────────────────────────────────

// WaIntel.Application/Quality/Dtos/HealthSnapshotDto.cs
export interface HealthSnapshot {
  phoneNumberId: string;
  periodStart: string;
  periodEnd: string;
  deliveryRate: number | null;
  readRate: number | null;
  blockProxyRate: number | null;
  qualityRating: string | null;
  messagingTier: string | null;
  tierHeadroom: number | null;
  messagesSent: number;
  messagesDelivered: number;
  messagesRead: number;
  messagesFailed: number;
}

// WaIntel.Application/Quality/Dtos/GuardianIncidentDto.cs
export interface GuardianIncident {
  id: string;
  phoneNumberId: string;
  incidentType: string;
  severity: string;
  status: string;
  throttleAction: string;
  triggerRating: string | null;
  openedAt: string;
  resolvedAt: string | null;
}

export interface HealthReport {
  snapshots: HealthSnapshot[];
  openIncidents: GuardianIncident[];
}

// WaIntel.Application/Quality/Dtos/TierAdvisorDto.cs
export interface TierAdvisor {
  phoneNumberId: string;
  currentTier: string;
  nextTier: string | null;
  currentDailyLimit: number | null;
  recentAverageDailyVolume: number;
  recommendedDailyVolume: number;
  readyToGrow: boolean;
  recommendation: string;
}

// WaIntel.Application/Windows/Dtos/WindowStateDto.cs
export interface WindowState {
  waId: string;
  phoneNumberId: string;
  origin: string;
  csExpiresAt: string | null;
  csOpen: boolean;
  ctwaExpiresAt: string | null;
  ctwaOpen: boolean;
}

// ── Gateway health aggregation (wavio.Gateway /health/services) ──────────────
// 200 when all healthy, 207 Multi-Status when any service is degraded.

export interface ServiceHealthEntry {
  service: string;
  status: string;
}

export interface ServicesHealth {
  overall: string;
  services: ServiceHealthEntry[];
}
