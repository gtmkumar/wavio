using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using wavio.ServiceDefaults.Logging;
using Xunit;

namespace WaIntel.Tests.Logging;

public class WaPiiMaskTests
{
    [Fact]
    public void MaskWaId_masks_all_but_the_last_4_digits()
    {
        Assert.Equal("••••••••5678", WaPiiMask.MaskWaId("919812345678"));
    }

    [Fact]
    public void MaskDigitRunsInPath_masks_an_embedded_waId_and_leaves_the_rest_of_the_path_alone()
    {
        // Regression test (issue #15 security review, S2): GET /v1/windows/{waId}'s raw wa_id
        // landed unmasked in ASP.NET Core's own request-start/finish logs and OTel spans, since
        // neither goes through a call site or a WaId-named property.
        var masked = WaPiiMask.MaskDigitRunsInPath("/api/v1/windows/919876543210");

        Assert.Equal("/api/v1/windows/••••••••3210", masked);
    }

    [Fact]
    public void MaskDigitRunsInPath_leaves_short_numeric_ids_untouched()
    {
        // A short numeric route segment (e.g. a port number or a small count) is not wa_id-shaped
        // (E.164 is 10-15 digits) — must not be masked away as collateral damage.
        var masked = WaPiiMask.MaskDigitRunsInPath("/api/v1/templates/42");

        Assert.Equal("/api/v1/templates/42", masked);
    }

    [Fact]
    public void MaskDigitRunsInPath_masks_multiple_embedded_waIds_independently()
    {
        var masked = WaPiiMask.MaskDigitRunsInPath("/api/v1/conversations/919876543210/messages/919111222333");

        Assert.Equal("/api/v1/conversations/••••••••3210/messages/••••••••2333", masked);
    }

    [Fact]
    public void MaskDigitRunsInPath_handles_null_and_empty()
    {
        Assert.Equal(string.Empty, WaPiiMask.MaskDigitRunsInPath(null));
        Assert.Equal(string.Empty, WaPiiMask.MaskDigitRunsInPath(string.Empty));
    }
}

public class WaIdMaskingEnricherTests
{
    private static LogEvent MakeLogEvent(params (string Name, string Value)[] properties)
    {
        var props = properties.Select(p => new LogEventProperty(p.Name, new ScalarValue(p.Value)));
        return new LogEvent(
            DateTimeOffset.UtcNow, LogEventLevel.Information,
            null, new MessageTemplateParser().Parse("test"), props);
    }

    private static string? GetProperty(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string s } ? s : null;

    [Fact]
    public void Enrich_masks_an_embedded_waId_in_a_Path_property()
    {
        var logEvent = MakeLogEvent(("Path", "/api/v1/windows/919876543210"));
        var enricher = new WaIdMaskingEnricher();

        enricher.Enrich(logEvent, new LogEventPropertyFactoryStub());

        Assert.Equal("/api/v1/windows/••••••••3210", GetProperty(logEvent, "Path"));
    }

    [Fact]
    public void Enrich_leaves_a_Path_with_no_waId_shaped_segment_untouched()
    {
        var logEvent = MakeLogEvent(("Path", "/api/v1/templates/42"));
        var enricher = new WaIdMaskingEnricher();

        enricher.Enrich(logEvent, new LogEventPropertyFactoryStub());

        Assert.Equal("/api/v1/templates/42", GetProperty(logEvent, "Path"));
    }

    [Fact]
    public void Enrich_still_masks_a_named_WaId_property_in_full()
    {
        var logEvent = MakeLogEvent(("WaId", "919876543210"));
        var enricher = new WaIdMaskingEnricher();

        enricher.Enrich(logEvent, new LogEventPropertyFactoryStub());

        Assert.Equal("••••••••3210", GetProperty(logEvent, "WaId"));
    }

    private sealed class LogEventPropertyFactoryStub : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
