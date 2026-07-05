using WaGateway.Application.Common.Interfaces;
using WaGateway.Application.Messages.Commands.SendMessage;
using Moq;
using wavio.SharedDataModel.Entities.Waba;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaGateway.Tests.Messages;

public class SendMessageHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();
    private const string ToWaId = "919876543210";

    private static Mock<IWindowStateClient> WindowStateClient(bool csOpen, bool ctwaOpen = false)
    {
        var mock = new Mock<IWindowStateClient>();
        mock.Setup(c => c.GetWindowStateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WindowStateResult(csOpen, ctwaOpen));
        return mock;
    }

    /// <summary>Seeds the phone-number-ownership row the handler's S3 check requires (security
    /// review, PR #45, S3) — every test below that expects a send to actually proceed needs its
    /// (tenantId, phoneNumberId) pair to exist first, mirroring how a real tenant would already
    /// have completed WABA onboarding before sending.</summary>
    private static void SeedPhoneNumber(InMemoryWaGatewayDbContext db, Guid tenantId, Guid phoneNumberId)
    {
        db.WabaPhoneNumbers.Add(new WabaPhoneNumber
        {
            Id = phoneNumberId,
            TenantId = tenantId,
            BusinessAccountId = Guid.NewGuid(),
            MetaPhoneNumberId = phoneNumberId.ToString("N"),
            DisplayPhoneNumber = "+1 555 0100",
            Status = "CONNECTED",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        });
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task A_free_form_send_with_an_open_window_is_accepted_and_writes_an_outbox_entry()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_free_form_send_with_an_open_window_is_accepted_and_writes_an_outbox_entry));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        var result = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "key-1"),
            CancellationToken.None);

        Assert.Equal("accepted", result.Status);
        Assert.Null(result.Wamid);
        Assert.False(result.BillableEstimate);
        Assert.Single(db.OutboundMessages);
        Assert.Single(db.OutboundOutboxEntries);
        Assert.Equal(result.Id, db.OutboundOutboxEntries.Single().OutboundMessageId);
    }

    [Fact]
    public async Task A_free_form_send_with_no_open_window_is_rejected_and_writes_no_outbox_entry()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_free_form_send_with_no_open_window_is_rejected_and_writes_no_outbox_entry));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: false).Object);

        var result = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "key-2"),
            CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("WINDOW_CLOSED", result.ErrorCode);
        Assert.Single(db.OutboundMessages); // recorded for idempotency, per V007's status comment
        Assert.Empty(db.OutboundOutboxEntries); // never dispatched
    }

    [Fact]
    public async Task A_marketing_template_send_is_accepted_regardless_of_window_state_and_flagged_billable()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_marketing_template_send_is_accepted_regardless_of_window_state_and_flagged_billable));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: false).Object);

        var result = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "template",
                """{"name":"promo","language":"en_US","category":"marketing"}""", "key-3"),
            CancellationToken.None);

        Assert.Equal("accepted", result.Status);
        Assert.True(result.BillableEstimate);
        Assert.Single(db.OutboundOutboxEntries);
    }

    [Fact]
    public async Task A_malformed_payload_throws_ValidationException_and_persists_nothing()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_malformed_payload_throws_ValidationException_and_persists_nothing));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":""}""", "key-4"),
            CancellationToken.None));

        Assert.Empty(db.OutboundMessages);
    }

    [Fact]
    public async Task A_repeated_Idempotency_Key_returns_the_original_result_without_creating_a_second_row()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_repeated_Idempotency_Key_returns_the_original_result_without_creating_a_second_row));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        var first = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "same-key"),
            CancellationToken.None);

        // A different body/type on the retry — idempotency must still return the ORIGINAL
        // result, ignoring what the retried request actually contained (this is what "the client
        // retried the exact same logical request" means in practice: same key wins).
        var second = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"a different body"}""", "same-key"),
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(db.OutboundMessages);
        Assert.Single(db.OutboundOutboxEntries);
    }

    [Fact]
    public async Task A_repeated_Idempotency_Key_on_a_previously_rejected_send_returns_the_same_rejection()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_repeated_Idempotency_Key_on_a_previously_rejected_send_returns_the_same_rejection));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: false).Object);

        var first = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "rejected-key"),
            CancellationToken.None);

        var second = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "rejected-key"),
            CancellationToken.None);

        Assert.Equal("rejected", first.Status);
        Assert.Equal("rejected", second.Status);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(db.OutboundMessages); // still just one row, not two rejections
    }

    [Fact]
    public async Task Idempotency_keys_are_scoped_per_tenant_not_global()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(Idempotency_keys_are_scoped_per_tenant_not_global));
        var otherTenantId = Guid.NewGuid();
        var otherTenantPhoneNumberId = Guid.NewGuid(); // a phone number id belongs to exactly one tenant, never shared
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        SeedPhoneNumber(db, otherTenantId, otherTenantPhoneNumberId);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        var forTenantA = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "shared-key"),
            CancellationToken.None);
        var forTenantB = await handler.HandleAsync(
            new SendMessageCommand(otherTenantId, otherTenantPhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "shared-key"),
            CancellationToken.None);

        Assert.NotEqual(forTenantA.Id, forTenantB.Id);
        Assert.Equal(2, db.OutboundMessages.Count());
    }

    [Fact]
    public async Task A_send_to_a_phone_number_id_with_no_matching_row_throws_KeyNotFoundException()
    {
        // Regression test (security review, PR #45, S3): a nonexistent/foreign PhoneNumberId
        // used to be silently accepted (202) and only fail asynchronously in the dispatcher,
        // minutes later, as an UNRESOLVED_PHONE_NUMBER dead-letter with no feedback to the caller.
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_send_to_a_phone_number_id_with_no_matching_row_throws_KeyNotFoundException));
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);
        var unregisteredPhoneNumberId = Guid.NewGuid(); // never seeded

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(
            new SendMessageCommand(TenantId, unregisteredPhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "key-unowned"),
            CancellationToken.None));

        Assert.Empty(db.OutboundMessages);
    }

    [Fact]
    public async Task A_marketing_template_send_to_a_suppressed_recipient_is_rejected_and_writes_no_outbox_entry()
    {
        // Deny-wins pre-dispatch suppression gate (issue #21, spec §4.10).
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_marketing_template_send_to_a_suppressed_recipient_is_rejected_and_writes_no_outbox_entry));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        db.SuppressionListEntries.Add(new wavio.SharedDataModel.Entities.Messaging.SuppressionListEntry
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            WaId = ToWaId,
            Reason = "stop_keyword",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        var result = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "template",
                """{"name":"promo","language":"en_US","category":"marketing"}""", "key-suppressed"),
            CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("SUPPRESSED", result.ErrorCode);
        Assert.Empty(db.OutboundOutboxEntries);
    }

    [Fact]
    public async Task A_marketing_template_send_to_a_suppressed_recipient_with_an_expired_suppression_is_accepted()
    {
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_marketing_template_send_to_a_suppressed_recipient_with_an_expired_suppression_is_accepted));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        db.SuppressionListEntries.Add(new wavio.SharedDataModel.Entities.Messaging.SuppressionListEntry
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            WaId = ToWaId,
            Reason = "manual",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), // expired
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        var result = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "template",
                """{"name":"promo","language":"en_US","category":"marketing"}""", "key-expired-suppression"),
            CancellationToken.None);

        Assert.Equal("accepted", result.Status);
        Assert.Single(db.OutboundOutboxEntries);
    }

    [Fact]
    public async Task A_suppressed_recipient_still_receives_utility_template_sends()
    {
        // Suppression means "no MARKETING" specifically (spec §4.10) — utility/authentication/
        // service sends are never blocked by it.
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_suppressed_recipient_still_receives_utility_template_sends));
        SeedPhoneNumber(db, TenantId, PhoneNumberId);
        db.SuppressionListEntries.Add(new wavio.SharedDataModel.Entities.Messaging.SuppressionListEntry
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            WaId = ToWaId,
            Reason = "stop_keyword",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        var result = await handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "template",
                """{"name":"otp","language":"en_US","category":"utility"}""", "key-utility"),
            CancellationToken.None);

        Assert.Equal("accepted", result.Status);
        Assert.Single(db.OutboundOutboxEntries);
    }

    [Fact]
    public async Task A_send_to_another_tenants_phone_number_id_throws_KeyNotFoundException()
    {
        // Regression test (security review, PR #45, S3): the row exists, but not for THIS
        // tenant — must be rejected the same way as a nonexistent phone number, not leak that
        // the id belongs to someone else.
        await using var db = InMemoryWaGatewayDbContext.Create(nameof(A_send_to_another_tenants_phone_number_id_throws_KeyNotFoundException));
        var otherTenantId = Guid.NewGuid();
        SeedPhoneNumber(db, otherTenantId, PhoneNumberId); // owned by a DIFFERENT tenant
        var handler = new SendMessageHandler(db, WindowStateClient(csOpen: true).Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(
            new SendMessageCommand(TenantId, PhoneNumberId, ToWaId, "text", """{"body":"hi"}""", "key-foreign"),
            CancellationToken.None));

        Assert.Empty(db.OutboundMessages);
    }
}
