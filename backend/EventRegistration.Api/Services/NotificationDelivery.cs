namespace EventRegistration.Api.Services;

/// <summary>
/// One channel in a fallback chain: its display name ("email", "Viber", "SMS") and its send.
/// <paramref name="AfterFailure"/> false = not tried when the previous channel really failed
/// (still tried when it was skipped or turned off).
/// </summary>
public sealed record ChannelAttempt(string Channel, Func<Task<DeliveryResult>> Send, bool AfterFailure = true);

/// <summary>What a chain did: the channel that delivered (null when none did) and every try in order.</summary>
public sealed record ChainResult(string? DeliveredBy, IReadOnlyList<(string Channel, DeliveryResult Result)> Tried)
{
    public bool Delivered => DeliveredBy is not null;
    // The delivering channel's message went to outbox/ because the channel is off (Development).
    public bool DeliveredDisabled => Delivered && Tried[^1].Result == DeliveryResult.Disabled;
    public string? FirstFailed => Tried.Where(t => t.Result == DeliveryResult.Failed).Select(t => t.Channel).FirstOrDefault();
}

/// <summary>
/// Tries each channel once, in order, and stops at the first that delivers. A failed or turned-off
/// channel moves on to the next one at once (a failure only when the next allows it); there are
/// no in-process retries.
/// </summary>
public class NotificationDelivery(ILogger<NotificationDelivery> log)
{
    public async Task<ChainResult> DeliverAsync(IEnumerable<ChannelAttempt> chain)
    {
        var tried = new List<(string, DeliveryResult)>();
        foreach (var attempt in chain)
        {
            if (!attempt.AfterFailure && tried.Count > 0 && tried[^1].Item2 == DeliveryResult.Failed)
            {
                log.LogWarning("{Channel} fallback is off; not sent.", tried[^1].Item1);
                break;
            }
            var result = await attempt.Send();
            tried.Add((attempt.Channel, result));
            if (result.Delivered()) return new ChainResult(attempt.Channel, tried);
            log.LogWarning("{Channel} {Result}; trying the next channel.", attempt.Channel,
                result == DeliveryResult.Failed ? "failed" : "is off");
        }
        return new ChainResult(null, tried);
    }
}
