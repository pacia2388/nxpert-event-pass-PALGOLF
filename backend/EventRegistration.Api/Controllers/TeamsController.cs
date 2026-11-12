using System.Data;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

// Teams: admins create teams, assign the Captain and manage members. A Captain can
// read their own team and add Participants to it, creating the account when the email is new.
[ApiController]
[Route("api/teams")]
[Authorize]
public class TeamsController : ControllerBase
{
    private const string AdminRoles = Roles.Admin + "," + Roles.SuperAdmin;

    // Temporary password for accounts a Captain creates. Shared and below the normal password
    // policy by product decision; members are expected to change it under Edit account.
    private const string DefaultMemberPassword = "char";

    // How long a "complete your registration" link stays valid.
    public const int RegistrationLinkDays = 7;

    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly IConfiguration _config;

    public TeamsController(AppDbContext db, EmailService email, IConfiguration config)
    {
        _db = db;
        _email = email;
        _config = config;
    }

    private static TeamMemberDto ToMemberDto(TeamMember m) =>
        new(m.UserId, m.User!.FullName, m.User.Email, m.AddedAt, m.NotificationFlag, m.RegistrationToken is null);

    private static TeamDetailDto ToDetailDto(Team t) => new(
        t.Id, t.Description, t.Category, t.TeamLeadUserId, t.TeamLead?.FullName ?? "",
        t.Members.Count, TeamCategories.MaxMembers(t.Category), t.CreatedAt,
        t.Members.OrderBy(m => m.User!.FullName).Select(ToMemberDto).ToList());

    private IQueryable<Team> TeamsWithMembers() =>
        _db.Teams.Include(t => t.TeamLead).Include(t => t.Members).ThenInclude(m => m.User);

    // ---- Admin: team management ----

