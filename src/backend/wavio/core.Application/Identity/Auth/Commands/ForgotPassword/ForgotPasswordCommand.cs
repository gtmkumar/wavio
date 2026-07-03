using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(string Identifier, string IdentifierType) : ICommand<bool>;
