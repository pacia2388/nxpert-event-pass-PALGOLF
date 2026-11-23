using System.ComponentModel.DataAnnotations;

namespace EventRegistration.Api.Models;

/// <summary>
/// A Viber message Sinch accepted, with the last delivery status Sinch reported by webhook.
/// <see cref="RegistrationId"/> and <see cref="Kind"/> are set only on the message that decides a
/// registrant's delivery when a later failure must send the email instead (see MobileTag);
/// <see cref="EmailQueuedAt"/> makes that email queue once. No foreign key: kept as an audit trail.
/// </summary>
public class ViberMessage
{
    public const string Accepted = "ACCEPTED";

    [Key, MaxLength(100)] public string MessageId { get; set; } = "";
    public int? RegistrationId { get; set; }
    [MaxLength(20)] public string? Kind { get; set; }
    [MaxLength(40)] public string Phone { get; set; } = "";
    // ACCEPTED, then Sinch's QUEUED_ON_CHANNEL, DELIVERED, READ, FAILED or SWITCHING_CHANNEL.
    [MaxLength(30)] public string Status { get; set; } = Accepted;
    [MaxLength(100)] public string? ReasonCode { get; set; }
    public DateTimeOffset AcceptedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EmailQueuedAt { get; set; }
}
