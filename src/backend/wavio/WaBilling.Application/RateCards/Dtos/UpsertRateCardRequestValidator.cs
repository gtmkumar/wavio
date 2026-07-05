using FluentValidation;

namespace WaBilling.Application.RateCards.Dtos;

/// <summary>Top-level shape validation for <c>POST /v1/rate-cards</c> — auto-discovered by
/// <c>AddValidatorsFromAssembly</c> and run via <c>ValidationFilter&lt;UpsertRateCardRequest&gt;</c>
/// on the endpoint (same convention as WaGateway's SendMessageRequestValidator). The CQRS
/// dispatcher pipeline does NOT run FluentValidation (dead code — see
/// .claude/agent-memory/dotnet-backend-developer/cqrs-validation-pipeline-is-dead-code.md), so
/// this only fires at the HTTP boundary; cross-field/DB-dependent rules (e.g. duplicate
/// (currency, effective_from)) are guard clauses in the handler.</summary>
public sealed class UpsertRateCardRequestValidator : AbstractValidator<UpsertRateCardRequest>
{
    public UpsertRateCardRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Source).Must(s => s is "meta" or "manual")
            .WithMessage("source must be 'meta' or 'manual'.");
        RuleFor(x => x.Status).Must(s => s is "draft" or "active" or "superseded")
            .WithMessage("status must be one of: draft, active, superseded.");
        RuleFor(x => x.EffectiveFrom).NotEqual(default(DateOnly));
        RuleFor(x => x.EffectiveTo)
            .GreaterThanOrEqualTo(x => x.EffectiveFrom)
            .When(x => x.EffectiveTo is not null)
            .WithMessage("effectiveTo must be on or after effectiveFrom.");

        RuleFor(x => x.Entries).NotEmpty().WithMessage("At least one rate-card entry is required.");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.Category).Must(RateCardCategories.All.Contains)
                .WithMessage($"category must be one of: {string.Join(", ", RateCardCategories.All)}.");
            entry.RuleFor(e => e.Market).NotEmpty().MaximumLength(60);
            entry.RuleFor(e => e.PricePerMessage).GreaterThanOrEqualTo(0);
        });
    }
}
