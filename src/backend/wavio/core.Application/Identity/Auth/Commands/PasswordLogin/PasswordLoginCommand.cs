using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.PasswordLogin;

public sealed record PasswordLoginCommand(
    string Identifier,
    string Password,
    string? IpAddress,
    string? UserAgent) : ICommand<TokenResponse>;
