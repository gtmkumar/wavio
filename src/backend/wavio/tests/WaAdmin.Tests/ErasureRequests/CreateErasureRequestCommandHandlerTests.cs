using WaAdmin.Application.Consent.Dtos;
using WaAdmin.Application.ErasureRequests.Commands.CreateErasureRequest;
using WaAdmin.Application.ErasureRequests.Queries.GetErasureRequestById;
using WaAdmin.Tests.Fakes;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaAdmin.Tests.ErasureRequests;

public class CreateErasureRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string WaId = "919876543210";

    private static InMemoryWaAdminDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        InMemoryWaAdminDbContext.Create(name);

    [Fact]
    public async Task HandleAsync_ErasureRequest_CreatesPendingRow()
    {
        await using var db = NewDb();
        var handler = new CreateErasureRequestCommandHandler(db);

        var result = await handler.HandleAsync(
            new CreateErasureRequestCommand(
                new CreateErasureRequestRequest(WaId, "erasure", "customer requested deletion", "support-agent-1"),
                TenantId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal("pending", result.Status);
        Assert.Null(result.ContentErasedAt);
        Assert.Null(result.CompletedAt);
        Assert.Single(db.ErasureRequests);
    }

    [Fact]
    public async Task HandleAsync_ExportRequest_CreatesPendingRowWithExportType()
    {
        await using var db = NewDb();
        var handler = new CreateErasureRequestCommandHandler(db);

        var result = await handler.HandleAsync(
            new CreateErasureRequestCommand(
                new CreateErasureRequestRequest(WaId, "export", null, null), TenantId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal("export", result.RequestType);
        Assert.Equal("pending", result.Status);
    }

    [Theory]
    [InlineData("", "erasure")]
    [InlineData(WaId, "not_a_real_type")]
    public async Task HandleAsync_InvalidRequest_ThrowsValidationExceptionAndPersistsNothing(string waId, string requestType)
    {
        await using var db = NewDb();
        var handler = new CreateErasureRequestCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new CreateErasureRequestCommand(new CreateErasureRequestRequest(waId, requestType, null, null), TenantId, null),
            CancellationToken.None));

        Assert.Empty(db.ErasureRequests);
    }

    [Fact]
    public async Task GetErasureRequestByIdQueryHandler_UnknownId_ReturnsNull()
    {
        await using var db = NewDb();
        var handler = new GetErasureRequestByIdQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetErasureRequestByIdQuery(TenantId, Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetErasureRequestByIdQueryHandler_AnotherTenantsRequest_ReturnsNull()
    {
        await using var db = NewDb();
        var createHandler = new CreateErasureRequestCommandHandler(db);
        var created = await createHandler.HandleAsync(
            new CreateErasureRequestCommand(new CreateErasureRequestRequest(WaId, "erasure", null, null), TenantId, null),
            CancellationToken.None);

        var getHandler = new GetErasureRequestByIdQueryHandler(db);
        var result = await getHandler.HandleAsync(
            new GetErasureRequestByIdQuery(Guid.NewGuid(), created.Id), CancellationToken.None);

        Assert.Null(result);
    }
}
