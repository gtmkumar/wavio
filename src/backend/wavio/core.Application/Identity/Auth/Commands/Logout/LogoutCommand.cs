using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.Logout;

public sealed record LogoutCommand(string RawRefreshToken) : ICommand<bool>;
