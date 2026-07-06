using WaPlatform.Contracts.TemplateDsl;

namespace WaAdmin.Infrastructure.Templates;

/// <summary>
/// Pre-approved vertical template packs (issue #27, spec §4.4): appointment reminder, pickup
/// scheduled, order ready, payment link, and OTP. Each is authored so its own category — utility
/// for everything except OTP, which uses Meta's authentication category — stays honest: no
/// promotional phrasing, no leading/trailing variables, sane variable density, no formatting
/// violations. This is verified by a test that runs every pack through the same
/// <see cref="RulesTemplateLintService"/> used at real submission time (not by inspection alone).
/// Seeded into templates.template_packs with tenant_id NULL (the nullable-tenant pattern — every
/// tenant can read these) by <c>WaAdmin.Infrastructure.Seeders.TemplatePackSeeder</c>.
/// </summary>
internal static class VerticalTemplatePacks
{
    public sealed record Pack(string PackKey, string Vertical, string Name, string Description, TemplateDefinition Definition);

    public static readonly IReadOnlyList<Pack> All =
    [
        new Pack(
            "appointment_reminder", "general", "Appointment Reminder",
            "Reminds a customer of an upcoming appointment and lets them confirm or reschedule.",
            new TemplateDefinition
            {
                Name = "appointment_reminder",
                Language = "en_US",
                Category = TemplateCategory.Utility,
                Components =
                [
                    new TemplateComponent
                    {
                        Type = "BODY",
                        Text = "Hi {{1}}, this is a reminder for your appointment on {{2}} at {{3}}. " +
                               "Reply CONFIRM to confirm or CANCEL to reschedule.",
                    },
                ],
            }),
        new Pack(
            "pickup_scheduled", "laundry", "Pickup Scheduled",
            "Confirms a scheduled pickup window for a laundry/dry-cleaning service.",
            new TemplateDefinition
            {
                Name = "pickup_scheduled",
                Language = "en_US",
                Category = TemplateCategory.Utility,
                Components =
                [
                    new TemplateComponent
                    {
                        Type = "BODY",
                        Text = "Hi {{1}}, your pickup is scheduled for {{2}} between {{3}}. " +
                               "Please have your items ready at the door.",
                    },
                ],
            }),
        new Pack(
            "order_ready", "retail", "Order Ready",
            "Notifies a customer that their order is ready for pickup.",
            new TemplateDefinition
            {
                Name = "order_ready",
                Language = "en_US",
                Category = TemplateCategory.Utility,
                Components =
                [
                    new TemplateComponent
                    {
                        Type = "BODY",
                        Text = "Hi {{1}}, your order {{2}} is ready for pickup at {{3}}.",
                    },
                ],
            }),
        new Pack(
            "payment_link", "general", "Payment Link",
            "Sends a customer a secure payment link for a pending order balance.",
            new TemplateDefinition
            {
                Name = "payment_link",
                Language = "en_US",
                Category = TemplateCategory.Utility,
                Components =
                [
                    new TemplateComponent
                    {
                        Type = "BODY",
                        Text = "Hi {{1}}, your payment of {{2}} for order {{3}} is pending. " +
                               "Please use the secure link below to complete payment.",
                    },
                    new TemplateComponent
                    {
                        Type = "BUTTONS",
                        ExtrasJson = """{"buttons":[{"type":"URL","text":"Pay Now","url":"https://pay.example.com/{{1}}"}]}""",
                    },
                ],
            }),
        new Pack(
            "otp", "general", "One-Time Passcode",
            "Delivers a one-time verification code (Meta authentication category).",
            new TemplateDefinition
            {
                Name = "otp_verification",
                Language = "en_US",
                Category = TemplateCategory.Authentication,
                Components =
                [
                    new TemplateComponent
                    {
                        Type = "BODY",
                        Text = "Your verification code is {{1}}. For your security, do not share this code with anyone.",
                    },
                ],
            }),
    ];
}
