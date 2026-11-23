namespace EventRegistration.Api.Services;

/// <summary>
/// One registrant / receiver message, defined once and sent through <see cref="MessageDispatcher"/>:
/// its SMS parts, its Viber parts and its email. The mobile parts of one message go out in order as
/// one group; a rejected part fails the whole message (then the email fallback runs).
/// <paramref name="Kind"/> is an <c>EmailRetryKinds</c> value (retry rebuild and Viber tag).
/// <paramref name="Email"/> sends the email (built only when it is sent); null = no address.
/// When there is no registration to rebuild the email from, one of these says what to email later:
/// <paramref name="EmailSnapshotJson"/> (meeting cancel snapshot, or a mail-plan delivery reference)
/// or <paramref name="EmailFile"/> (the whole email saved as an .eml file, see <see cref="EmailFileStore"/>).
/// </summary>
public sealed record OutgoingMessage(string Kind, IReadOnlyList<MobilePart> Sms, IReadOnlyList<MobilePart> Viber,
    Func<Task<DeliveryResult>>? Email = null, string? EmailSnapshotJson = null, string? EmailFile = null)
{
    public OutgoingMessage(string kind, MessageTemplates.Mobile mobile, Func<Task<DeliveryResult>>? email = null,
        string? emailSnapshotJson = null, string? emailFile = null)
        : this(kind, mobile.Sms, mobile.Viber, email, emailSnapshotJson, emailFile) { }
}

/// <summary>
/// One mobile send. <paramref name="Subject"/> only labels the outbox / test file. Images are Viber
/// only: a path relative to the API (made absolute with the provider's public base) plus the PNG
/// kept for the outbox. <paramref name="TextWithoutImages"/> replaces the text when the images
/// cannot be sent (no public HTTPS base, or Viber off).
/// </summary>
public sealed record MobilePart(string Subject, string Text, IReadOnlyList<MobileImage>? Images = null,
    string? TextWithoutImages = null);

public sealed record MobileImage(string Path, byte[]? Png = null);
