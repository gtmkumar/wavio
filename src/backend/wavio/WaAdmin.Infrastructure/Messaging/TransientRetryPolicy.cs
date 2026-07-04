using System.Data.Common;
using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace WaAdmin.Infrastructure.Messaging;

/// <summary>Result of attempting to process one message via <see cref="TransientRetryPolicy.ExecuteAsync"/>.</summary>
public enum MessageProcessingOutcome
{
    /// <summary>The command handler ran and returned true — ack the delivery.</summary>
    Processed,

    /// <summary>A transient (infra-shaped) failure survived every in-process retry — nack WITH
    /// requeue so the message stays on the live queue for a later redelivery once the
    /// dependency recovers. Never dead-lettered: retrying is exactly what should eventually
    /// fix this class of failure (security review S1, issue #16 follow-up).</summary>
    Requeue,

    /// <summary>Either a permanent failure (malformed payload) or a deterministic business
    /// "parked" outcome (unresolvable tenant, unknown template, invalid transition — the command
    /// handler returned false, not an exception) — nack WITHOUT requeue, landing in the DLQ.
    /// Retrying either case would never succeed.</summary>
    DeadLetter,
}

/// <summary>
/// Classifies exceptions from a single message-processing attempt as transient (infra: DB/network
/// blips, worth a few in-process retries with backoff) vs. permanent, and drives the bounded
/// retry loop. Extracted from <see cref="TemplateEventsConsumerBackgroundService"/> so it can be
/// unit-tested without a real RabbitMQ broker or Postgres instance (issue #16 security-review
/// follow-up, S1: a brief DB outage must not permanently dead-letter a legitimate Meta status
/// transition).
/// </summary>
public static partial class TransientRetryPolicy
{
    /// <summary>Backoff between in-process retry attempts for a transient failure. Three
    /// retries (~7s total) covers a "brief" outage (the scenario S1 is about) without needing a
    /// broker round-trip; a longer outage falls through to <see cref="MessageProcessingOutcome.Requeue"/>
    /// so the message is never lost — it just waits for the next redelivery.</summary>
    public static readonly IReadOnlyList<TimeSpan> Delays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <summary>
    /// Infra-shaped exceptions worth retrying in-process. Deliberately NOT the same thing as the
    /// command handlers' "parked" business outcome (unresolvable tenant, unknown template,
    /// invalid transition) — those are signaled by a <c>false</c> return value, not an exception,
    /// and retrying them would never succeed (see <see cref="MessageProcessingOutcome.DeadLetter"/>).
    /// Anything not recognized here (malformed JSON, a genuine code bug) is treated as permanent —
    /// retrying a poison message would only delay its (still necessary) trip to the DLQ.
    ///
    /// Walks the full <see cref="Exception.InnerException"/> chain, not just the outermost type:
    /// confirmed live (issue #16 security-review follow-up) that a real Postgres outage surfaces
    /// as EF's own <c>Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException</c> —
    /// EF's Npgsql provider has a built-in retrying execution strategy that already retries a
    /// few times internally before giving up and wrapping the real (transient) Npgsql/socket
    /// exception in that non-<see cref="DbException"/> type. A same-type-only check missed this
    /// entirely and dead-lettered a perfectly recoverable outage — do not revert to that.
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (IsTransientType(current)) return true;
        }
        return false;
    }

    private static bool IsTransientType(Exception ex) => ex switch
    {
        DbException => true,
        TimeoutException => true,
        SocketException => true,
        IOException => true,
        _ => false,
    };

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying up to <see cref="Delays"/>.Count times (with
    /// backoff via <paramref name="delay"/>) when it throws a transient exception. A permanent
    /// exception, or a <c>false</c> return with no exception at all (the handlers' deterministic
    /// "parked" signal), short-circuits immediately — no wasted retries.
    /// </summary>
    /// <param name="operation">One attempt at processing the message; returns whether it was
    /// applied (mirrors the command handlers' `Task&lt;bool&gt;`).</param>
    /// <param name="delay">Injected so tests can skip real waiting; production passes
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public static async Task<MessageProcessingOutcome> ExecuteAsync(
        Func<Task<bool>> operation,
        Func<TimeSpan, CancellationToken, Task> delay,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var applied = await operation();
                return applied ? MessageProcessingOutcome.Processed : MessageProcessingOutcome.DeadLetter;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!IsTransient(ex))
                {
                    LogPermanentFailure(logger, ex);
                    return MessageProcessingOutcome.DeadLetter;
                }

                if (attempt >= Delays.Count)
                {
                    LogTransientRetriesExhausted(logger, ex, attempt);
                    return MessageProcessingOutcome.Requeue;
                }

                LogTransientRetrying(logger, ex, attempt + 1, Delays.Count);
                await delay(Delays[attempt], cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Transient failure processing message (attempt {Attempt}/{MaxAttempts}) — retrying")]
    private static partial void LogTransientRetrying(ILogger logger, Exception exception, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Transient failure processing message survived all {AttemptsMade} in-process retries — requeueing for later redelivery")]
    private static partial void LogTransientRetriesExhausted(ILogger logger, Exception exception, int attemptsMade);

    [LoggerMessage(Level = LogLevel.Error, Message = "Permanent failure processing message — parking to DLQ")]
    private static partial void LogPermanentFailure(ILogger logger, Exception exception);
}
