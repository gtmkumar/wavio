using System.Text.Json;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Commands.RecordOptOut;
using WaAdmin.Application.Consent.Logic;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace WaAdmin.Infrastructure.Messaging;

/// <summary>
/// STOP-keyword listener (issue #21, spec §4.10): consumes <c>wa.message.received.v1</c> off the
/// shared <c>wavio.events</c> topic exchange (issue #13's producer) and, for a text message whose
/// body matches <see cref="OptOutKeywordMatcher"/>'s vocabulary, records an opt-out + suppression
/// in one unit of work via <see cref="RecordOptOutCommand"/>. Same dead-letter-queue/transient-
/// retry/per-attempt-DI-scope shape as <see cref="TemplateEventsConsumerBackgroundService"/> (the
/// most recent WaAdmin consumer, issue #16) — reused deliberately rather than WaIntel's simpler
/// ack-always-and-log-on-failure shape (issue #15), since a STOP keyword is a compliance-bearing
/// event and losing one silently on a transient DB blip is a worse outcome here than for a window
/// upsert.
///
/// Tenant resolution (<see cref="ITenantResolver"/>) happens BEFORE dispatching the command, same
/// as WaIntel's <c>MessageReceivedConsumerService</c> — this event carries Meta's raw
/// <c>phone_number_id</c>, not an internal tenant id. An unresolvable event is treated as
/// "processed" (acked, not dead-lettered): Wave 1 reality is that <c>waba.phone_numbers</c> may
/// be empty for a given number before onboarding completes, and requeuing would just spin forever
/// on the same unresolvable lookup.
/// </summary>
public sealed partial class StopKeywordConsumerService : BackgroundService
{
    public const string ExchangeName = "wavio.events";
    public const string QueueName = "wa-admin.stop-keyword";
    public const string DeadLetterExchangeName = "wavio.events.dlx";
    public const string DeadLetterQueueName = "wa-admin.stop-keyword.dlq";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StopKeywordConsumerService> _logger;

    public StopKeywordConsumerService(
        RabbitMqConnectionManager connectionManager, IServiceScopeFactory scopeFactory,
        ILogger<StopKeywordConsumerService> logger)
    {
        _connectionManager = connectionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogConsumerLoopFailed(_logger, ex);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var channel = await _connectionManager.CreateChannelAsync(stoppingToken);

        await channel.ExchangeDeclareAsync(
            ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(
            DeadLetterExchangeName, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            DeadLetterQueueName, DeadLetterExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);

        var queueArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchangeName };
        await channel.QueueDeclareAsync(
            QueueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs,
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            QueueName, ExchangeName, routingKey: MessageReceivedV1.Name, cancellationToken: stoppingToken);

        await channel.BasicQosAsync(0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleDeliveryAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        var bodyBytes = ea.Body.ToArray();

        MessageProcessingOutcome outcome;
        try
        {
            outcome = await TransientRetryPolicy.ExecuteAsync(
                () => ProcessOnceAsync(bodyBytes, stoppingToken),
                Task.Delay,
                _logger,
                stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        switch (outcome)
        {
            case MessageProcessingOutcome.Processed:
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                break;
            case MessageProcessingOutcome.Requeue:
                LogRequeued(_logger);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
                break;
            case MessageProcessingOutcome.DeadLetter:
            default:
                LogDeadLettered(_logger);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                break;
        }
    }

    /// <summary>One attempt. Returns false ("parked", not retryable) for: malformed body, a
    /// non-text message, no keyword match, or an unresolvable tenant — none of these should ever
    /// land in the DLQ via a retry, they are the expected steady-state outcome for the vast
    /// majority of inbound messages (which are not opt-out keywords at all).</summary>
    private async Task<bool> ProcessOnceAsync(byte[] body, CancellationToken cancellationToken)
    {
        var evt = JsonSerializer.Deserialize<MessageReceivedV1>(body, JsonOptions);
        if (evt is null)
        {
            LogMalformed(_logger);
            return true; // nothing to retry — a permanently malformed body would never parse
        }

        var match = OptOutKeywordMatcher.TryMatch(evt.Text);
        if (match is null)
        {
            return true; // the overwhelming common case: not an opt-out message
        }

        using var scope = _scopeFactory.CreateScope();
        var tenantResolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ScopedCurrentTenant>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var resolved = await tenantResolver.ResolveAsync(evt.PhoneNumberId, cancellationToken);
        if (resolved is null)
        {
            LogUnresolvedTenant(_logger, evt.PhoneNumberId);
            return true; // parked, not a failure — see class doc comment
        }

        tenantContext.OverrideTenantId = resolved.TenantId;
        await dispatcher.SendAsync(
            new RecordOptOutCommand(
                resolved.TenantId, evt.WaId, Scope: "marketing", Reason: "stop_keyword",
                match.Value.Keyword, match.Value.Language, evt.Wamid, PayloadJson: null, ActorId: null),
            cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "STOP-keyword consumer loop failed; reconnecting in 5s")]
    private static partial void LogConsumerLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Received a wa.message.received.v1 delivery that failed to deserialize — acking to drop it")]
    private static partial void LogMalformed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No waba.phone_numbers row for Meta phone_number_id {MetaPhoneNumberId} — parking STOP-keyword event")]
    private static partial void LogUnresolvedTenant(ILogger logger, string metaPhoneNumberId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Requeueing a wa.message.received.v1 delivery after exhausting in-process retries (transient failure)")]
    private static partial void LogRequeued(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettering a wa.message.received.v1 delivery (permanent failure)")]
    private static partial void LogDeadLettered(ILogger logger);
}
