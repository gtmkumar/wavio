using FluentValidation;

namespace WaGateway.Application.Campaigns.Dtos;

/// <summary>Top-level shape validation for <c>POST /v1/campaigns</c> — auto-discovered by
/// <c>AddValidatorsFromAssembly</c> and run via <c>ValidationFilter&lt;CreateCampaignRequest&gt;</c>
/// on the endpoint (same convention as <c>SendMessageRequestValidator</c>, issue #14). Business
/// rules that need a DB lookup (phone number ownership, template-version approval, duplicate
/// waId) live in <c>CreateCampaignCommandHandler</c> instead — this validator only checks what's
/// knowable from the request body alone.</summary>
public sealed class CreateCampaignRequestValidator : AbstractValidator<CreateCampaignRequest>
{
    public CreateCampaignRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumberId).NotEmpty();
        RuleFor(x => x.TemplateVersionId).NotEmpty();
        RuleFor(x => x.Audience).NotEmpty();
        RuleForEach(x => x.Audience).ChildRules(member =>
        {
            member.RuleFor(m => m.WaId).NotEmpty().MaximumLength(20);
        });
    }
}
