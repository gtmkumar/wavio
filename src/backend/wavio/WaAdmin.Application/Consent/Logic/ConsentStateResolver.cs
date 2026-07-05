namespace WaAdmin.Application.Consent.Logic;

/// <summary>Per-purpose consent snapshot for GET /v1/consent/{waId} — see
/// <see cref="ConsentStateResolver.Resolve"/>.</summary>
public sealed record ConsentPurposeState(string Purpose, bool OptedIn, DateTimeOffset? LastOptInAt, DateTimeOffset? LastOptOutAt);

/// <summary>
/// Derives the CURRENT consent state per purpose from the append-only opt-in/opt-out ledgers
/// (issue #21, spec §4.10) — there is no single "current state" row to read; state is always
/// derived by comparing the most recent applicable event of each kind. Pure, no I/O.
///
/// Rule: a purpose is opted-in iff the latest opt-in event FOR THAT PURPOSE is more recent than
/// the latest opt-out event that APPLIES to that purpose — an opt-out with
/// <c>scope=marketing</c> only overrides the "marketing" purpose; <c>scope=all</c> overrides
/// every purpose. No opt-in event at all means never-opted-in, regardless of opt-out history.
/// </summary>
public static class ConsentStateResolver
{
    public static readonly IReadOnlyList<string> Purposes = ["transactional", "marketing", "service"];

    public static IReadOnlyList<ConsentPurposeState> Resolve(
        IReadOnlyList<(string Purpose, DateTimeOffset OccurredAt)> optIns,
        IReadOnlyList<(string Scope, DateTimeOffset OccurredAt)> optOuts)
    {
        DateTimeOffset? latestOptOutAll = optOuts
            .Where(o => o.Scope == "all")
            .Select(o => (DateTimeOffset?)o.OccurredAt)
            .OrderByDescending(o => o)
            .FirstOrDefault();

        var results = new List<ConsentPurposeState>(Purposes.Count);
        foreach (var purpose in Purposes)
        {
            DateTimeOffset? latestOptIn = optIns
                .Where(o => o.Purpose == purpose)
                .Select(o => (DateTimeOffset?)o.OccurredAt)
                .OrderByDescending(o => o)
                .FirstOrDefault();

            DateTimeOffset? latestOptOutForPurpose = optOuts
                .Where(o => o.Scope == purpose)
                .Select(o => (DateTimeOffset?)o.OccurredAt)
                .OrderByDescending(o => o)
                .FirstOrDefault();

            var latestApplicableOptOut = Later(latestOptOutAll, latestOptOutForPurpose);

            var optedIn = latestOptIn is not null &&
                (latestApplicableOptOut is null || latestOptIn > latestApplicableOptOut);

            results.Add(new ConsentPurposeState(purpose, optedIn, latestOptIn, latestApplicableOptOut));
        }
        return results;
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a > b ? a : b;
    }
}
