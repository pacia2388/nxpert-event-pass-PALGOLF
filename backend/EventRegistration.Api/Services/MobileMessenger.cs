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
    Task<DeliveryResult> SendPassAsync(string phone, string title, DateTime startsAt, DateTime endsAt, string venue,
        string attendeeName, string passToken, byte[] qrPng, string? editUrl = null, MobileTag? tag = null);
    Task<DeliveryResult> SendTextAsync(string phone, string subject, string text, MobileTag? tag = null);
    Task<DeliveryResult> SendMealStubsAsync(string phone, string eventTitle,
        IReadOnlyList<(string Caption, Guid Control)> stubs);
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

    private async Task<IMobileSender> ResolveAsync() => await SmsActiveAsync() ? _sms : _viber;

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

    public async Task<DeliveryResult> SendPassAsync(string phone, string title, DateTime startsAt, DateTime endsAt, string venue,
        string attendeeName, string passToken, byte[] qrPng, string? editUrl = null, MobileTag? tag = null) =>
        await (await ResolveAsync()).SendPassAsync(phone, title, startsAt, endsAt, venue, attendeeName, passToken, qrPng, editUrl, tag);

    public async Task<DeliveryResult> SendTextAsync(string phone, string subject, string text, MobileTag? tag = null) =>
        await (await ResolveAsync()).SendTextAsync(phone, subject, text, tag);

    public async Task<DeliveryResult> SendMealStubsAsync(string phone, string eventTitle,
        IReadOnlyList<(string Caption, Guid Control)> stubs) =>
        await (await ResolveAsync()).SendMealStubsAsync(phone, eventTitle, stubs);
}

/// <summary>
/// Marks the message that decides a registrant's delivery when a later Viber failure report must
/// still send the email (mobile preferred, FallbackToEmail on, has an email). SMS ignores it.
/// </summary>
public sealed record MobileTag(int RegistrationId, string Kind);

/// <summary>The active mobile channel: display name, sender and the country codes it may reach.</summary>
public sealed record MobileRoute(string Name, IMobileSender Sender, IReadOnlyList<string> CountryCodes)
{
    // False when the number's country code is not listed for this channel: the channel is
    // skipped (not failed), so email is used whatever FallbackToEmail says.
    public bool Supports(string? phone) => PhoneNumber.Supported(phone, CountryCodes);
}
