using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// Automatic post-event feedback: frozen confirmed audience, one-time link, anonymous answers,
// one answer per person (a second submit updates it), 409 before the event ends, throttling.
[Collection("api")]
public class EventFeedbackAutomationTests(ApiFixture api)
{
    private async Task<int> CreateEventAsync(bool ended, string? title = null)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.Users.Where(u => u.Email == ApiFixture.AdminEmail).Select(u => u.Id).SingleAsync();
        var start = ended ? DateTime.Now.AddDays(-2) : DateTime.Now.AddDays(2);
        var ev = new Event
        {
            Title = title ?? $"Feedback {Guid.NewGuid():N}"[..20],
            Venue = "Course",
            StartsAt = start,
            EndsAt = start.AddHours(4),
            Status = EventStatus.Published,
            PublicSlug = $"feedback-{Guid.NewGuid():N}"[..20],
            CreatedByUserId = adminId,
            IsPublicLinkEnabled = true,
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    }

    private async Task<Registration> AddRegistrationAsync(int eventId, int? userId, DateTime? confirmedAt,
        string? guestName = null, string? guestEmail = null, string? guestPhone = null, string? guestChannel = null)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reg = new Registration
        {
            EventId = eventId,
            UserId = userId,
            GuestFullName = userId is null ? guestName ?? "Guest" : null,
            GuestEmail = guestEmail,
            GuestPhone = guestPhone,
            GuestNotificationChannel = guestChannel,
            RegisteredAt = DateTime.Now,
            Token = Guid.NewGuid(),
            ConfirmedAt = confirmedAt,
        };
        db.Registrations.Add(reg);
        await db.SaveChangesAsync();
        return reg;
    }

    private async Task EnableAsync(int eventId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EventFeedbackCampaigns.Add(new EventFeedbackCampaign { EventId = eventId, Enabled = true });
        await db.SaveChangesAsync();
    }

    private async Task RunPrepareAsync()
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EventFeedbackService>().PrepareDueAsync();
    }

    private async Task RunSendAsync(int eventId)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EventFeedbackService>().SendPendingAsync(eventId);
    }

    private async Task<List<EventFeedbackRecipient>> RecipientsAsync(int eventId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.EventFeedbackRecipients.AsNoTracking().Where(r => r.EventId == eventId).ToListAsync();
    }

    [Fact]
    public async Task The_event_form_switch_is_saved_and_returned_and_sending_needs_a_finished_event()
    {
        var admin = await api.AdminAsync();
        var title = $"Switch {Guid.NewGuid():N}"[..20];
        var start = DateTime.Today.AddDays(10).AddHours(8);
        var created = await admin.PostAsJsonAsync("/api/events", new
        {
            title,
            description = "",
            startsAt = start,
            endsAt = start.AddHours(8),
            venue = "Course",
            isPublicLinkEnabled = true,
            status = "Published",
            feedbackForm = true,
        });
        created.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetInt32();

        var got = await admin.GetFromJsonAsync<JsonElement>($"/api/events/{id}");
        Assert.True(got.GetProperty("feedbackForm").GetBoolean());

        // A future event: feedback links wait until it ends.
        var early = await admin.PostAsync($"/api/events/{id}/feedback/automation/send", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        var put = await admin.PutAsJsonAsync($"/api/events/{id}", new
        {
            title,
            description = "",
            startsAt = start,
            endsAt = start.AddHours(8),
            venue = "Course",
            isPublicLinkEnabled = true,
            status = "Published",
            feedbackForm = false,
        });
        put.EnsureSuccessStatusCode();
        got = await admin.GetFromJsonAsync<JsonElement>($"/api/events/{id}");
        Assert.False(got.GetProperty("feedbackForm").GetBoolean());
    }

    [Fact]
    public async Task The_send_button_freezes_the_audience_and_links_the_unanswered()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var ev = await CreateEventAsync(ended: true);
        await AddRegistrationAsync(ev, user.Id, DateTime.Now.AddDays(-1));

        // No campaign row and the switch off: the button still prepares and sends.
        var res = await admin.PostAsync($"/api/events/{ev}/feedback/automation/send", null);
        res.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("sent").GetInt32());

        var recipient = Assert.Single(await RecipientsAsync(ev));
        Assert.Equal(FeedbackRecipientStatus.Sent, recipient.Status);
        Assert.Single(ApiFixture.Sent("email", user.Email));
    }

    [Fact]
    public async Task Only_confirmed_active_registrants_are_frozen_and_a_phone_only_guest_gets_it_by_mobile()
    {
        var admin = await api.AdminAsync();
        var active = await api.CreateUserAsync(admin, "Participant");
        var pending = await api.CreateUserAsync(admin, "Participant");
        var deactivated = await api.CreateUserAsync(admin, "Participant");
        (await admin.PostAsync($"/api/users/{deactivated.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var ev = await CreateEventAsync(ended: true);

        await AddRegistrationAsync(ev, active.Id, DateTime.Now.AddDays(-1));
        await AddRegistrationAsync(ev, pending.Id, null);
        await AddRegistrationAsync(ev, deactivated.Id, DateTime.Now.AddDays(-1));
        var phone = ApiFixture.NewPhone();
        var guest = await AddRegistrationAsync(ev, null, DateTime.Now.AddDays(-1),
            "Guest Phone", null, phone, NotificationChannels.Viber);
        await EnableAsync(ev);

        await RunPrepareAsync();
        var recipients = await RecipientsAsync(ev);

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, r => r.UserId == active.Id && r.Channel == NotificationChannels.Email);
        Assert.DoesNotContain(recipients, r => r.UserId == pending.Id);
        Assert.DoesNotContain(recipients, r => r.UserId == deactivated.Id);
        var guestRecipient = Assert.Single(recipients, r => r.RegistrationId == guest.Id);
        Assert.Equal(NotificationChannels.Viber, guestRecipient.Channel);

        await RunSendAsync(ev);
        Assert.All(await RecipientsAsync(ev), r => Assert.Equal(FeedbackRecipientStatus.Sent, r.Status));
        Assert.Single(ApiFixture.Sent("viber", phone));
    }

    [Fact]
    public async Task One_person_gets_one_link_even_with_several_registrations()
    {
        var ev = await CreateEventAsync(ended: true);
        var email = ApiFixture.NewEmail();
        await AddRegistrationAsync(ev, null, DateTime.Now.AddDays(-1), "Guest Twice", email);
        await AddRegistrationAsync(ev, null, DateTime.Now.AddDays(-1), "Guest Twice", email.ToUpperInvariant());
        await EnableAsync(ev);

        await RunPrepareAsync();

        Assert.Single(await RecipientsAsync(ev));
    }

    [Fact]
    public async Task A_claimed_link_is_not_sent_again_and_retry_needs_the_switch_on()
    {
        var ev = await CreateEventAsync(ended: true);
        await AddRegistrationAsync(ev, null, DateTime.Now.AddDays(-1), "Guest Claim", ApiFixture.NewEmail());
        await EnableAsync(ev);
        await RunPrepareAsync();
        using (var scope = api.Services.CreateScope())
        {
            // Another sender (scheduler or Retry) already claimed it.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.EventFeedbackRecipients.Where(r => r.EventId == ev)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, FeedbackRecipientStatus.Sending)
                    .SetProperty(r => r.SentAt, DateTime.UtcNow));
        }

        await RunSendAsync(ev);

        Assert.Equal(FeedbackRecipientStatus.Sending, Assert.Single(await RecipientsAsync(ev)).Status);

        var admin = await api.AdminAsync();
        (await admin.PutAsJsonAsync($"/api/events/{ev}/feedback/automation", new { enabled = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/events/{ev}/feedback/automation/retry", null)).StatusCode);
    }

    [Fact]
    public async Task My_Events_feedback_waits_for_the_end_and_uses_the_account_name()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        var upcoming = await CreateEventAsync(ended: false);
        await AddRegistrationAsync(upcoming, user.Id, DateTime.Now);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/events/{upcoming}/feedback",
            new { rating = 5, isAnonymous = false })).StatusCode);

        var ended = await CreateEventAsync(ended: true);
        await AddRegistrationAsync(ended, user.Id, DateTime.Now.AddDays(-1));
        (await client.PostAsJsonAsync($"/api/events/{ended}/feedback",
            new { rating = 5, isAnonymous = false, displayName = "Someone Else" })).EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fb = await db.Feedbacks.SingleAsync(f => f.EventId == ended && f.UserId == user.Id);
        Assert.NotEqual("Someone Else", fb.DisplayName);

        // The admin Feedback tab reads this summary: the account's answer appears in it.
        var summary = await admin.GetFromJsonAsync<SummaryBody>($"/api/events/{ended}/feedback");
        var item = Assert.Single(summary!.Items);
        Assert.Equal(5, item.Rating);
        Assert.False(item.IsAnonymous);
    }

    [Fact]
    public async Task Nothing_is_prepared_before_the_event_ends()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var ev = await CreateEventAsync(ended: false);
        await AddRegistrationAsync(ev, user.Id, DateTime.Now);
        await EnableAsync(ev);

        await RunPrepareAsync();

        Assert.Empty(await RecipientsAsync(ev));
    }

    [Fact]
    public async Task An_anonymous_answer_is_hidden_and_a_second_submit_updates_it()
    {
        var ev = await CreateEventAsync(ended: true);
        var email = ApiFixture.NewEmail();
        var reg = await AddRegistrationAsync(ev, null, DateTime.Now.AddDays(-1), "Guest One", email);
        var token = Guid.NewGuid();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EventFeedbackRecipients.Add(new EventFeedbackRecipient
            {
                EventId = ev,
                RegistrationId = reg.Id,
                Name = "Guest One",
                Email = email,
                Token = token,
            });
            await db.SaveChangesAsync();
        }
        var anon = api.Anonymous();

        var first = await anon.PostAsJsonAsync($"/api/feedback/{token}", new
        {
            rating = 3,
            comment = "Okay",
            isAnonymous = true,
            organizationRating = 3,
            wentWell = "Venue",
            improve = "Parking",
        });
        var second = await anon.PostAsJsonAsync($"/api/feedback/{token}", new
        {
            rating = 5,
            comment = "Better",
            isAnonymous = true,
        });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var opened = await anon.GetFromJsonAsync<PublicFeedbackBody>($"/api/feedback/{token}");
        Assert.True(opened!.AlreadySubmitted);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var answers = await db.Feedbacks.Where(f => f.EventId == ev).ToListAsync();
            var answer = Assert.Single(answers);
            Assert.Equal(5, answer.Rating);
            Assert.True(answer.IsAnonymous);
            Assert.Null(answer.DisplayName);
        }

        var admin = await api.AdminAsync();
        var summary = await admin.GetFromJsonAsync<SummaryBody>($"/api/events/{ev}/feedback");
        var item = Assert.Single(summary!.Items);
        Assert.Equal("Anonymous", item.FullName);
        Assert.True(item.IsAnonymous);
    }

    [Fact]
    public async Task The_link_works_only_for_its_event_and_refuses_answers_before_the_end()
    {
        var before = await CreateEventAsync(ended: false, title: "Not over yet");
        var reg = await AddRegistrationAsync(before, null, null, "Guest Early", ApiFixture.NewEmail());
        var token = Guid.NewGuid();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EventFeedbackRecipients.Add(new EventFeedbackRecipient
            {
                EventId = before,
                RegistrationId = reg.Id,
                Name = "Guest Early",
                Email = "early@example.com",
                Token = token,
            });
            await db.SaveChangesAsync();
        }

        var opened = await api.Anonymous().GetFromJsonAsync<PublicFeedbackBody>($"/api/feedback/{token}");
        var res = await api.Anonymous().PostAsJsonAsync($"/api/feedback/{token}",
            new { rating = 4, comment = (string?)null, isAnonymous = false });

        Assert.Equal("Not over yet", opened!.EventTitle);
        Assert.False(opened.AlreadySubmitted);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var other = await CreateEventAsync(ended: true, title: "Other event");
        Assert.NotEqual(before, other);
    }

    [Fact]
    public async Task The_feedback_link_is_rate_limited_per_token()
    {
        var ev = await CreateEventAsync(ended: true);
        var reg = await AddRegistrationAsync(ev, null, DateTime.Now.AddDays(-1), "Guest Rate", ApiFixture.NewEmail());
        var token = Guid.NewGuid();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EventFeedbackRecipients.Add(new EventFeedbackRecipient
            {
                EventId = ev,
                RegistrationId = reg.Id,
                Name = "Guest Rate",
                Email = "rate@example.com",
                Token = token,
            });
            await db.SaveChangesAsync();
        }

        HttpStatusCode last = HttpStatusCode.OK;
        var anon = api.Anonymous();
        for (var i = 0; i < 30 && last != HttpStatusCode.TooManyRequests; i++)
            last = (await anon.PostAsJsonAsync($"/api/feedback/{token}",
                new { rating = 4, comment = (string?)null, isAnonymous = false })).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Rating_in_My_Events_then_through_the_link_updates_the_same_answer()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        var ev = await CreateEventAsync(ended: true);
        var reg = await AddRegistrationAsync(ev, user.Id, DateTime.Now.AddDays(-1));
        await api.Anonymous().PostAsJsonAsync($"/api/events/{ev}/feedback",
            new { rating = 2, comment = "From My Events", isAnonymous = false });
        var token = Guid.NewGuid();
        int recipientId;
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recipient = new EventFeedbackRecipient
            {
                EventId = ev,
                RegistrationId = reg.Id,
                UserId = user.Id,
                Name = user.Email,
                Email = user.Email,
                Token = token,
            };
            db.EventFeedbackRecipients.Add(recipient);
            await db.SaveChangesAsync();
            recipientId = recipient.Id;
        }

        var link = await api.Anonymous().PostAsJsonAsync($"/api/feedback/{token}",
            new { rating = 5, comment = "From the link", isAnonymous = true });

        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var answer = Assert.Single(await db.Feedbacks.Where(f => f.EventId == ev).ToListAsync());
            Assert.Equal(5, answer.Rating);
            Assert.Equal(recipientId, answer.FeedbackRecipientId);
        }
    }

    [Fact]
    public async Task Answering_through_the_link_then_in_My_Events_updates_the_same_answer()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        var ev = await CreateEventAsync(ended: true);
        var reg = await AddRegistrationAsync(ev, user.Id, DateTime.Now.AddDays(-1));
        var token = Guid.NewGuid();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EventFeedbackRecipients.Add(new EventFeedbackRecipient
            {
                EventId = ev,
                RegistrationId = reg.Id,
                UserId = user.Id,
                Name = user.Email,
                Email = user.Email,
                Token = token,
            });
            await db.SaveChangesAsync();
        }
        (await api.Anonymous().PostAsJsonAsync($"/api/feedback/{token}",
            new { rating = 3, comment = "From the link", isAnonymous = false })).EnsureSuccessStatusCode();

        var mine = await client.PostAsJsonAsync($"/api/events/{ev}/feedback",
            new { rating = 4, comment = "From My Events", isAnonymous = false });

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var answer = Assert.Single(await db.Feedbacks.Where(f => f.EventId == ev).ToListAsync());
            Assert.Equal(4, answer.Rating);
        }
    }

    private sealed record PublicFeedbackBody(string EventTitle, bool AlreadySubmitted);
    private sealed record SummaryItem(string FullName, bool IsAnonymous, int Rating);
    private sealed record SummaryBody(List<SummaryItem> Items);
}
