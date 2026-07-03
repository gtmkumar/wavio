using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.OtpVerify;

public sealed record OtpVerifyCommand(
    string Identifier,
    string IdentifierType,
    string Purpose,
    string Code,
    string? IpAddress,
    string? UserAgent) : ICommand<OtpVerifiedResponse>;
