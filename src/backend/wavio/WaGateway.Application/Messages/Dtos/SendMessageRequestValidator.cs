using FluentValidation;

namespace WaGateway.Application.Messages.Dtos;

/// <summary>Top-level shape validation for <c>POST /v1/messages</c> — auto-discovered by
/// <c>AddValidatorsFromAssembly</c> and run via <c>ValidationFilter&lt;SendMessageRequest&gt;</c>
/// on the endpoint (see WaIngest's/core's Auth.cs for the same convention). Per-message-type
/// payload shape is validated separately by <c>MessagePayloadValidator</c> inside the command
/// handler, since it depends on the runtime <see cref="SendMessageRequest.MessageType"/> value.</summary>
public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.PhoneNumberId).NotEmpty();
        RuleFor(x => x.ToWaId).NotEmpty().MaximumLength(20);
        RuleFor(x => x.MessageType)
            .Must(MessageTypes.All.Contains)
            .WithMessage($"messageType must be one of: {string.Join(", ", MessageTypes.All)}.");
    }
}
