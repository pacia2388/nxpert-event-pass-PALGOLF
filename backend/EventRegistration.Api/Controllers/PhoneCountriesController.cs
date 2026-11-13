using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

public record PhoneCountryDto(int Id, string CountryCode, bool Viber, bool Sms);
public record PhoneCountryRequest(string CountryCode);
public record PhoneCountryChannelsRequest(bool Viber, bool Sms);

// Country codes users may save phone numbers with, and which mobile channels may send to each.
// Admin-managed; the codes feed the phone control on every form.
[ApiController]
[Route("api/phone-countries")]
[Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
public class PhoneCountriesController(AppDbContext db) : ControllerBase
{
    // Public: the account page and user forms need the codes. No personal data.
    [HttpGet, AllowAnonymous]
    public async Task<List<PhoneCountryDto>> List() =>
        await db.PhoneCountries.AsNoTracking()
            .OrderBy(c => c.CountryCode)
            .Select(c => new PhoneCountryDto(c.Id, c.CountryCode, c.Viber, c.Sms))
            .ToListAsync();

    // New codes start with every channel off (numbers allowed, messages go by email).
    [HttpPost]
    public async Task<ActionResult<PhoneCountryDto>> Add(PhoneCountryRequest req)
    {
        var code = (req.CountryCode ?? "").Trim().TrimStart('+');
        if (code.Length is < 1 or > 4 || !code.All(char.IsAsciiDigit) || code[0] == '0')
            return BadRequest(new { message = "Country code must be 1 to 4 digits, e.g. 63." });
        if (await db.PhoneCountries.AnyAsync(c => c.CountryCode == code))
            return Conflict(new { message = $"+{code} is already listed." });

        var row = new PhoneCountry { CountryCode = code };
        db.PhoneCountries.Add(row);
        await db.SaveChangesAsync();
        return Ok(new PhoneCountryDto(row.Id, row.CountryCode, row.Viber, row.Sms));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PhoneCountryDto>> SetChannels(int id, PhoneCountryChannelsRequest req)
    {
        var row = await db.PhoneCountries.FindAsync(id);
        if (row is null) return NotFound();
        row.Viber = req.Viber;
        row.Sms = req.Sms;
        await db.SaveChangesAsync();
        return Ok(new PhoneCountryDto(row.Id, row.CountryCode, row.Viber, row.Sms));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id)
    {
        var row = await db.PhoneCountries.FindAsync(id);
        if (row is null) return NotFound();
        db.PhoneCountries.Remove(row);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
