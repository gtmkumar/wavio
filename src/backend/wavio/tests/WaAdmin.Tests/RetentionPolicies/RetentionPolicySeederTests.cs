using WaAdmin.Infrastructure.Seeders;
using Xunit;

namespace WaAdmin.Tests.RetentionPolicies;

/// <summary>Locks in the spec-derived retention numbers (spec §4.10: "message content 12
/// months, metadata/cost ledger 8 years for tax"; §6: "raw_webhooks (30-day TTL)") so a future
/// edit changing them is a deliberate, reviewed decision, not a silent typo.</summary>
public class RetentionPolicySeederTests
{
    [Theory]
    [InlineData("message_content", 365)]
    [InlineData("metadata", 2920)]
    [InlineData("cost_ledger", 2920)]
    [InlineData("consent_evidence", 2920)]
    [InlineData("raw_webhook", 30)]
    public void PlatformDefaults_ContainsExpectedRetentionDaysForEachDataClass(string dataClass, int expectedDays)
    {
        var entry = RetentionPolicySeeder.PlatformDefaults.Single(d => d.DataClass == dataClass);

        Assert.Equal(expectedDays, entry.RetentionDays);
    }

    [Fact]
    public void PlatformDefaults_CoversExactlyTheFiveCheckConstraintDataClasses()
    {
        // consent.retention_policies_data_class_check (V012) allows exactly these five values.
        string[] expected = ["message_content", "metadata", "cost_ledger", "consent_evidence", "raw_webhook"];

        Assert.Equal(expected.Order(), RetentionPolicySeeder.PlatformDefaults.Select(d => d.DataClass).Order());
    }
}