    [HttpGet]
    [Authorize(Roles = AdminRoles)]
    public async Task<ActionResult<IEnumerable<TeamDto>>> List()
    {
        var rows = await _db.Teams
            .OrderBy(t => t.Description)
            .Select(t => new
            {
                t.Id, t.Description, t.Category, t.TeamLeadUserId,
                LeadName = t.TeamLead!.FullName, Count = t.Members.Count, t.CreatedAt,
            })
            .ToListAsync();
        return Ok(rows.Select(r => new TeamDto(
            r.Id, r.Description, r.Category, r.TeamLeadUserId, r.LeadName,
            r.Count, TeamCategories.MaxMembers(r.Category), r.CreatedAt)));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<ActionResult<TeamDetailDto>> Get(int id)
    {
        var team = await TeamsWithMembers().FirstOrDefaultAsync(t => t.Id == id);
        return team is null ? NotFound() : Ok(ToDetailDto(team));
    }

    // Shared create/update checks. Returns an error result, or null when the request is valid.
    private async Task<ActionResult?> ValidateUpsert(TeamUpsertRequest req, int? teamId)
    {
        var description = (req.Description ?? "").Trim();
        if (description.Length < 2 || description.Length > 200 || UserAccountHelper.HasControlChars(description))
            return BadRequest(new { message = "Team name must be 2 to 200 characters." });
        if (!TeamCategories.IsValid(req.Category))
            return BadRequest(new { message = "Category must be Senior or Regular." });

        var lead = await _db.Users.FindAsync(req.TeamLeadUserId);
        if (lead is null || lead.Role != Roles.Captain)
            return BadRequest(new { message = "The selected user must have the Captain role." });
        if (await _db.Teams.AnyAsync(t => t.TeamLeadUserId == lead.Id && (teamId == null || t.Id != teamId)))
            return Conflict(new { message = $"{lead.FullName} already leads another team." });
        return null;
    }

    [HttpPost]
    [Authorize(Roles = AdminRoles)]
    public async Task<ActionResult<TeamDetailDto>> Create(TeamUpsertRequest req)
    {
        var error = await ValidateUpsert(req, null);
        if (error is not null) return error;

        var team = new Team
        {
            Description = req.Description.Trim(),
            Category = req.Category,
            TeamLeadUserId = req.TeamLeadUserId,
            CompanyId = 1,
            CreatedByUserId = this.UserId(),
        };
        _db.Teams.Add(team);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Could not save the team. The captain may already lead another team." });
        }
        return Ok(ToDetailDto(await TeamsWithMembers().FirstAsync(t => t.Id == team.Id)));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<ActionResult<TeamDetailDto>> Update(int id, TeamUpsertRequest req)
    {
        var team = await TeamsWithMembers().FirstOrDefaultAsync(t => t.Id == id);
        if (team is null) return NotFound();

        var error = await ValidateUpsert(req, id);
        if (error is not null) return error;

        // A category change must not leave the team over its new cap.
        var max = TeamCategories.MaxMembers(req.Category);
        if (team.Members.Count > max)
            return Conflict(new
            {
                message = $"This team has {team.Members.Count} members. A {req.Category} team holds at most {max}; remove members first.",
            });

        team.Description = req.Description.Trim();
        team.Category = req.Category;
        team.TeamLeadUserId = req.TeamLeadUserId;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Could not save the team. The captain may already lead another team." });
        }
        return Ok(ToDetailDto(await TeamsWithMembers().FirstAsync(t => t.Id == id)));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> Delete(int id)
    {
        var team = await _db.Teams.FindAsync(id);
        if (team is null) return NotFound();
        _db.Teams.Remove(team); // members cascade; the user accounts are kept
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/members")]
    [Authorize(Roles = AdminRoles)]
    public async Task<ActionResult<TeamMemberAddResult>> AddMember(int id, TeamAddMemberRequest req)
    {
        var user = await _db.Users.FindAsync(req.UserId);
        if (user is null) return NotFound(new { message = "User not found." });
        if (user.Role != Roles.Participant)
            return BadRequest(new { message = "Only Participants can be team members." });
        return await AddMemberAsync(id, user, isNew: false);
    }

    [HttpDelete("{id:int}/members/{userId:int}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        var member = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == id && m.UserId == userId);
        if (member is null) return NotFound();
        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Captain: own team ----

    // The caller's team: the one a Captain leads, or the one a Participant is a member of
    // (read-only for them; every write endpoint below stays Captain-only). 204 when there is none.
    [HttpGet("mine")]
    [Authorize(Roles = Roles.Captain + "," + Roles.Participant)]
    public async Task<ActionResult<TeamDetailDto>> Mine()
    {
        var userId = this.UserId();
        var team = User.IsInRole(Roles.Captain)
            ? await TeamsWithMembers().FirstOrDefaultAsync(t => t.TeamLeadUserId == userId)
            : await TeamsWithMembers().FirstOrDefaultAsync(t => t.Members.Any(m => m.UserId == userId));
        return team is null ? NoContent() : Ok(ToDetailDto(team));
    }

    // Add a Participant to the caller's team by name + email. An email that is not in the system
    // creates the account (role Participant, default temporary password); an existing Participant
    // just joins. The role is never taken from input.
    [HttpPost("mine/members")]
    [Authorize(Roles = Roles.Captain)]
    public async Task<ActionResult<TeamMemberAddResult>> AddMine(TeamMemberCreateRequest req)
    {
        var leadId = this.UserId();
        var teamId = await _db.Teams.Where(t => t.TeamLeadUserId == leadId)
            .Select(t => (int?)t.Id).FirstOrDefaultAsync();
        if (teamId is null)
            return Conflict(new { message = "You have not been assigned a team yet." });

        var first = (req.FirstName ?? "").Trim();
        var last = (req.LastName ?? "").Trim();
        var middle = (req.MiddleInitial ?? "").Trim().TrimEnd('.');
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (first.Length == 0 || last.Length == 0)
            return BadRequest(new { message = "First and last name are required." });
        if (UserAccountHelper.HasControlChars(first) || UserAccountHelper.HasControlChars(last)
            || UserAccountHelper.HasControlChars(middle))
            return BadRequest(new { message = "Names contain invalid characters." });
        if (email.Length == 0 || email.Length > 256 || !UserAccountHelper.IsValidEmail(email))
            return BadRequest(new { message = "A valid email is required." });

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            if (existing.Role != Roles.Participant)
                return Conflict(new { message = "That account cannot be added to a team." });
            return await AddMemberAsync(teamId.Value, existing, isNew: false);
        }

        var user = new User
        {
            FirstName = first,
            MiddleInitial = middle.Length == 0 ? null : middle,
            LastName = last,
            FullName = UserAccountHelper.ComposeFullName(first, middle, last),
            Email = email,
            Username = await UsernameHelper.UniqueAsync(_db, UsernameHelper.Base(first, last, null)),
            PasswordHash = PasswordHasher.Hash(DefaultMemberPassword),
            Role = Roles.Participant,
            NotificationChannel = NotificationChannels.Email,
            CompanyId = 1,
            EmailVerified = true, // created by a Captain on the member's behalf
        };
        return await AddMemberAsync(teamId.Value, user, isNew: true);
    }

    // Remove a member from the caller's own team. Only the membership goes (with any pending
    // registration link); the account, passes and registrations are kept.
    [HttpDelete("mine/members/{userId:int}")]
    [Authorize(Roles = Roles.Captain)]
    public async Task<IActionResult> RemoveMine(int userId)
    {
        var leadId = this.UserId();
        var member = await _db.TeamMembers
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Team!.TeamLeadUserId == leadId);
        if (member is null) return NotFound(new { message = "That person is not a member of your team." });
        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Email the selected members of the caller's own team a single-use link to complete their
    // account (the Captain created it with a temporary password). A new link replaces the old one.
    [HttpPost("mine/registration-notice")]
    [Authorize(Roles = Roles.Captain)]
    public async Task<ActionResult<TeamRegistrationNoticeResult>> SendRegistrationNotice(TeamRegistrationNoticeRequest req)
    {
        var leadId = this.UserId();
        var team = await TeamsWithMembers().FirstOrDefaultAsync(t => t.TeamLeadUserId == leadId);
        if (team is null)
            return Conflict(new { message = "You have not been assigned a team yet." });

        var ids = (req.UserIds ?? new()).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest(new { message = "Select at least one member." });
        var selected = team.Members.Where(m => ids.Contains(m.UserId)).ToList();
        if (selected.Count != ids.Count) // never email users outside the caller's team
            return BadRequest(new { message = "Some selected people are not members of your team." });
        // Members who already have a complete account (existing Participants, or who finished the
        // link) are exempt.
        var registered = selected.Where(m => m.RegistrationToken is null).Select(m => m.User!.FullName).ToList();
        if (registered.Count > 0)
            return BadRequest(new { message = $"Already registered: {string.Join(", ", registered)}." });

        var frontendBase = _config["App:FrontendBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";
        int sent = 0, failed = 0;
        foreach (var m in selected)
        {
            var token = Guid.NewGuid();
            var url = $"{frontendBase}/register?team={token}";
            try
            {
                await _email.SendAsync(m.User!.Email, "Complete your registration",
                    EmailTemplates.TeamRegistrationNotice(m.User.FullName, team.Description,
                        team.TeamLead?.FullName ?? "Your captain", url, RegistrationLinkDays));
                m.RegistrationToken = token;
                m.RegistrationTokenExpiresAt = DateTime.UtcNow.AddDays(RegistrationLinkDays);
                m.NotificationFlag = true;
                sent++;
            }
            catch (Exception)
            {
                failed++; // keep the previous flag and link
            }
            await Task.Delay(200); // gentle throttle to stay under Gmail burst limits
        }
        await _db.SaveChangesAsync();
        return Ok(new TeamRegistrationNoticeResult(sent, failed,
            team.Members.OrderBy(m => m.User!.FullName).Select(ToMemberDto).ToList()));
    }

    // The one place a member row is written. The member cap and the one-team-per-user rule are
    // checked inside a serializable transaction so two concurrent adds cannot overfill a team, and
    // a new account is only saved together with its membership (a full team leaves no orphan user).
    private async Task<ActionResult<TeamMemberAddResult>> AddMemberAsync(int teamId, User user, bool isNew)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
        if (team is null) return NotFound();

        var max = TeamCategories.MaxMembers(team.Category);
        if (await _db.TeamMembers.CountAsync(m => m.TeamId == teamId) >= max)
            return Conflict(new { message = $"This {team.Category} team is full ({max} members)." });

        if (!isNew)
        {
            var current = await _db.TeamMembers.Where(m => m.UserId == user.Id)
                .Select(m => (int?)m.TeamId).FirstOrDefaultAsync();
            if (current == teamId)
                return Conflict(new { message = $"{user.FullName} is already in this team." });
            if (current is not null)
                return Conflict(new { message = $"{user.FullName} is already in another team." });
        }

        // A new account is "unregistered" until the member completes it via the emailed link.
        var member = new TeamMember { TeamId = teamId, User = user, RegistrationToken = isNew ? Guid.NewGuid() : null };
        _db.TeamMembers.Add(member);
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Could not add this member. Please try again." });
        }
        return Ok(new TeamMemberAddResult(ToMemberDto(member), isNew));
    }
}
