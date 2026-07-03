using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.OtpSend;

public sealed record OtpSendCommand(
    string Identifier,
    string IdentifierType,
    string Purpose,
    string? IpAddress,
    string? UserAgent) : ICommand<OtpSentResponse>;
