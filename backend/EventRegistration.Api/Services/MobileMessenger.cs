using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// A transport for phone-based notifications (entry pass, kit-claim, edit link, broadcasts).
/// Implemented by <see cref="SinchViberService"/> and <see cref="SmsService"/> so the two are
/// interchangeable behind <see cref="MobileMessenger"/>.
/// </summary>
public interface IMobileSender
{
    // Send the email when this channel fails (config Viber:/Sms:FallbackToEmail).
    bool FallbackToEmail { get; }
    // Sends this channel's parts of the message (OutgoingMessage.Sms or .Viber), in order, as one group.
    Task<DeliveryResult> SendAsync(string phone, OutgoingMessage message, MobileTag? tag = null);
}

/// <summary>
/// Routes phone notifications to Viber or SMS: the admin-configured provider
/// (<see cref="SettingKeys.MobileProvider"/>, default Viber) when both are set up, otherwise the one
/// that is. Read per-send so an admin can switch providers in Settings without a restart.
/// </summary>
public class MobileMessenger
{
    private readonly SinchViberService _viber;
    private readonly SmsService _sms;
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public MobileMessenger(SinchViberService viber, SmsService sms, SettingsService settings, AppDbContext db)
    {
        _viber = viber;
        _sms = sms;
        _settings = settings;
        _db = db;
    }

    public bool ViberReady => _viber.Configured;
    public bool SmsReady => _sms.Configured;

    /// <summary>
    /// The channel in use: the admin's choice when both services are set up, else the one that is
    /// set up; with neither set up, the stored choice (messages then go by email).
    /// </summary>
    public async Task<string> ActiveProviderAsync()
    {
        if (ViberReady != SmsReady) return ViberReady ? MobileProviders.Viber : MobileProviders.Sms;
        return MobileProviders.Normalize(await _settings.GetAsync(SettingKeys.MobileProvider));
    }

    private async Task<bool> SmsActiveAsync() => await ActiveProviderAsync() == MobileProviders.Sms;

    /// <summary>
    /// The active mobile channel only (Viber or SMS; the other one is never tried), with the
    /// country codes it may send to from the Phone country table.
    /// </summary>
    public async Task<MobileRoute> RouteAsync()
    {
        var sms = await SmsActiveAsync();
        var codes = await _db.PhoneCountries.AsNoTracking()
            .Where(c => sms ? c.Sms : c.Viber).Select(c => c.CountryCode).ToListAsync();
        return new MobileRoute(sms ? "SMS" : "Viber", sms ? _sms : _viber, codes);
    }
}

/// <summary>
/// Marks a message whose later Viber failure report must still send the email (mobile preferred,
/// email fallback on, has an email). The email is rebuilt from the registration, or, with no
/// registration, rebuilt from <paramref name="EmailSnapshotJson"/> (mail plan / reminder delivery)
/// or loaded from <paramref name="EmailFile"/> (broadcast, an .eml file). SMS ignores it.
/// </summary>
public sealed record MobileTag(int? RegistrationId, string Kind, string? EmailSnapshotJson = null, string? EmailFile = null);

/// <summary>The active mobile channel: display name, sender and the country codes it may reach.</summary>
public sealed record MobileRoute(string Name, IMobileSender Sender, IReadOnlyList<string> CountryCodes)
{
    // False when the number's country code is not listed for this channel: the channel is
    // skipped (not failed), so email is used whatever FallbackToEmail says.
    public bool Supports(string? phone) => PhoneNumber.Supported(phone, CountryCodes);
}
