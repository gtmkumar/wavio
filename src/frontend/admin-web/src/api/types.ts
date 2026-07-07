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

// ── Billing / WaBilling (envelope-wrapped) ───────────────────────────────────

// WaBilling.Application/RateCards/Dtos/RateCardDtos.cs
export const RATE_CARD_CATEGORIES = [
  "marketing",
  "utility",
  "authentication",
  "authentication_international",
  "service",
] as const;

export interface RateCardEntry {
  id: string;
  category: string;
  market: string;
  volumeTier: string | null;
  pricePerMessage: number;
  currency: string;
}

export interface RateCard {
  id: string;
  name: string;
  currency: string;
  source: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  status: string;
  notes: string | null;
  entries: RateCardEntry[];
}

export interface UpsertRateCardEntryRequest {
  category: string;
  market: string;
  volumeTier: string | null;
  pricePerMessage: number;
}

export interface UpsertRateCardRequest {
  name: string;
  currency: string;
  source: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  status: string;
  notes: string | null;
  entries: UpsertRateCardEntryRequest[];
}

// WaBilling.Application/Quotas/Dtos/QuotaDtos.cs
export interface QuotaStatusEntry {
  category: string;
  period: string;
  limitUnit: string;
  softLimit: number | null;
  hardLimit: number | null;
  currentValue: number;
  softLimitAlerted: boolean;
  hardLimitBlocked: boolean;
}

// WaBilling.Application/Estimator/Dtos/CostEstimateDto.cs
export interface CostEstimate {
  found: boolean;
  billable: boolean;
  amount: number;
  currency: string;
  category: string;
  market: string;
  volumeTier: string | null;
  rateCardId: string | null;
  reason: string;
}

// WaBilling.Application/Reconciliation/Dtos/ReconciliationDto.cs
export interface Reconciliation {
  periodStart: string;
  periodEnd: string;
  ledgerTotal: number;
  ledgerRowCount: number;
  invoiceTotal: number;
  invoiceRowCount: number;
  varianceAmount: number;
  variancePercent: number | null;
  withinTarget: boolean;
}

// ── Consent / DPDP / WaAdmin (envelope-wrapped) ──────────────────────────────

// WaAdmin.Application/Consent/Dtos/ConsentDtos.cs
export const CONSENT_PURPOSES = ["transactional", "marketing", "service"] as const;
export const CONSENT_CAPTURE_CHANNELS = [
  "web_form",
  "qr",
  "in_chat",
  "in_person",
  "api",
  "import",
] as const;
/** Manual API path — stop_keyword is reserved for the STOP listener. */
export const OPT_OUT_REASONS = ["manual", "complaint"] as const;
export const OPT_OUT_SCOPES = ["marketing", "all"] as const;

export interface RecordOptInRequest {
  waId: string;
  purpose: string;
  captureChannel: string;
  onBehalfOfWaId: string | null;
  onBehalfOfName: string | null;
  evidenceProofRef: string | null;
  evidenceWamid: string | null;
  actor: string | null;
}

export interface OptInEvent {
  id: string;
  waId: string;
  purpose: string;
  captureChannel: string;
  evidenceWamid: string | null;
  actor: string | null;
  occurredAt: string;
}

export interface RecordManualOptOutRequest {
  waId: string;
  scope: string;
  reason: string;
  notes: string | null;
}

export interface OptOutEvent {
  id: string;
  waId: string;
  scope: string;
  reason: string;
  keyword: string | null;
  language: string | null;
  occurredAt: string;
}

export interface ConsentPurposeState {
  purpose: string;
  optedIn: boolean;
  lastOptInAt: string | null;
  lastOptOutAt: string | null;
}

export interface ConsentState {
  waId: string;
  suppressed: boolean;
  purposes: ConsentPurposeState[];
}

export const ERASURE_REQUEST_TYPES = ["erasure", "export"] as const;

export interface CreateErasureRequestRequest {
  waId: string;
  requestType: string;
  reason: string | null;
  requestedBy: string | null;
}

export interface ErasureRequest {
  id: string;
  waId: string;
  requestType: string;
  status: string;
  reason: string | null;
  contentErasedAt: string | null;
  exportRef: string | null;
  completedAt: string | null;
  createdAt: string;
}

