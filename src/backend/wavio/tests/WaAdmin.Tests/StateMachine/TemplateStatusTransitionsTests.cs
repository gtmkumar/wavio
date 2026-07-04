using WaAdmin.Application.Templates.StateMachine;
using Xunit;

namespace WaAdmin.Tests.StateMachine;

/// <summary>Exhaustive table-driven coverage of the template status state machine (issue #16):
/// every legal edge must return true, and — because CanTransition is the sole gate the
/// Application handlers rely on to "guard invalid transitions" — every OTHER (from, to) pair
/// among the six known statuses must return false.</summary>
public class TemplateStatusTransitionsTests
{
    public static readonly TheoryData<string, string> LegalTransitions = new()
    {
        { TemplateStatusTransitions.Draft, TemplateStatusTransitions.Pending },
        { TemplateStatusTransitions.Pending, TemplateStatusTransitions.Approved },
        { TemplateStatusTransitions.Pending, TemplateStatusTransitions.Rejected },
        { TemplateStatusTransitions.Approved, TemplateStatusTransitions.Paused },
        { TemplateStatusTransitions.Approved, TemplateStatusTransitions.Disabled },
        { TemplateStatusTransitions.Approved, TemplateStatusTransitions.Draft },
        { TemplateStatusTransitions.Paused, TemplateStatusTransitions.Approved },
        { TemplateStatusTransitions.Paused, TemplateStatusTransitions.Disabled },
        { TemplateStatusTransitions.Paused, TemplateStatusTransitions.Draft },
        { TemplateStatusTransitions.Rejected, TemplateStatusTransitions.Draft },
    };

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void CanTransition_LegalEdge_ReturnsTrue(string from, string to)
    {
        Assert.True(TemplateStatusTransitions.CanTransition(from, to));
    }

    public static TheoryData<string, string> AllIllegalPairs()
    {
        var data = new TheoryData<string, string>();
        var legal = LegalTransitions.Select(row => ((string)row[0]!, (string)row[1]!)).ToHashSet();

        foreach (var from in TemplateStatusTransitions.AllStatuses)
        foreach (var to in TemplateStatusTransitions.AllStatuses)
        {
            if (from == to) continue; // same-status is covered by a dedicated test below
            if (legal.Contains((from, to))) continue;
            data.Add(from, to);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllIllegalPairs))]
    public void CanTransition_IllegalEdge_ReturnsFalse(string from, string to)
    {
        Assert.False(TemplateStatusTransitions.CanTransition(from, to));
    }

    [Theory]
    [InlineData(TemplateStatusTransitions.Draft)]
    [InlineData(TemplateStatusTransitions.Pending)]
    [InlineData(TemplateStatusTransitions.Approved)]
    [InlineData(TemplateStatusTransitions.Rejected)]
    [InlineData(TemplateStatusTransitions.Paused)]
    [InlineData(TemplateStatusTransitions.Disabled)]
    public void CanTransition_SameStatus_ReturnsFalse(string status)
    {
        Assert.False(TemplateStatusTransitions.CanTransition(status, status));
    }

    [Fact]
    public void CanTransition_Disabled_IsTerminal()
    {
        foreach (var to in TemplateStatusTransitions.AllStatuses)
            Assert.False(TemplateStatusTransitions.CanTransition(TemplateStatusTransitions.Disabled, to));
    }

    [Theory]
    [InlineData("draft")]      // wrong case
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public void IsValidStatus_UnknownOrWrongCase_ReturnsFalse(string status)
    {
        Assert.False(TemplateStatusTransitions.IsValidStatus(status));
    }

    [Theory]
    [InlineData(TemplateStatusTransitions.Draft)]
    [InlineData(TemplateStatusTransitions.Pending)]
    [InlineData(TemplateStatusTransitions.Approved)]
    [InlineData(TemplateStatusTransitions.Rejected)]
    [InlineData(TemplateStatusTransitions.Paused)]
    [InlineData(TemplateStatusTransitions.Disabled)]
    public void IsValidStatus_KnownStatus_ReturnsTrue(string status)
    {
        Assert.True(TemplateStatusTransitions.IsValidStatus(status));
    }
}
