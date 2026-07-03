using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.AcceptInvite;

// ── DTO ─────────────────────────────────────────────────────────────────────
public sealed record AcceptInviteRequest(string Token, string NewPassword);

// ── Accept an invitation: set password, activate (public, unauthenticated) ──
public sealed record AcceptInviteCommand(AcceptInviteRequest Request) : ICommand<bool>;
