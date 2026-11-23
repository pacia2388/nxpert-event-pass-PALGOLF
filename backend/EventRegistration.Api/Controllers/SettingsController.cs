using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventRegistration.Api.Controllers;

// Admin-configurable application settings.
[ApiController]
[Route("api/settings")]
[Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;
    private readonly MobileMessenger _mobile;
    private readonly CategoryCatalog _categories;

    public SettingsController(SettingsService settings, MobileMessenger mobile, CategoryCatalog categories)
    {
        _categories = categories;
        _settings = settings;
        _mobile = mobile;
    }

    private bool CanSwitch => _mobile.ViberReady && _mobile.SmsReady;

    private async Task<AppSettingsDto> DtoAsync(List<string> cc) =>
        new(cc, await _mobile.ActiveProviderAsync(), _mobile.ViberReady, _mobile.SmsReady);

    [HttpGet]
    public async Task<ActionResult<AppSettingsDto>> Get()
    {
        return Ok(await DtoAsync(await _settings.GetEmailCcAsync()));
    }

    // Which service carries mobile messages ("Viber" or "Sms"), so forms can label the mobile
    // channel. Public: the registration form and account page need it.
    [HttpGet("mobile-channel"), AllowAnonymous]
    public async Task<ActionResult<MobileChannelDto>> MobileChannel() =>
        Ok(new MobileChannelDto(await _mobile.ActiveProviderAsync()));

    // Event / team categories from categories.json (empty = none). Public: Browse Events shows the
    // category pill to everyone.
    [HttpGet("categories"), AllowAnonymous]
    public ActionResult<List<CategoryDto>> Categories() => Ok(_categories.List());

    [HttpPut]
    public async Task<ActionResult<AppSettingsDto>> Update(UpdateSettingsRequest req)
    {
        // Normalize + de-duplicate, and validate each address format.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<string>();
        foreach (var raw in req.CcEmails ?? new())
        {
            var e = (raw ?? "").Trim();
            if (e.Length == 0) continue;
            if (!IsValidEmail(e))
                return BadRequest(new { message = $"'{e}' is not a valid email address." });
            if (seen.Add(e)) cleaned.Add(e);
        }

        if (req.MobileProvider is not null && !MobileProviders.IsValid(req.MobileProvider))
            return BadRequest(new { message = "Mobile provider must be 'Viber' or 'Sms'." });
        // The channel can be chosen only when both services are set up; otherwise the stored choice
        // is left alone (the set-up service is used whatever it says).
        if (req.MobileProvider is not null && CanSwitch)
            await _settings.SetAsync(SettingKeys.MobileProvider, MobileProviders.Normalize(req.MobileProvider));

        await _settings.SetAsync(SettingKeys.EmailCc, string.Join(",", cleaned));
        return Ok(await DtoAsync(cleaned));
    }

    private static bool IsValidEmail(string email) =>
        System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
}
