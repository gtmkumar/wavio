using System.Net;

namespace core.Application.Common;

/// <summary>Minimal, dependency-free HTML email bodies for the user-provisioning flows.</summary>
public static class EmailTemplates
{
    private const string Brand = "#5C6E2E";

    private static string Shell(string heading, string innerHtml) => $$"""
        <div style="font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:#F4F1E9;padding:24px">
          <div style="max-width:480px;margin:0 auto;background:#fff;border-radius:16px;overflow:hidden;border:1px solid #ECE7DA">
            <div style="background:{{Brand}};padding:20px 24px">
              <span style="color:#fff;font-weight:700;font-size:18px;letter-spacing:-0.3px">Wavio</span>
            </div>
            <div style="padding:24px">
              <h2 style="margin:0 0 12px;color:#1B2310;font-size:18px">{{heading}}</h2>
              {{innerHtml}}
            </div>
            <div style="padding:16px 24px;border-top:1px solid #F0ECE0;color:#9AA084;font-size:12px">
              Wavio Admin Console · automated message
            </div>
          </div>
        </div>
        """;

    private static string Button(string href, string label) =>
        $"<a href=\"{href}\" style=\"display:inline-block;background:{Brand};color:#fff;text-decoration:none;" +
        $"padding:11px 20px;border-radius:10px;font-weight:600;font-size:14px\">{label}</a>";

    /// <summary>Self-service invite: recipient sets their own password via a tokenised link.</summary>
    public static (string subject, string html) InviteSelfService(string name, string acceptUrl)
    {
        var who = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name);
        var html = Shell("You've been invited", $"""
            <p style="color:#444;font-size:14px;line-height:1.6">Hi {who}, you've been invited to the Wavio admin console.
            Set your password to activate your account and sign in.</p>
            <p style="margin:20px 0">{Button(acceptUrl, "Set your password")}</p>
            <p style="color:#9AA084;font-size:12px;line-height:1.6">This link is single-use. If you weren't expecting this, you can ignore this email.</p>
            """);
        return ("You're invited to Wavio", html);
    }

    /// <summary>Admin-activation invite: account exists but an admin will activate it.</summary>
    public static (string subject, string html) InviteAdminActivate(string name)
    {
        var who = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name);
        var html = Shell("Your account was created", $"""
            <p style="color:#444;font-size:14px;line-height:1.6">Hi {who}, an account was created for you on the Wavio admin console.
            An administrator will activate it shortly and share your sign-in details.</p>
            """);
        return ("Your Wavio account was created", html);
    }

    /// <summary>Admin activated the account and set a temporary password.</summary>
    public static (string subject, string html) Activated(string name, string email, string tempPassword, string loginUrl)
    {
        var who = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name);
        var html = Shell("Your account is active", $"""
            <p style="color:#444;font-size:14px;line-height:1.6">Hi {who}, your Wavio account is now active.
            Sign in with the temporary password below — you'll be asked to set a new one on first login.</p>
            <table style="margin:16px 0;font-size:14px">
              <tr><td style="color:#9AA084;padding:2px 12px 2px 0">Email</td><td style="color:#1B2310;font-weight:600">{WebUtility.HtmlEncode(email)}</td></tr>
              <tr><td style="color:#9AA084;padding:2px 12px 2px 0">Temporary password</td>
                  <td style="color:#1B2310;font-weight:700;font-family:ui-monospace,Menlo,monospace">{WebUtility.HtmlEncode(tempPassword)}</td></tr>
            </table>
            <p style="margin:20px 0">{Button(loginUrl, "Sign in")}</p>
            """);
        return ("Your Wavio account is active", html);
    }
}
