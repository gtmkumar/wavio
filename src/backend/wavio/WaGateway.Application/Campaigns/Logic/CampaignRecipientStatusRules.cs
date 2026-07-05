namespace WaGateway.Application.Campaigns.Logic;

/// <summary>
/// Pure rules for a <see cref="wavio.SharedDataModel.Entities.Messaging.CampaignRecipient"/>'s
/// status lifecycle (issue #22, db/migrations/V013's own status-transition comment: "pending -&gt;
/// sent -&gt; delivered -&gt; read, or suppressed/failed/cancelled"). No I/O.
/// </summary>
public static class CampaignRecipientStatusRules
{
    public const string Pending = "pending";
    public const string Suppressed = "suppressed";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    /// <summary>Terminal-or-in-flight rank used to reject an out-of-order status webhook replay
    /// (e.g. a redelivered "delivered" arriving after "read" already landed) from regressing a
    /// recipient backwards. Higher rank = further along; <see cref="Failed"/>/<see cref="Cancelled"/>/
    /// <see cref="Suppressed"/> are terminal side-branches, not on the main line, so they are not
    /// compared against Delivered/Read at all — <see cref="IsForwardTransition"/> only guards the
    /// Sent → Delivered → Read main line.</summary>
    private static readonly IReadOnlyDictionary<string, int> MainLineRank = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [Sent] = 0,
        [Delivered] = 1,
        [Read] = 2,
    };

    /// <summary>True when applying <paramref name="incoming"/> over <paramref name="current"/> is
    /// a real forward move on the Sent → Delivered → Read main line (so the caller should write it
    /// and count it exactly once), false for a redelivered/out-of-order/no-op webhook. A
    /// <paramref name="current"/> or <paramref name="incoming"/> outside the main line (e.g.
    /// already <see cref="Failed"/>/<see cref="Cancelled"/>/<see cref="Suppressed"/>, or an
    /// incoming "failed"/"sent" status) is handled by the caller separately — this method only
    /// answers the Delivered/Read question.</summary>
    public static bool IsForwardTransition(string current, string incoming)
    {
        if (!MainLineRank.TryGetValue(current, out var currentRank) ||
            !MainLineRank.TryGetValue(incoming, out var incomingRank))
        {
            return false;
        }
        return incomingRank > currentRank;
    }

    /// <summary>A recipient can be terminally failed from Sent or (redundantly) Delivered/Read —
    /// but never re-failed once already Failed/Cancelled/Suppressed (no double-counting a repeat
    /// "failed" webhook).</summary>
    public static bool CanTransitionToFailed(string current) =>
        current is Pending or Sent or Delivered or Read;

    /// <summary>Campaign completion (V013's own comment, taken literally): done once no recipient
    /// remains <see cref="Pending"/> or <see cref="Sent"/> — i.e. every recipient has reached a
    /// terminal outcome (delivered, read, failed, suppressed, or cancelled). A recipient parked at
    /// "sent" with no further delivery/read webhook ever arriving is a real, documented Wave 1 gap
    /// (Meta doesn't guarantee a read receipt) — such a campaign simply never completes without a
    /// manual cancel; see CampaignChunkerService's class doc comment.</summary>
    public static bool IsCampaignComplete(int pendingOrSentCount) => pendingOrSentCount == 0;
}
