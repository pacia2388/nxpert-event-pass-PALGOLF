using System.ComponentModel.DataAnnotations;

namespace EventRegistration.Api.Models;

public static class EmailRetryKinds
{
    public const string Pass = "Pass";
    public const string KitClaimed = "KitClaimed";
    public const string EditLink = "EditLink";
    // A single-use "set your password" link sent to an account with no password.
    public const string PasswordSetup = "PasswordSetup";
    // The one-time post-event feedback link.
    public const string FeedbackRequest = "FeedbackRequest";
    public const string MeetingUpdate = "MeetingUpdate";
    public const string MeetingCancel = "MeetingCancel";
    // Any other email sent through EmailSender (or a broadcast's fallback): the whole email is kept
    // as an .eml file (EmailFile). Rows queued before that keep it as JSON in SnapshotJson.
    public const string Message = "Message";
    // A mail-plan day / reminder email after a late Viber failure: SnapshotJson keeps only the
    // delivery (MailPlanEmailRef); the email is rebuilt from the plan, since pasted images make copies large.
    public const string MailPlan = "MailPlan";
    public const string MailReminder = "MailReminder";
}

public static class EmailRetryStatus
{
    public const string Queued = "Queued";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

/// <summary>
/// A registrant email that failed and is retried in the background (5×, every 5 min). Only the
/// registration and message kind are kept; the email is rebuilt at each try, so no pass or body is
/// stored. A meeting cancel is sent after its registration is deleted, so it keeps a small snapshot
/// of the calendar entry instead (no QR). No foreign key: the registration may be gone.
/// </summary>
public class EmailRetry
{
    public int Id { get; set; }
    public int? RegistrationId { get; set; }
    [MaxLength(20)] public string Kind { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = EmailRetryStatus.Queued;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    [MaxLength(500)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    // MeetingCancel: the calendar entry, attendee name and reason as JSON.
    // MailPlan / MailReminder: the receiver's delivery (MailPlanEmailRef) as JSON.
    // Message (older rows only): the whole email (QueuedEmail) as JSON.
    public string? SnapshotJson { get; set; }
    // Message: the file name of the saved email (EmailFileStore), never its content.
    [MaxLength(100)] public string? EmailFile { get; set; }
}
