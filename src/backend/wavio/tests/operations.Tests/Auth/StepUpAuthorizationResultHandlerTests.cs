using System.Text.Json;
using wavio.Utilities.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the structured-403 translation of a §8 step-up denial by
/// <see cref="StepUpAuthorizationResultHandler"/>: a Forbid carrying a
/// <see cref="StepUpRequiredFailureReason"/> becomes a machine-readable
/// <c>step_up_required</c> envelope the client can prompt + retry against.
/// </summary>
public class StepUpAuthorizationResultHandlerTests
{
    [Fact]
    public async Task Step_up_denial_is_rendered_as_structured_403()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        var policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
        var reason = new StepUpRequiredFailureReason(new PermissionHandler(), "wallet.adjust");
        var result = PolicyAuthorizationResult.Forbid(
            AuthorizationFailure.Failed(new[] { reason }));

        await new StepUpAuthorizationResultHandler()
            .HandleAsync(next: _ => Task.CompletedTask, context, policy, result);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        var message = doc.RootElement.GetProperty("message");

        Assert.Equal("step_up_required", message.GetProperty("responseMessage").GetString());

        var codes = message.GetProperty("errorMessage").GetProperty("step_up_required");
        Assert.Equal(1, codes.GetArrayLength());
        Assert.Equal("wallet.adjust", codes[0].GetString());
    }
}
