using System.Text;
using System.Threading.RateLimiting;
using EventRegistration.Api.Data;
using EventRegistration.Api.Hubs;
using EventRegistration.Api.Middleware;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using EventRegistration.Api.Services.Theming;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
// Machine-local secrets (gitignored), e.g. appsettings.Development.Local.json.
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.Local.json", optional: true);
// Event / team categories for this installation. Not published, so a deploy keeps the server's copy;
// no file = no categories. Edits apply without a restart.
builder.Configuration.AddJsonFile("categories.json", optional: true, reloadOnChange: true);
// Optional user profile fields for this installation. Not published; edits apply without a restart.
builder.Configuration.AddJsonFile("user-fields.json", optional: true, reloadOnChange: true);
// Sinch webhook lines also go to logs/viber-hook/*.log (Logging:LogLevel filters still apply).
builder.Logging.AddProvider(new ViberHookFileLoggerProvider(
    Path.Combine(builder.Environment.ContentRootPath, "logs", "viber-hook")));

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ---- Settings + services ----
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<JwtService>();
builder.Services.AddOptions<CategoryOptions>().Bind(builder.Configuration.GetSection("Categories")).ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<CategoryOptions>, CategoryOptionsValidator>();
builder.Services.AddSingleton<CategoryCatalog>();
builder.Services.AddOptions<UserFieldOptions>().Bind(builder.Configuration.GetSection("UserFields"));
builder.Services.AddSingleton<UserFieldCatalog>();
// Inline-image limits of composed emails (Mail:InlineImages), shared with the web editor.
builder.Services.AddOptions<MailImageOptions>().Bind(builder.Configuration.GetSection("Mail:InlineImages")).ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MailImageOptions>>().Value);

// The name on what the API sends out; blank keeps the built-in one.
Branding.AppName = builder.Configuration["App:Name"] ?? "";
// The look: the theme file "theme" names (same key as the web app's appSettings.json) (theme.palgolf.json or theme.nxpass.json; missing = PALGOLF).
// See documentation/theming.md.
ThemeStore.WebBaseUrl = builder.Configuration["App:FrontendBaseUrl"] ?? ThemeStore.WebBaseUrl;
ThemeStore.FilePath = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["theme"] ?? "theme.palgolf.json");

var emailSettings = builder.Configuration.GetSection("Email").Get<EmailSettings>() ?? new EmailSettings();
builder.Services.AddSingleton(emailSettings);
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddSingleton<EmailFileStore>();
// Composed invitation / mail-plan emails with their attachments live in their own folder for as
// long as they are needed (never the 26 h retry cleanup).
builder.Services.AddKeyedSingleton("outgoing", (sp, _) => EmailFileStore.Outgoing(
    sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IWebHostEnvironment>()));
builder.Services.AddScoped<EmailSender>();
builder.Services.AddScoped<InvitationEmailDispatcher>();
builder.Services.AddHostedService<InvitationEmailScheduler>();
builder.Services.AddScoped<MailPlanAudience>();
builder.Services.AddScoped<MailPlanDispatcher>();
builder.Services.AddSingleton<PastedImageFetcher>();
builder.Services.AddHostedService<MailPlanScheduler>();
var viberSettings = builder.Configuration.GetSection("Viber").Get<ViberSettings>() ?? new ViberSettings();
builder.Services.AddSingleton(viberSettings);
// Singleton: the Sinch client inside caches its token and connections.
builder.Services.AddSingleton<SinchViberService>();
var smsSettings = builder.Configuration.GetSection("Sms").Get<SmsSettings>() ?? new SmsSettings();
builder.Services.AddSingleton(smsSettings);
builder.Services.AddScoped<SmsService>();
// Routes phone notifications to Viber or SMS per the admin-configured provider.
builder.Services.AddScoped<MobileMessenger>();
builder.Services.AddScoped<NotificationDelivery>();
builder.Services.AddScoped<MessageDispatcher>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<EventFeedbackService>();
builder.Services.AddHostedService<EventFeedbackScheduler>();
builder.Services.AddScoped<EmailRetryDispatcher>();
builder.Services.AddHostedService<EmailRetryScheduler>();
builder.Services.AddScoped<PasswordSetupService>();
builder.Services.AddSingleton<PasswordLinkQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PasswordLinkQueue>());
builder.Services.AddSingleton<QrService>();
builder.Services.AddSingleton<MealStubImageService>();
builder.Services.AddScoped<MealStubService>();
builder.Services.AddScoped<PassService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AttendeeQuery>();
builder.Services.AddScoped<AttendanceSummaryService>();
builder.Services.AddScoped<CalendarInvitationNotifier>();
builder.Services.AddSignalR();

