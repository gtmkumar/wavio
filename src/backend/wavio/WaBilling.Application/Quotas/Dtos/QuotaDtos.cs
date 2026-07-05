namespace WaBilling.Application.Quotas.Dtos;

/// <summary>POST /v1/quotas/check result — the send-time gate (spec §4.7). <see cref="Allowed"/>
/// is false only when <see cref="Blocked"/> is true (marketing hard-limit block); a
/// non-marketing hard breach is surfaced via <see cref="HardLimitReached"/> without blocking.</summary>
public sealed record QuotaCheckResultDto(
    bool Allowed,
    bool Blocked,
    bool SoftLimitReached,
    bool HardLimitReached,
    string Reason);

/// <summary>One row of GET /v1/quotas/status — current usage vs. configured limits for one
/// (category, period) quota the tenant has enabled.</summary>
public sealed record QuotaStatusEntryDto(
    string Category,
    string Period,
    string LimitUnit,
    long? SoftLimit,
    long? HardLimit,
    decimal CurrentValue,
    bool SoftLimitAlerted,
    bool HardLimitBlocked);
