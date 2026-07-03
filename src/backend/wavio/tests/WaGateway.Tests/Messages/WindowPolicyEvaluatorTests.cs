using WaGateway.Application.Messages.Dtos;
using WaGateway.Application.Messages.Logic;
using Xunit;

namespace WaGateway.Tests.Messages;

public class WindowPolicyEvaluatorTests
{
    [Theory]
    [InlineData(true, false)]  // CS open
    [InlineData(false, true)]  // only CTWA open
    [InlineData(true, true)]   // both open
    public void FreeForm_send_is_allowed_and_free_when_any_window_is_open(bool csOpen, bool ctwaOpen)
    {
        var result = WindowPolicyEvaluator.Evaluate(MessageTypes.Text, null, csOpen, ctwaOpen);

        Assert.Equal(SendDecision.Allow, result.Decision);
        Assert.False(result.BillableEstimate);
    }

    [Fact]
    public void FreeForm_send_is_rejected_with_WINDOW_CLOSED_when_no_window_is_open()
    {
        var result = WindowPolicyEvaluator.Evaluate(MessageTypes.Text, null, csWindowOpen: false, ctwaWindowOpen: false);

        Assert.Equal(SendDecision.RejectWindowClosed, result.Decision);
        Assert.Null(result.BillableEstimate);
    }

    [Theory]
    [InlineData(MessageTypes.Media)]
    [InlineData(MessageTypes.InteractiveButtons)]
    [InlineData(MessageTypes.InteractiveList)]
    [InlineData(MessageTypes.InteractiveCtaUrl)]
    [InlineData(MessageTypes.InteractiveFlow)]
    [InlineData(MessageTypes.Location)]
    [InlineData(MessageTypes.Contacts)]
    [InlineData(MessageTypes.Reaction)]
    [InlineData(MessageTypes.OrderDetails)]
    public void Every_non_template_type_is_treated_as_free_form(string messageType)
    {
        var result = WindowPolicyEvaluator.Evaluate(messageType, null, csWindowOpen: false, ctwaWindowOpen: false);

        Assert.Equal(SendDecision.RejectWindowClosed, result.Decision);
    }

    [Fact]
    public void Marketing_template_is_always_allowed_and_always_billable()
    {
        var closed = WindowPolicyEvaluator.Evaluate(MessageTypes.Template, "marketing", false, false);
        var open = WindowPolicyEvaluator.Evaluate(MessageTypes.Template, "marketing", true, false);

        Assert.Equal(SendDecision.Allow, closed.Decision);
        Assert.True(closed.BillableEstimate);
        Assert.Equal(SendDecision.Allow, open.Decision);
        Assert.True(open.BillableEstimate);
    }

    [Fact]
    public void Authentication_template_is_always_allowed_and_always_billable_even_inside_the_window()
    {
        // Spec §2.2: authentication templates are the one template category NOT covered by an
        // open window — they are always billable, unlike utility.
        var result = WindowPolicyEvaluator.Evaluate(MessageTypes.Template, "authentication", csWindowOpen: true, ctwaWindowOpen: false);

        Assert.Equal(SendDecision.Allow, result.Decision);
        Assert.True(result.BillableEstimate);
    }

    [Fact]
    public void Utility_template_is_free_inside_the_window()
    {
        var result = WindowPolicyEvaluator.Evaluate(MessageTypes.Template, "utility", csWindowOpen: true, ctwaWindowOpen: false);

        Assert.Equal(SendDecision.Allow, result.Decision);
        Assert.False(result.BillableEstimate);
    }

    [Fact]
    public void Utility_template_is_billable_outside_the_window()
    {
        var result = WindowPolicyEvaluator.Evaluate(MessageTypes.Template, "utility", csWindowOpen: false, ctwaWindowOpen: false);

        Assert.Equal(SendDecision.Allow, result.Decision);
        Assert.True(result.BillableEstimate);
    }
}
