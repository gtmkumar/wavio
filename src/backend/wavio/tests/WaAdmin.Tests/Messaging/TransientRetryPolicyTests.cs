using System.Data.Common;
using System.Net.Sockets;
using System.Text.Json;
using WaAdmin.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WaAdmin.Tests.Messaging;

/// <summary>
/// Security-review follow-up (S1, issue #16): a brief Postgres/RabbitMQ outage must not
/// permanently dead-letter a legitimate Meta status transition. These tests exercise
/// <see cref="TransientRetryPolicy"/> directly (no real broker/DB needed) — production wiring is
/// in <c>TemplateEventsConsumerBackgroundService.HandleDeliveryAsync</c>.
/// </summary>
public class TransientRetryPolicyTests
{
    // A minimal concrete DbException — the abstract base class is what IsTransient actually
    // checks for (it covers every ADO.NET provider's exception type, including Npgsql's, without
    // this test project needing an Npgsql package reference).
    private sealed class FakeDbException : DbException
    {
        public FakeDbException(string message) : base(message) { }
    }

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task ExecuteAsync_SucceedsFirstTry_ReturnsProcessedWithoutDelay()
    {
        var delayCalls = 0;
        Task Delay(TimeSpan ts, CancellationToken ct) { delayCalls++; return Task.CompletedTask; }

        var outcome = await TransientRetryPolicy.ExecuteAsync(
            () => Task.FromResult(true), Delay, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Processed, outcome);
        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailureThenSuccess_IsRetriedAndEventuallyApplied()
    {
        // Simulates a brief DB outage: fails twice with a transient exception (as if Postgres
        // were briefly unreachable), then succeeds on the third attempt (DB recovered) — proving
        // the message is retried and eventually applied rather than immediately dead-lettered.
        var attempt = 0;
        Task<bool> Operation()
        {
            attempt++;
            if (attempt <= 2) throw new FakeDbException("connection refused (simulated outage)");
            return Task.FromResult(true);
        }

        var delayCalls = new List<TimeSpan>();
        Task Delay(TimeSpan ts, CancellationToken ct) { delayCalls.Add(ts); return Task.CompletedTask; }

        var outcome = await TransientRetryPolicy.ExecuteAsync(Operation, Delay, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.Processed, outcome);
        Assert.Equal(3, attempt);
        Assert.Equal(2, delayCalls.Count);
        Assert.Equal(TransientRetryPolicy.Delays[0], delayCalls[0]);
        Assert.Equal(TransientRetryPolicy.Delays[1], delayCalls[1]);
    }

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public async Task ExecuteAsync_TransientFailureNeverRecovers_RequeuesAfterExhaustingRetries(Exception transientException)
    {
        var attempts = 0;
        Task<bool> Operation() { attempts++; throw transientException; }

        var outcome = await TransientRetryPolicy.ExecuteAsync(Operation, NoDelay, NullLogger.Instance, CancellationToken.None);

        // Requeue, NEVER DeadLetter — an outage that outlasts the in-process retry window must
        // still not permanently lose the message (S1's core requirement).
        Assert.Equal(MessageProcessingOutcome.Requeue, outcome);
        Assert.Equal(TransientRetryPolicy.Delays.Count + 1, attempts);
    }

    public static TheoryData<Exception> TransientExceptions() => new()
    {
        new FakeDbException("connection refused"),
        new TimeoutException("command timeout"),
        new SocketException(),
        new IOException("broken pipe"),
    };

    [Fact]
    public async Task ExecuteAsync_MalformedPayload_DeadLettersImmediatelyWithoutRetrying()
    {
        var attempts = 0;
        Task<bool> Operation() { attempts++; throw new JsonException("Empty body"); }

        var outcome = await TransientRetryPolicy.ExecuteAsync(Operation, NoDelay, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.DeadLetter, outcome);
        Assert.Equal(1, attempts); // permanent failures never retry — would only delay the DLQ trip
    }

    [Fact]
    public async Task ExecuteAsync_HandlerReturnsFalse_DeadLettersImmediatelyWithoutRetrying()
    {
        // Mirrors the command handlers' deterministic "parked" outcome (unresolvable tenant,
        // unknown template, invalid transition) — no exception at all, so there is nothing
        // transient to retry.
        var attempts = 0;
        Task<bool> Operation() { attempts++; return Task.FromResult(false); }

        var outcome = await TransientRetryPolicy.ExecuteAsync(Operation, NoDelay, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(MessageProcessingOutcome.DeadLetter, outcome);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void IsTransient_KnownInfraExceptionTypes_ReturnTrue()
    {
        Assert.True(TransientRetryPolicy.IsTransient(new FakeDbException("x")));
        Assert.True(TransientRetryPolicy.IsTransient(new TimeoutException()));
        Assert.True(TransientRetryPolicy.IsTransient(new SocketException()));
        Assert.True(TransientRetryPolicy.IsTransient(new IOException()));
    }

    [Fact]
    public void IsTransient_TransientExceptionWrappedInNonTransientOuterType_StillDetectedViaInnerException()
    {
        // Regression test for a real bug caught live (issue #16 security-review follow-up): EF's
        // Npgsql provider wraps a genuinely transient connection failure in its own
        // RetryLimitExceededException (not a DbException itself) after its built-in retry
        // strategy gives up. A same-type-only check on the outer exception missed this and
        // dead-lettered a recoverable Postgres outage. Using a plain wrapper here (not the real
        // EF type) to prove the chain-walking fix generically, without a Relational package
        // reference this test project doesn't otherwise need.
        var inner = new FakeDbException("connection refused (simulated outage)");
        var wrapped = new InvalidOperationException("The maximum number of retries was exceeded", inner);

        Assert.True(TransientRetryPolicy.IsTransient(wrapped));
    }

    [Fact]
    public void IsTransient_UnknownOrBusinessExceptionTypes_ReturnFalse()
    {
        Assert.False(TransientRetryPolicy.IsTransient(new JsonException()));
        Assert.False(TransientRetryPolicy.IsTransient(new FormatException()));
        Assert.False(TransientRetryPolicy.IsTransient(new InvalidOperationException()));
    }
}
