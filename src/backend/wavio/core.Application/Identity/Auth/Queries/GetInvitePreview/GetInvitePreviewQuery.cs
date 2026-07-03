using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Queries.GetInvitePreview;

// ── DTO ─────────────────────────────────────────────────────────────────────
public sealed record InvitePreviewDto(bool Valid, string? Email, string? Name);

// ── Preview an invitation token (public, unauthenticated) ───────────────────
public sealed record GetInvitePreviewQuery(string Token) : IQuery<InvitePreviewDto>;
