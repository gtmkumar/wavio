using System.Text.Json;
using WaIngest.Application.Ingestion.Normalization;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Xunit;

namespace WaIngest.Tests.Ingestion.Normalization;

public class MetaWebhookNormalizerTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Normalize_InboundTextMessage_ProducesMessageReceivedV1()
    {
        var root = Parse("""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "messages": [{
                  "id": "wamid.ABC",
                  "from": "919812345678",
                  "type": "text",
                  "timestamp": "1700000000",
                  "text": { "body": "hello" }
                }]
              }
            }]
          }]
        }
        """);

        var results = MetaWebhookNormalizer.Normalize(root, out var skipped);

        Assert.Empty(skipped);
        var result = Assert.Single(results);
        Assert.Equal("wamid.ABC", result.DedupeKey);
        Assert.Equal(MessageReceivedV1.Name, result.DedupeEventType);

        var evt = Assert.IsType<MessageReceivedV1>(result.Event);
        Assert.Equal("wamid.ABC", evt.Wamid);
        Assert.Equal("919812345678", evt.WaId);
        Assert.Equal("phone-1", evt.PhoneNumberId);
        Assert.Equal("waba-123", evt.WabaId);
        Assert.Equal("text", evt.MessageType);
    }

    [Fact]
    public void Normalize_FlowReply_ProducesFlowResponseV1()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "messages": [{
                  "id": "wamid.FLOW1",
                  "from": "919812345678",
                  "type": "interactive",
                  "timestamp": "1700000000",
                  "interactive": {
                    "type": "nfm_reply",
                    "nfm_reply": { "name": "booking_flow", "response_json": "{\"slot\":\"10am\"}" }
                  }
                }]
              }
            }]
          }]
        }
        """);

        var results = MetaWebhookNormalizer.Normalize(root, out var skipped);

        Assert.Empty(skipped);
        var result = Assert.Single(results);
        Assert.Equal(FlowResponseV1.Name, result.DedupeEventType);

        var evt = Assert.IsType<FlowResponseV1>(result.Event);
        Assert.Equal("booking_flow", evt.FlowId);
        Assert.Equal("wamid.FLOW1", evt.Wamid);
        Assert.Equal("{\"slot\":\"10am\"}", evt.ResponseJson);
    }

    [Fact]
    public void Normalize_MultipleStatusesForSameWamid_HaveDistinctDedupeEventTypes()
    {
        // Same message progressing sent -> delivered must publish twice, not collide in dedupe.
        var sentRoot = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "statuses": [{ "id": "wamid.XYZ", "status": "sent", "timestamp": "1700000000" }]
              }
            }]
          }]
        }
        """);
        var deliveredRoot = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "statuses": [{ "id": "wamid.XYZ", "status": "delivered", "timestamp": "1700000005" }]
              }
            }]
          }]
        }
        """);

        var sentResult = Assert.Single(MetaWebhookNormalizer.Normalize(sentRoot, out _));
        var deliveredResult = Assert.Single(MetaWebhookNormalizer.Normalize(deliveredRoot, out _));

        Assert.Equal("wamid.XYZ", sentResult.DedupeKey);
        Assert.Equal("wamid.XYZ", deliveredResult.DedupeKey);
        Assert.NotEqual(sentResult.DedupeEventType, deliveredResult.DedupeEventType);

        Assert.Equal("sent", Assert.IsType<MessageStatusV1>(sentResult.Event).Status);
        Assert.Equal("delivered", Assert.IsType<MessageStatusV1>(deliveredResult.Event).Status);
    }

    [Fact]
    public void Normalize_FailedStatusWithErrorAndPricing_PopulatesMessageStatusV1Fields()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "statuses": [{
                  "id": "wamid.FAIL1",
                  "status": "failed",
                  "timestamp": "1700000000",
                  "errors": [{ "code": 131047, "title": "Re-engagement required" }],
                  "pricing": { "billable": true, "category": "utility", "pricing_model": "PMP" }
                }]
              }
            }]
          }]
        }
        """);

        var result = Assert.Single(MetaWebhookNormalizer.Normalize(root, out _));
        var evt = Assert.IsType<MessageStatusV1>(result.Event);

        Assert.Equal("failed", evt.Status);
        Assert.Equal(131047, evt.ErrorCode);
        Assert.True(evt.Billable);
        Assert.Equal("utility", evt.PricingCategory);
        Assert.Equal("PMP", evt.PricingModel);
        // No amount/currency/country in this fixture — PMP fields must stay null, not default to 0.
        Assert.Null(evt.Amount);
        Assert.Null(evt.Currency);
        Assert.Null(evt.DestinationMarket);
    }

    [Fact]
    public void Normalize_StatusWithPmpAmount_PopulatesMessageStatusV1PricingFields()
    {
        // Issue #19: nested {"amount": {"value": ..., "currency": ...}} shape.
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "statuses": [{
                  "id": "wamid.PMP1",
                  "status": "delivered",
                  "timestamp": "1700000000",
                  "pricing": {
                    "billable": true,
                    "category": "marketing",
                    "pricing_model": "PMP",
                    "country": "IN",
                    "amount": { "value": 0.87, "currency": "INR" }
                  }
                }]
              }
            }]
          }]
        }
        """);

        var result = Assert.Single(MetaWebhookNormalizer.Normalize(root, out _));
        var evt = Assert.IsType<MessageStatusV1>(result.Event);

        Assert.Equal(0.87m, evt.Amount);
        Assert.Equal("INR", evt.Currency);
        Assert.Equal("IN", evt.DestinationMarket);
        Assert.NotNull(evt.PricingRawJson);
        Assert.Contains("\"amount\"", evt.PricingRawJson);
    }

    [Fact]
    public void Normalize_StatusWithPaymentObject_ProducesPaymentStatusV1()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "statuses": [{
                  "id": "wamid.PAY1",
                  "status": "captured",
                  "timestamp": "1700000000",
                  "payment": {
                    "reference_id": "order-42",
                    "amount": { "value": 15000, "currency": "INR" },
                    "transaction_id": "psp-txn-1"
                  }
                }]
              }
            }]
          }]
        }
        """);

        var result = Assert.Single(MetaWebhookNormalizer.Normalize(root, out _));
        Assert.StartsWith(PaymentStatusV1.Name, result.DedupeEventType);

        var evt = Assert.IsType<PaymentStatusV1>(result.Event);
        Assert.Equal("order-42", evt.ReferenceId);
        Assert.Equal("success", evt.Status);
        Assert.Equal(15000, evt.AmountMinorUnits);
        Assert.Equal("INR", evt.Currency);
        Assert.Equal("psp-txn-1", evt.PspTransactionId);
    }

    [Fact]
    public void Normalize_TemplateStatusUpdate_ProducesTemplateStatusChangedV1()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "message_template_status_update",
              "value": {
                "message_template_id": "tpl-999",
                "message_template_name": "order_ready",
                "event": "REJECTED",
                "reason": "INVALID_FORMAT"
              }
            }]
          }]
        }
        """);

        var result = Assert.Single(MetaWebhookNormalizer.Normalize(root, out _));
        var evt = Assert.IsType<TemplateStatusChangedV1>(result.Event);

        Assert.Equal(Guid.Empty, evt.TemplateId);
        Assert.Equal("tpl-999", evt.MetaTemplateId);
        Assert.Equal("REJECTED", evt.NewStatus);
        Assert.Equal("INVALID_FORMAT", evt.Reason);
        Assert.Equal(64, result.DedupeKey.Length); // SHA-256 hex
    }

    [Fact]
    public void Normalize_TemplateCategoryUpdate_ProducesTemplateCategoryChangedV1()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "message_template_category_update",
              "value": {
                "message_template_id": "tpl-999",
                "previous_category": "utility",
                "new_category": "marketing"
              }
            }]
          }]
        }
        """);

        var result = Assert.Single(MetaWebhookNormalizer.Normalize(root, out _));
        var evt = Assert.IsType<TemplateCategoryChangedV1>(result.Event);

        Assert.Equal("utility", evt.PreviousCategory);
        Assert.Equal("marketing", evt.NewCategory);
    }

    [Fact]
    public void Normalize_PhoneNumberQualityUpdate_ProducesQualityAndTierEvents()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "phone_number_quality_update",
              "value": {
                "phone_number_id": "phone-1",
                "event": "YELLOW",
                "current_limit": "TIER_10K"
              }
            }]
          }]
        }
        """);

        var results = MetaWebhookNormalizer.Normalize(root, out var skipped);

        Assert.Empty(skipped);
        Assert.Equal(2, results.Count);

        var quality = Assert.IsType<QualityChangedV1>(results[0].Event);
        Assert.Equal("YELLOW", quality.CurrentRating);
        Assert.Equal("TIER_10K", quality.MessagingTier);

        var tier = Assert.IsType<TierChangedV1>(results[1].Event);
        Assert.Equal("TIER_10K", tier.NewTier);
        Assert.Null(tier.PreviousTier);

        // Same underlying webhook fragment — same dedupe key, different event_type per event kind.
        Assert.Equal(results[0].DedupeKey, results[1].DedupeKey);
        Assert.NotEqual(results[0].DedupeEventType, results[1].DedupeEventType);
    }

    [Fact]
    public void Normalize_AccountUpdate_ProducesAccountAlertV1WithCriticalSeverityForBan()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "account_update",
              "value": { "event": "ACCOUNT_BANNED", "phone_number": "919812345678" }
            }]
          }]
        }
        """);

        var result = Assert.Single(MetaWebhookNormalizer.Normalize(root, out _));
        var evt = Assert.IsType<AccountAlertV1>(result.Event);

        Assert.Equal("waba-123", evt.WabaId);
        Assert.Equal("ACCOUNT_BANNED", evt.AlertType);
        Assert.Equal("critical", evt.Severity);
    }

    [Fact]
    public void Normalize_StatusValueLongerThanDedupeColumnBudget_TruncatesDedupeKeyButKeepsFullStatusOnEvent()
    {
        // Regression test (security review, N1): ingest.webhook_dedupe.event_type is varchar(50).
        // {routingKey}:{status} must always fit, or the dedupe INSERT fails AFTER a successful
        // publish (WebhookProcessor's ordering) — leaving the row 'failed' and causing a replay
        // to publish a real duplicate. Meta's own status vocabulary never gets close, but this
        // must never depend on that being true.
        var longStatus = new string('x', 60);
        var root = Parse($$"""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{
              "field": "messages",
              "value": {
                "metadata": { "phone_number_id": "phone-1" },
                "statuses": [{ "id": "wamid.LONGSTATUS", "status": "{{longStatus}}", "timestamp": "1700000000" }]
              }
            }]
          }]
        }
        """);

        var result = Assert.Single(MetaWebhookNormalizer.Normalize(root, out _));

        Assert.True(result.DedupeEventType.Length <= 50,
            $"dedupe event_type must fit ingest.webhook_dedupe.event_type varchar(50), was {result.DedupeEventType.Length}");
        Assert.StartsWith(MessageStatusV1.Name + ":", result.DedupeEventType);

        // Only the dedupe KEY is bounded — the published event itself keeps full fidelity.
        Assert.Equal(longStatus, Assert.IsType<MessageStatusV1>(result.Event).Status);
    }

    [Fact]
    public void Normalize_UnrecognizedField_IsSkippedNotThrown()
    {
        var root = Parse("""
        {
          "entry": [{
            "id": "waba-123",
            "changes": [{ "field": "some_future_field", "value": { "whatever": true } }]
          }]
        }
        """);

        var results = MetaWebhookNormalizer.Normalize(root, out var skipped);

        Assert.Empty(results);
        Assert.Single(skipped);
    }

    [Fact]
    public void Normalize_EmptyEnvelope_ReturnsNoEventsAndDoesNotThrow()
    {
        var root = Parse("""{ "object": "whatsapp_business_account" }""");

        var results = MetaWebhookNormalizer.Normalize(root, out var skipped);

        Assert.Empty(results);
        Assert.NotEmpty(skipped);
    }
}