// ---- Auth ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        // Browsers can't set an Authorization header on a WebSocket, so SignalR sends the JWT
        // as an "access_token" query-string value on the hub connection — read it here.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(o =>
{
    // Allowed for admins/super-admins (always) OR any user carrying the confirm grant claim.
    o.AddPolicy(Permissions.ConfirmRegistrationsPolicy, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole(Roles.Admin) ||
            ctx.User.IsInRole(Roles.SuperAdmin) ||
            ctx.User.HasClaim(Permissions.ConfirmRegistrations, "true")));
});

// ---- CORS: allow-all for the demo (see appsettings note; tighten before real use) ----
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// A composed email may carry up to 25 MB of attachments plus its inline images. Let the multipart
// reader accept that; the built email is still checked against the 25 MB limit.
var mailUploadLimits = builder.Configuration.GetSection("Mail:InlineImages").Get<MailImageOptions>()
    ?? new MailImageOptions();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = mailUploadLimits.RequestLimitBytes;
    o.ValueLengthLimit = int.MaxValue;
    o.MemoryBufferThreshold = int.MaxValue;
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Per-IP limits on the password endpoints (guessing and email flooding). Reusable for more
// /api/auth routes by adding the policy name.
builder.Services.AddRateLimiter(o =>
{
    // Feedback links: also capped per IP across every token, so one address cannot try token
    // after token (the "feedback" policy below limits each link on its own).
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api/feedback")
            ? RateLimitPartition.GetFixedWindowLimiter(
                "feedback-ip|" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("none"));
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static RateLimitPartition<string> PerIp(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    o.AddPolicy("auth-forgot-password", PerIp);
    o.AddPolicy("auth-set-password", PerIp);
    // Per IP and token: one link cannot be hammered, and other visitors are unaffected.
    o.AddPolicy("feedback", context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + "|" + context.Request.Path,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();
ThemeStore.Log = message => app.Logger.LogWarning("{ThemeMessage}", message);

// Say where email goes, so a run's log answers it without opening the local settings.
if (emailSettings.Enabled)
    app.Logger.LogInformation("Email delivery: SMTP via {Host}:{Port}.", emailSettings.Host, emailSettings.Port);
else
    app.Logger.LogInformation("Email delivery: outbox (SMTP is off).");
var webhookSecret = viberSettings.WebhookSecret?.Trim();
app.Logger.LogInformation("Viber delivery reports: {Enabled}",
    string.IsNullOrEmpty(webhookSecret) || webhookSecret.Equals("required", StringComparison.OrdinalIgnoreCase) ? "off" : "on");

// ---- Migrate + seed on startup ----
// Applying migrations on startup is controlled by the Database:AutoMigrate flag in appsettings
// (default true), so a published/deployed build can bring the schema up to date automatically —
// or leave it to a manual/DBA process by setting the flag to false.
// A server reboot starts IIS before SQL Server listens; retry within a 90 s budget instead of
// failing to start. The budget stays under IIS's 120 s startup limit, and each failed attempt
// already spends the SQL connect timeout (15 s by default), so a deadline, not a count.
var databaseDeadline = DateTimeOffset.UtcNow.AddSeconds(90);
for (var attempt = 1; ; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (app.Configuration.GetValue("Database:AutoMigrate", true))
            await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
        break;
    }
    catch (Exception ex) when (DateTimeOffset.UtcNow < databaseDeadline)
    {
        app.Logger.LogWarning(ex, "The database is not reachable (attempt {Attempt}); retrying in 5 seconds.", attempt);
        await Task.Delay(TimeSpan.FromSeconds(5));
    }
}

app.UseCors();
app.UseAuthentication();
// Deactivated / passwordless accounts are refused on every signed-in request (401).
app.UseMiddleware<ActiveUserMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapGet("/", () => Results.Ok(new { service = "EventRegistration.Api", status = "ok" }));

app.Run();

// Lets the integration tests host the API in-process (WebApplicationFactory<Program>).
public partial class Program { }
