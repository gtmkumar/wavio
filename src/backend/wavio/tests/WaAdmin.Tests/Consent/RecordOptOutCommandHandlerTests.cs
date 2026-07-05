using WaAdmin.Application.Consent.Commands.RecordOptOut;
using WaAdmin.Tests.Fakes;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaAdmin.Tests.Consent;

public class RecordOptOutCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string WaId = "919876543210";

    private static InMemoryWaAdminDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        InMemoryWaAdminDbContext.Create(name);

    [Fact]
    public async Task HandleAsync_ManualOptOut_WritesOptOutEventAndSuppressionRowInOneUnitOfWork()
    {
        await using var db = NewDb();
        var handler = new RecordOptOutCommandHandler(db);

        var result = await handler.HandleAsync(
            new RecordOptOutCommand(TenantId, WaId, "marketing", "manual", null, null, null, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal("manual", result.Reason);
        Assert.Single(db.OptOutEvents);
        Assert.Single(db.SuppressionListEntries);
        Assert.Equal(WaId, db.SuppressionListEntries.Single().WaId);
    }

    [Fact]
    public async Task HandleAsync_StopKeywordOptOut_RecordsKeywordAndLanguage()
    {
        await using var db = NewDb();
        var handler = new RecordOptOutCommandHandler(db);

        var result = await handler.HandleAsync(
            new RecordOptOutCommand(TenantId, WaId, "marketing", "stop_keyword", "stop", "en", "wamid-1", null, null),
            CancellationToken.None);

        Assert.Equal("stop", result.Keyword);
        Assert.Equal("en", result.Language);
        Assert.Equal("stop_listener", db.SuppressionListEntries.Single().Source);
    }

    [Fact]
    public async Task HandleAsync_StopKeywordRedeliveredWithSameInboundWamid_IsIdempotent()
    {
        // The STOP listener has no DB unique constraint to lean on (V012 has none on
        // inbound_wamid) — this app-level check is the only guard against RabbitMQ redelivery
        // creating a duplicate opt-out event or double-touching the suppression row.
        await using var db = NewDb();
        var handler = new RecordOptOutCommandHandler(db);
        var command = new RecordOptOutCommand(TenantId, WaId, "marketing", "stop_keyword", "stop", "en", "wamid-dup", null, null);

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var second = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Single(db.OptOutEvents);
        Assert.Single(db.SuppressionListEntries);
    }

    [Fact]
    public async Task HandleAsync_SecondOptOutForAlreadySuppressedRecipient_UpdatesExistingSuppressionRowRatherThanDuplicating()
    {
        await using var db = NewDb();
        var handler = new RecordOptOutCommandHandler(db);

        await handler.HandleAsync(
            new RecordOptOutCommand(TenantId, WaId, "marketing", "manual", null, null, null, null, null),
            CancellationToken.None);
        await handler.HandleAsync(
            new RecordOptOutCommand(TenantId, WaId, "marketing", "complaint", null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal(2, db.OptOutEvents.Count()); // append-only ledger — both rows kept
        Assert.Single(db.SuppressionListEntries); // enforcement row is upserted, not duplicated
        Assert.Equal("complaint", db.SuppressionListEntries.Single().Reason);
    }

    [Theory]
    [InlineData("not_a_real_scope", "manual")]
    [InlineData("marketing", "not_a_real_reason")]
    public async Task HandleAsync_InvalidScopeOrReason_ThrowsValidationExceptionAndPersistsNothing(
        string scope, string reason)
    {
        await using var db = NewDb();
        var handler = new RecordOptOutCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new RecordOptOutCommand(TenantId, WaId, scope, reason, null, null, null, null, null),
            CancellationToken.None));

        Assert.Empty(db.OptOutEvents);
        Assert.Empty(db.SuppressionListEntries);
    }
}
