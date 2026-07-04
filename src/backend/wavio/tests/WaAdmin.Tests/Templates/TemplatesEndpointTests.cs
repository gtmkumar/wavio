using WaAdmin.Application.Templates.Commands.CreateTemplate;
using WaAdmin.Application.Templates.Commands.DeleteTemplate;
using WaAdmin.Application.Templates.Commands.UpdateTemplate;
using WaAdmin.Application.Templates.Dtos;
using WaAdmin.Application.Templates.Queries.GetTemplateById;
using WaAdmin.WebApi.Endpoints;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Services;
using WaPlatform.Contracts.TemplateDsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Xunit;

namespace WaAdmin.Tests.Templates;

/// <summary>Thin endpoint tests: calls the static handler methods directly with fabricated
/// dependencies, same pattern as WaIngest.Tests' ReceiveWebhookTests. The state-machine and
/// persistence logic is covered by the Application-layer handler tests — these only verify the
/// endpoint dispatches the right command/query and maps results to the right HTTP status.</summary>
public class TemplatesEndpointTests
{
    private static ICurrentUser FakeUser(Guid tenantId, Guid userId)
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.RequireTenantId()).Returns(tenantId);
        user.SetupGet(u => u.UserId).Returns(userId);
        return user.Object;
    }

    private static TemplateDefinition Definition() => new()
    {
        Name = "order_confirmation",
        Language = "en_US",
        Category = TemplateCategory.Utility,
        Components = [new TemplateComponent { Type = "BODY", Text = "Hello" }],
    };

    [Fact]
    public async Task CreateTemplate_DispatchesCommandAndReturns201()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var dto = new TemplateDto(templateId, Guid.NewGuid(), "order_confirmation", "en_US", "utility",
            null, "PENDING", null, null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);

        var dispatcher = new Mock<IDispatcher>();
        dispatcher.Setup(d => d.SendAsync(It.IsAny<CreateTemplateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTemplateResult(dto, true, null));

        var result = await WaAdmin.WebApi.Endpoints.Templates.CreateTemplate(
            new CreateTemplateRequest(Guid.NewGuid(), Definition(), null),
            FakeUser(tenantId, userId), dispatcher.Object, CancellationToken.None);

        var created = Assert.IsType<Created<wavio.Utilities.ApiResponse.ResponseUtil.SingleResponse<CreateTemplateResult>>>(result);
        Assert.Equal($"/v1/templates/{templateId}", created.Location);
        dispatcher.Verify(d => d.SendAsync(
            It.Is<CreateTemplateCommand>(c => c.TenantId == tenantId && c.ActorId == userId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTemplateById_Found_Returns200()
    {
        var dto = new TemplateDto(Guid.NewGuid(), Guid.NewGuid(), "n", "en_US", "utility",
            null, "DRAFT", null, null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        var dispatcher = new Mock<IDispatcher>();
        dispatcher.Setup(d => d.QueryAsync(It.IsAny<GetTemplateByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await WaAdmin.WebApi.Endpoints.Templates.GetTemplateById(dto.Id, dispatcher.Object, CancellationToken.None);

        Assert.IsType<Ok<wavio.Utilities.ApiResponse.ResponseUtil.SingleResponse<TemplateDto>>>(result);
    }

    [Fact]
    public async Task GetTemplateById_NotFound_Returns404()
    {
        var dispatcher = new Mock<IDispatcher>();
        dispatcher.Setup(d => d.QueryAsync(It.IsAny<GetTemplateByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TemplateDto?)null);

        var result = await WaAdmin.WebApi.Endpoints.Templates.GetTemplateById(Guid.NewGuid(), dispatcher.Object, CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task UpdateTemplate_NotFound_Returns404()
    {
        var dispatcher = new Mock<IDispatcher>();
        dispatcher.Setup(d => d.SendAsync(It.IsAny<UpdateTemplateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TemplateDto?)null);

        var result = await WaAdmin.WebApi.Endpoints.Templates.UpdateTemplate(
            Guid.NewGuid(), new UpdateTemplateRequest(Definition(), null),
            FakeUser(Guid.NewGuid(), Guid.NewGuid()), dispatcher.Object, CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task DeleteTemplate_Found_Returns200_NotFound_Returns404()
    {
        var dispatcher = new Mock<IDispatcher>();
        dispatcher.Setup(d => d.SendAsync(It.IsAny<DeleteTemplateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var okResult = await WaAdmin.WebApi.Endpoints.Templates.DeleteTemplate(
            Guid.NewGuid(), FakeUser(Guid.NewGuid(), Guid.NewGuid()), dispatcher.Object, CancellationToken.None);
        Assert.IsType<Ok<wavio.Utilities.ApiResponse.ResponseUtil.Response>>(okResult);

        dispatcher.Setup(d => d.SendAsync(It.IsAny<DeleteTemplateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var notFoundResult = await WaAdmin.WebApi.Endpoints.Templates.DeleteTemplate(
            Guid.NewGuid(), FakeUser(Guid.NewGuid(), Guid.NewGuid()), dispatcher.Object, CancellationToken.None);
        Assert.IsType<NotFound>(notFoundResult);
    }
}
