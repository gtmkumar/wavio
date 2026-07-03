using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.ResetPassword;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand<bool>;
