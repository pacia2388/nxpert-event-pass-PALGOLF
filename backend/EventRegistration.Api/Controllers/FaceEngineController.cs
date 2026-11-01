using System.Security.Cryptography;
using System.Text;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

/// <summary>
/// Service-to-service endpoints for the face recognition engine (facerog-engine). The engine has no
/// user session, so it authenticates with a shared key in the X-Face-Engine-Key header
/// (FaceEngine:ApiKey). The endpoints are disabled (404) while no key is configured.
/// </summary>
[ApiController]
[Route("api/face-engine")]
[AllowAnonymous]
public class FaceEngineController : ControllerBase
{
    public const string KeyHeader = "X-Face-Engine-Key";

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public FaceEngineController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>
    /// The event's registrants who have a photo, for the engine to build its recognition gallery.
    /// PersonId is "u{userId}" for account registrations (checked in via /api/checkin/user) and
    /// "g{token}" for guest registrations (checked in via /api/checkin by token). A member registered
    /// for several sub-events appears once.
    /// </summary>
    [HttpGet("events/{id:int}/gallery")]
    public async Task<ActionResult<IEnumerable<FaceEngineGalleryItem>>> Gallery(int id)
    {
        if (RejectRequest() is { } rejected) return rejected;

        if (!await _db.Events.AnyAsync(e => e.Id == id)) return NotFound();

        var regs = await _db.Registrations
            .Where(r => r.EventId == id && (r.User != null ? r.User.PhotoUrl != null : r.GuestPhoto != null))
            .Select(r => new
            {
                r.UserId,
                r.Token,
                Name = r.User != null ? r.User.FullName : r.GuestFullName,
                Photo = r.User != null ? r.User.PhotoUrl : r.GuestPhoto,
            })
            .ToListAsync();

        var items = regs
            .Select(r => new FaceEngineGalleryItem(
                r.UserId is int uid ? $"u{uid}" : $"g{r.Token}",
                r.Name ?? "Guest",
                r.Photo!))
            .DistinctBy(i => i.PersonId)
            .ToList();
        return Ok(items);
    }

    /// <summary>
    /// Which of the given events are over, so the engine can delete its copy of their faces: those
    /// past the last day of the event and its sub-events (check-in stays open through that day,
    /// see CheckInController), and those that no longer exist.
    /// </summary>
    [HttpGet("events/finished")]
    public async Task<ActionResult<IEnumerable<int>>> FinishedEvents([FromQuery] int[] ids)
    {
        if (RejectRequest() is { } rejected) return rejected;

        var events = await _db.Events
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.EndsAt, LastSessionEnd = e.Sessions.Max(s => (DateTime?)s.EndsAt) })
            .ToListAsync();

        // Times are wall-clock, like the check-in window.
        var today = DateTime.Now.Date;
        var running = events
            .Where(e => today <= (e.LastSessionEnd > e.EndsAt ? e.LastSessionEnd.Value : e.EndsAt).Date)
            .Select(e => e.Id)
            .ToHashSet();
        return Ok(ids.Distinct().Where(id => !running.Contains(id)));
    }

    private ActionResult? RejectRequest()
    {
        var configuredKey = _config["FaceEngine:ApiKey"];
        if (string.IsNullOrEmpty(configuredKey)) return NotFound();
        return KeyMatches(Request.Headers[KeyHeader].ToString(), configuredKey) ? null : Unauthorized();
    }

    private static bool KeyMatches(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
