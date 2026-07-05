using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.ErasureRequests.Commands.CreateErasureRequest;

/// <summary>POST /v1/consent/requests — raises a DPDP erasure or export request (issue #21,
/// spec §4.10/§9). Processing is asynchronous — see
/// WaAdmin.Infrastructure.BackgroundWork.ErasureRequestProcessorService.</summary>
public sealed record CreateErasureRequestCommand(
    CreateErasureRequestRequest Request, Guid TenantId, Guid? ActorId)
    : ICommand<ErasureRequestDto>;
