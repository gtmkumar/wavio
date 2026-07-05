using WaAdmin.Application.Consent.Commands.RecordOptIn;
using WaAdmin.Application.Consent.Dtos;
using WaAdmin.Tests.Fakes;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaAdmin.Tests.Consent;

public class RecordOptInCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string WaId = "919876543210";

    private static InMemoryWaAdminDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        InMemoryWaAdminDbContext.Create(name);

    [Fact]
    public async Task HandleAsync_ValidRequest_WritesAppendOnlyOptInEventRow()
    {
        await using var db = NewDb();
        var handler = new RecordOptInCommandHandler(db);
        var req = new RecordOptInRequest(WaId, "marketing", "web_form", null, null, null, null, "front-desk");

        var result = await handler.HandleAsync(
            new RecordOptInCommand(req, TenantId, Guid.NewGuid(), null), CancellationToken.None);

        Assert.Equal(WaId, result.WaId);
        Assert.Equal("marketing", result.Purpose);
        Assert.Single(db.OptInEvents);
        Assert.Equal("front-desk", db.OptInEvents.Single().Actor);
    }

    [Fact]
    public async Task HandleAsync_BehalfOfFields_AreFoldedIntoEvidenceJson()
    {
        // Consenting party != service recipient (spec §4.10) — the caller passes explicit typed
        // fields, the handler folds them into evidence jsonb rather than a schema change.
        await using var db = NewDb();
        var handler = new RecordOptInCommandHandler(db);
        var req = new RecordOptInRequest(
            WaId, "service", "in_person", OnBehalfOfWaId: "919999999999", OnBehalfOfName: "Dependant Name",
            EvidenceProofRef: "proof-ref-123", EvidenceWamid: null, Actor: "receptionist");

        await handler.HandleAsync(new RecordOptInCommand(req, TenantId, Guid.NewGuid(), null), CancellationToken.None);

        var stored = db.OptInEvents.Single();
        Assert.NotNull(stored.Evidence);
        Assert.Contains("919999999999", stored.Evidence);
        Assert.Contains("Dependant Name", stored.Evidence);
        Assert.Contains("proof-ref-123", stored.Evidence);
    }

    [Fact]
    public async Task HandleAsync_NoBehalfOfFields_EvidenceIsNull()
    {
        await using var db = NewDb();
        var handler = new RecordOptInCommandHandler(db);
        var req = new RecordOptInRequest(WaId, "marketing", "api", null, null, null, null, null);

        await handler.HandleAsync(new RecordOptInCommand(req, TenantId, Guid.NewGuid(), null), CancellationToken.None);

        Assert.Null(db.OptInEvents.Single().Evidence);
    }

    [Theory]
    [InlineData("", "marketing", "web_form")]
    [InlineData(WaId, "not_a_real_purpose", "web_form")]
    [InlineData(WaId, "marketing", "not_a_real_channel")]
    public async Task HandleAsync_InvalidRequest_ThrowsValidationExceptionAndPersistsNothing(
        string waId, string purpose, string captureChannel)
    {
        await using var db = NewDb();
        var handler = new RecordOptInCommandHandler(db);
        var req = new RecordOptInRequest(waId, purpose, captureChannel, null, null, null, null, null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new RecordOptInCommand(req, TenantId, Guid.NewGuid(), null), CancellationToken.None));

        Assert.Empty(db.OptInEvents);
    }

    [Fact]
    public async Task HandleAsync_SecondOptInForSamePurpose_WritesASecondAppendOnlyRowRatherThanUpdating()
    {
        await using var db = NewDb();
        var handler = new RecordOptInCommandHandler(db);
        var req = new RecordOptInRequest(WaId, "marketing", "web_form", null, null, null, null, null);

        await handler.HandleAsync(new RecordOptInCommand(req, TenantId, Guid.NewGuid(), null), CancellationToken.None);
        await handler.HandleAsync(new RecordOptInCommand(req, TenantId, Guid.NewGuid(), null), CancellationToken.None);

        Assert.Equal(2, db.OptInEvents.Count());
    }
}
