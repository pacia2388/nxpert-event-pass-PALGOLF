using System.ComponentModel.DataAnnotations;

namespace EventRegistration.Api.Models;

/// <summary>
/// A Viber message Sinch accepted, with the last delivery status Sinch reported by webhook.
/// <see cref="Kind"/> (and <see cref="RegistrationId"/> for a registrant) is set only when a later
/// failure must send the email instead (see MobileTag). All parts of one send share a
/// <see cref="GroupId"/>; <see cref="EmailQueuedAt"/> is stamped on the whole group so its email
/// queues once. With no registration, the group's first part says what to email:
/// <see cref="EmailSnapshotJson"/> (a mail-plan delivery reference) or <see cref="EmailFile"/> (the
/// broadcast's email, saved as an .eml file; only its name is stored). No foreign key: kept as an audit trail.
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
    public Guid GroupId { get; set; }
    public string? EmailSnapshotJson { get; set; }
    [MaxLength(100)] public string? EmailFile { get; set; }
}
