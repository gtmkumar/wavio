using WaAdmin.Application.Templates.Queries.GetTemplates;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Templates;
using Xunit;

namespace WaAdmin.Tests.Templates;

/// <summary>Security-review follow-up (S2, issue #16): pageSize must be clamped server-side, not
/// trusted from the caller — an authenticated tenant could otherwise force an arbitrarily large
/// single-page fetch. <c>PaginatedList&lt;T&gt;.PageSize</c> itself is private, so these tests
/// assert on the observable page shape (item count returned, page count) instead.</summary>
public class GetTemplatesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static async Task SeedAsync(InMemoryWaAdminDbContext db, int count)
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            db.Templates.Add(new Template
            {
                Id = Guid.NewGuid(), TenantId = TenantId, BusinessAccountId = Guid.NewGuid(),
                Name = $"template_{i}", Language = "en_US", Category = "utility", Status = "DRAFT",
                CreatedAt = now, UpdatedAt = now, Version = 1,
            });
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandleAsync_PageSizeAboveCap_ClampsTo200()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_PageSizeAboveCap_ClampsTo200));
        await SeedAsync(db, 250); // more than the 200 cap, so an unclamped fetch would be observably different
        var handler = new GetTemplatesQueryHandler(db);

        var result = await handler.HandleAsync(new GetTemplatesQuery(1, 100_000), CancellationToken.None);

        Assert.Equal(200, result.List.Count);
        Assert.Equal(2, result.PageCount); // ceil(250 / 200) — proves the effective page size was 200, not 100,000
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task HandleAsync_NonPositivePageSize_FallsBackToDefaultOf20(int requestedPageSize)
    {
        await using var db = InMemoryWaAdminDbContext.Create($"{nameof(HandleAsync_NonPositivePageSize_FallsBackToDefaultOf20)}_{requestedPageSize}");
        await SeedAsync(db, 25);
        var handler = new GetTemplatesQueryHandler(db);

        var result = await handler.HandleAsync(new GetTemplatesQuery(1, requestedPageSize), CancellationToken.None);

        Assert.Equal(20, result.List.Count);
        Assert.Equal(2, result.PageCount); // ceil(25 / 20)
    }

    [Fact]
    public async Task HandleAsync_NonPositivePage_FallsBackToPage1()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_NonPositivePage_FallsBackToPage1));
        var handler = new GetTemplatesQueryHandler(db);

        var result = await handler.HandleAsync(new GetTemplatesQuery(0, 20), CancellationToken.None);

        Assert.Equal(1, result.PageNumber);
    }

    [Fact]
    public async Task HandleAsync_PageSizeWithinBounds_UsesRequestedValue()
    {
        await using var db = InMemoryWaAdminDbContext.Create(nameof(HandleAsync_PageSizeWithinBounds_UsesRequestedValue));
        await SeedAsync(db, 60);
        var handler = new GetTemplatesQueryHandler(db);

        var result = await handler.HandleAsync(new GetTemplatesQuery(1, 50), CancellationToken.None);

        Assert.Equal(50, result.List.Count);
        Assert.Equal(2, result.PageCount); // ceil(60 / 50)
    }
}
