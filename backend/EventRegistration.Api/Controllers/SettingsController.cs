using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using EventRegistration.Api.Services.Theming;
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

    public SettingsController(SettingsService settings, MobileMessenger mobile)
    {
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

    // A filled-in theme.json for developers to edit and drop beside the API after publishing.
    [HttpGet("theme-template")]
    public IActionResult ThemeTemplate() =>
        File(System.Text.Encoding.UTF8.GetBytes(ThemeResolver.Template()), "application/json", "theme-template.json");

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
