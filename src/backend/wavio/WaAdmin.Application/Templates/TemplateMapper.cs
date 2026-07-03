using WaAdmin.Application.Templates.Dtos;
using wavio.SharedDataModel.Entities.Templates;

namespace WaAdmin.Application.Templates;

internal static class TemplateMapper
{
    public static TemplateVersionDto ToDto(this TemplateVersion v) => new(
        v.Id, v.VersionNumber, v.Components, v.ExampleValues, v.Status,
        v.RejectionReason, v.SubmittedAt, v.ReviewedAt, v.CreatedAt);

    public static TemplateDto ToDto(this Template t, TemplateVersion? currentVersion) => new(
        t.Id, t.BusinessAccountId, t.Name, t.Language, t.Category, t.MetaTemplateId, t.Status,
        t.CurrentVersionId, t.PausedUntil, t.PauseCount, t.CreatedAt, t.UpdatedAt,
        currentVersion?.ToDto());

    public static TemplateStatusEventDto ToDto(this TemplateStatusEvent e) => new(
        e.Id, e.OldStatus, e.NewStatus, e.Reason, e.OccurredAt);
}
