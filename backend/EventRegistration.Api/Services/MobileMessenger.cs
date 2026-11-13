using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// A transport for phone-based notifications (entry pass, kit-claim, edit link, broadcasts).
/// Implemented by <see cref="ViberService"/> and <see cref="SmsService"/> so the two are
/// interchangeable behind <see cref="MobileMessenger"/>.
/// </summary>
public interface IMobileSender
{
    // Send the email when this channel fails (config Viber:/Sms:FallbackToEmail).
    bool FallbackToEmail { get; }
    Task<DeliveryResult> SendPassAsync(string phone, string title, DateTime startsAt, DateTime endsAt, string venue,
        string attendeeName, string passToken, byte[] qrPng, string? editUrl = null);
    Task<DeliveryResult> SendTextAsync(string phone, string subject, string text);
    Task<DeliveryResult> SendMealStubsAsync(string phone, string eventTitle,
        IReadOnlyList<(string Caption, Guid Control)> stubs);
}

/// <summary>
/// Routes phone notifications to Viber or SMS based on the admin-configured provider
/// (<see cref="SettingKeys.MobileProvider"/>, default Viber). The choice is read per-send so an
/// admin can switch providers in Settings without a restart.
/// </summary>
public class MobileMessenger
{
    private readonly ViberService _viber;
    private readonly SmsService _sms;
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;

    public MobileMessenger(ViberService viber, SmsService sms, SettingsService settings, AppDbContext db)
    {
        _viber = viber;
        _sms = sms;
        _settings = settings;
        _db = db;
    }

    private async Task<bool> SmsActiveAsync() => string.Equals(
        await _settings.GetAsync(SettingKeys.MobileProvider), MobileProviders.Sms, StringComparison.OrdinalIgnoreCase);

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
        string attendeeName, string passToken, byte[] qrPng, string? editUrl = null) =>
        await (await ResolveAsync()).SendPassAsync(phone, title, startsAt, endsAt, venue, attendeeName, passToken, qrPng, editUrl);

    public async Task<DeliveryResult> SendTextAsync(string phone, string subject, string text) =>
        await (await ResolveAsync()).SendTextAsync(phone, subject, text);

    public async Task<DeliveryResult> SendMealStubsAsync(string phone, string eventTitle,
        IReadOnlyList<(string Caption, Guid Control)> stubs) =>
        await (await ResolveAsync()).SendMealStubsAsync(phone, eventTitle, stubs);
}

/// <summary>The active mobile channel: display name, sender and the country codes it may reach.</summary>
public sealed record MobileRoute(string Name, IMobileSender Sender, IReadOnlyList<string> CountryCodes)
{
    // False when the number's country code is not listed for this channel: the channel is
    // skipped (not failed), so email is used whatever FallbackToEmail says.
    public bool Supports(string? phone) => PhoneNumber.Supported(phone, CountryCodes);
}
