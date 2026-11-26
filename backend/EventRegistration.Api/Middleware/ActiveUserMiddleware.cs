using System.Security.Claims;
using EventRegistration.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Middleware;

/// <summary>The signed-in account's state, loaded once per request and reused by controllers.</summary>
public sealed record ActiveUser(int Id, DateTime? DeactivatedAt, bool HasPassword);

/// <summary>
/// One check for every signed-in request (SignalR hubs included): the account still exists, is not
/// deactivated and has a password. A token can outlive any of those, so it is refused with 401.
/// Anonymous requests run no query.
/// </summary>
public class ActiveUserMiddleware(RequestDelegate next)
{
    public const string ItemKey = "ActiveUser";

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
            var user = userId == 0 ? null : await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new ActiveUser(u.Id, u.DeactivatedAt, u.PasswordHash != ""))
                .FirstOrDefaultAsync(context.RequestAborted);
            if (user is null || user.DeactivatedAt is not null || !user.HasPassword)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = StatusCodes.Status401Unauthorized });
                return;
            }
            context.Items[ItemKey] = user;
        }
        await next(context);
    }
}
