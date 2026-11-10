using System.Text.RegularExpressions;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

// Airline baggage vouchers (golf equipment). Participants and Team Leads only; each user sees
// only their own vouchers. A ticket number can be used for one voucher across all users.
[ApiController]
[Route("api/vouchers")]
[Authorize(Roles = Roles.Participant + "," + Roles.TeamLead)]
public partial class VouchersController : ControllerBase
{
    private readonly AppDbContext _db;
    public VouchersController(AppDbContext db) => _db = db;

    private const string DuplicateMessage = "This airline ticket number already has a voucher.";

    [GeneratedRegex("^[A-Z0-9-]{4,30}$")]
    private static partial Regex TicketPattern();

    public static string VoucherNo(int id) => $"GV-{id:D6}";

    // Trimmed, upper-cased ticket number, or null when it breaks the format rule.
    public static string? NormalizeTicket(string? raw)
    {
        var t = (raw ?? "").Trim().ToUpperInvariant();
        return TicketPattern().IsMatch(t) && t.Any(char.IsLetterOrDigit) ? t : null;
    }

    [HttpGet]
    public async Task<ActionResult<List<VoucherDto>>> Mine()
    {
        var uid = this.UserId();
        var rows = await _db.Vouchers
            .Where(v => v.UserId == uid)
            .OrderByDescending(v => v.Id)
            .Select(v => new { v.Id, v.User!.FullName, v.TicketNumber, v.CreatedAt })
            .ToListAsync();
        // Stored as UTC; mark it so JSON carries the "Z" and the browser shows local time.
        return rows.Select(v => new VoucherDto(v.Id, VoucherNo(v.Id), v.FullName, v.TicketNumber,
            DateTime.SpecifyKind(v.CreatedAt, DateTimeKind.Utc))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<VoucherDto>> Create(CreateVoucherRequest req)
    {
        var ticket = NormalizeTicket(req.TicketNumber);
        if (ticket is null)
            return BadRequest(new { message = "Airline ticket number must be 4–30 letters, numbers or dashes." });

        if (await _db.Vouchers.AnyAsync(v => v.TicketNumber == ticket))
            return Conflict(new { message = DuplicateMessage });

        var uid = this.UserId();
        var user = await _db.Users.FindAsync(uid);
        if (user is null) return Unauthorized();

        var voucher = new Voucher { UserId = uid, TicketNumber = ticket, CreatedAt = DateTime.UtcNow };
        _db.Vouchers.Add(voucher);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index on TicketNumber: another request saved the same ticket first.
            return Conflict(new { message = DuplicateMessage });
        }

        var dto = new VoucherDto(voucher.Id, VoucherNo(voucher.Id), user.FullName, voucher.TicketNumber, voucher.CreatedAt);
        return Created($"/api/vouchers/{voucher.Id}", dto);
    }
}
