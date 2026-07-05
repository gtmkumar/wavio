using WaGateway.Application.Common.Interfaces;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// Hand-rolled <see cref="IMetaGraphMessageClient"/> fake whose <see cref="SendAsync"/> call can
/// be paused mid-flight via <see cref="Gate"/> — the ONE genuine external boundary
/// <see cref="WaGateway.Infrastructure.BackgroundWork.OutboxDispatcherService"/> depends on (Meta's
/// real Graph API), so faking it is the legitimate "mock only where an interface genuinely needs
/// it" case CLAUDE.md allows, not a shortcut around the database layer under test.
///
/// The pause is what makes OutboxDispatcherFencedWriteTests' race DETERMINISTIC rather than a
/// flaky true-multithreaded race: the test claims the lease, starts this call, steals the lease
/// out from under it (backdating locked_at + a second instance's real LeaseNextBatchAsync), THEN
/// releases the gate so the original call's fenced completion write executes against a lease it
/// no longer holds — reliably exercising the "0 rows affected -> discard" path every run.
/// </summary>
public sealed class ControllableGraphClient : IMetaGraphMessageClient
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Result returned once <see cref="Release"/> is called. Defaults to a successful send.</summary>
    public GraphSendResult ResultOnRelease { get; set; } = new(true, $"wamid.itest-{Guid.NewGuid():N}", false, null, null);

    /// <summary>Completes once <see cref="SendAsync"/> has been entered — await this before
    /// stealing the lease, so the steal reliably happens while the call is genuinely in flight.</summary>
    public Task CallStarted => _callStarted.Task;

    public async Task<GraphSendResult> SendAsync(GraphSendRequest request, CancellationToken cancellationToken)
    {
        _callStarted.TrySetResult();
        await _gate.Task.WaitAsync(cancellationToken);
        return ResultOnRelease;
    }

    /// <summary>Lets the paused <see cref="SendAsync"/> call return.</summary>
    public void Release() => _gate.TrySetResult();
}