// WaAdmin.Application/RetentionPolicies (UpsertRetentionPolicyCommandHandler vocabulary)
export const RETENTION_DATA_CLASSES = [
  "message_content",
  "metadata",
  "cost_ledger",
  "consent_evidence",
  "raw_webhook",
] as const;

export interface RetentionPolicy {
  id: string;
  tenantId: string | null;
  dataClass: string;
  retentionDays: number;
  basis: string | null;
  enabled: boolean;
  updatedAt: string;
}

export interface UpsertRetentionPolicyRequest {
  dataClass: string;
  retentionDays: number;
  basis: string | null;
  enabled: boolean;
}

// ── Identity admin (core.Application — envelope-wrapped) ─────────────────────

// wavio.SharedDataModel/Enums/UserType.cs / UserStatus.cs
export const USER_TYPES = ["platform_admin", "tenant_admin", "staff", "auditor", "support"] as const;
export const USER_STATUSES = ["active", "invited", "locked", "suspended"] as const;

// core.Application/Identity/Users/Dtos/UserDtos.cs (UserDto — list projection
// fills only through displayName; the detail endpoint fills the profile too)
export interface UserDetail {
  id: string;
  email: string | null;
  phoneE164: string | null;
  userType: string;
  status: string;
  mfaEnabled: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  firstName: string | null;
  lastName: string | null;
  displayName: string | null;
  designation: string | null;
  employmentType: string | null;
  kycStatus: string | null;
}

// UserDtos.cs UpdateUserRequest — null leaves a field unchanged, "" clears it.
export interface UpdateUserRequest {
  email?: string | null;
  phone?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  designation?: string | null;
}

// UserDtos.cs RoleDto / PermissionDto / ChangeRoleRequest
export interface Role {
  id: string;
  code: string;
  name: string;
  scopeType: string;
  isSystem: boolean;
  status: string;
}

export interface Permission {
  id: string;
  code: string;
  module: string;
  action: string;
  name: string;
  riskLevel: string;
}

export interface ChangeRoleRequest {
  roleId: string;
  scopeType: string;
  /** null lets the backend fall back to the actor's tenant for tenant-scoped roles. */
  scopeId: string | null;
}

// core.Application/Identity/AccessControl/Dtos/AccessControlDtos.cs
export interface Person {
  id: string;
  name: string;
  email: string;
  initials: string;
  roleCode: string;
  roleName: string;
  scopeLabel: string;
  tier: string;
  status: string;
  userType: string;
  lastActiveAt: string | null;
}

export interface PeopleCounts {
  all: number;
  platformStaff: number;
  tenantStaff: number;
}

export interface AccessPeoplePage {
  counts: PeopleCounts;
  people: PaginatedList<Person>;
}

export interface InviteUserRequest {
  email: string;
  phone: string | null;
  firstName: string | null;
  lastName: string | null;
  userType: string;
  roleId: string;
  scopeType: string;
  scopeId: string | null;
  /** When set the account is created active immediately; otherwise an invite email is sent. */
  password: string | null;
}

export interface SetPersonStatusRequest {
  action: "activate" | "suspend" | "reactivate";
  /** activate requires a temporary password (≥8 chars); the user must change it on first login. */
  password: string | null;
}

export interface SetPersonStatusResult {
  status: string;
  mustChangePassword: boolean;
}

// AccessControlDtos.cs — roles/permissions matrix for the console
export interface MatrixModule {
  key: string;
  label: string;
}

export interface RoleSummary {
  id: string;
  code: string;
  name: string;
  description: string | null;
  scopeType: string;
  isSystem: boolean;
  memberCount: number;
  /** "module:action" cell keys currently granted to this role. */
  onCells: string[];
}

export interface RoleGroup {
  tier: string;
  tierLabel: string;
  roles: RoleSummary[];
}

export interface AccessRoles {
  modules: MatrixModule[];
  actions: string[];
  groups: RoleGroup[];
  /** cellKey ("module:action") → permission codes that checkbox grants. */
  cells: Record<string, string[]>;
}

export interface RoleCellChange {
  cellKey: string;
  enabled: boolean;
}

// core.Application/Identity/AccessControl/Commands/ManageRoles/ManageRoles.cs
export interface CreateRoleRequest {
  code: string;
  name: string;
  description: string | null;
  scopeType: string;
}

export interface UpdateRoleRequest {
  name: string;
  description: string | null;
}

export interface CloneRoleRequest {
  code: string;
  name: string;
  description: string | null;
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
