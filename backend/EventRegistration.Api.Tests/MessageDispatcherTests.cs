using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventRegistration.Api.Tests;

// The one send → fall back flow (plans/completed/20261123_uniform-message-delivery.md), without a database: the
// provider route is passed in and the registrant has no registration (no retry row).
public class MessageDispatcherTests
{
    private const string Listed = "+639171234567";
    private const string Unlisted = "+15551234567";

    private sealed class FakeSender(DeliveryResult result, bool fallback) : IMobileSender
    {
        public int Sends;
        public MobileTag? Tag;
        public bool FallbackToEmail => fallback;
        public Task<DeliveryResult> SendAsync(string phone, OutgoingMessage message, MobileTag? tag = null)
        {
            Sends++;
            Tag = tag;
            return Task.FromResult(result);
        }
    }

    private static async Task<(PassDelivery Result, FakeSender Mobile, int Emails)> SendAsync(string channel, string phone,
        DeliveryResult mobileResult, bool providerFallback, bool? fallbackOverride = null, string? snapshot = "{}")
    {
        var sender = new FakeSender(mobileResult, providerFallback);
        var emails = 0;
        var parts = new[] { new MobilePart("Hi", "Hello") };
        Task<DeliveryResult> SendEmail()
        {
            emails++;
            return Task.FromResult(DeliveryResult.Sent);
        }
        var message = new OutgoingMessage(EmailRetryKinds.Message, parts, parts, SendEmail, snapshot);

        // The route is passed in, so the messenger and the scope factory (retry rows) are never used.
        var chain = new NotificationDelivery(NullLogger<NotificationDelivery>.Instance);
        var dispatcher = new MessageDispatcher(null!, chain, null!);
        var route = new MobileRoute("Viber", sender, ["63"]);
        var result = await dispatcher.SendAsync(new Recipient(phone, channel), message,
            route: route, fallbackToEmail: fallbackOverride);
        return (result, sender, emails);
    }

    [Theory]
    // channel, phone, mobile result, provider fallback → mobile tried, email sent
    [InlineData(NotificationChannels.Viber, Listed, DeliveryResult.Sent, true, 1, 0)]
    [InlineData(NotificationChannels.Viber, Listed, DeliveryResult.Failed, true, 1, 1)]
    [InlineData(NotificationChannels.Viber, Listed, DeliveryResult.Failed, false, 1, 0)]
    [InlineData(NotificationChannels.Viber, Listed, DeliveryResult.NotConfigured, false, 1, 1)]
    [InlineData(NotificationChannels.Viber, Unlisted, DeliveryResult.Sent, false, 0, 1)]
    [InlineData(NotificationChannels.Both, Listed, DeliveryResult.Sent, true, 1, 1)]
    [InlineData(NotificationChannels.Both, Listed, DeliveryResult.Failed, true, 1, 1)]
    [InlineData(NotificationChannels.Email, Listed, DeliveryResult.Sent, true, 0, 1)]
    public async Task Preference_then_fallback(string channel, string phone, DeliveryResult mobile, bool fallback,
        int mobileSends, int emails)
    {
        var (_, sender, sent) = await SendAsync(channel, phone, mobile, fallback);

        Assert.Equal(mobileSends, sender.Sends);
        Assert.Equal(emails, sent);
    }

    [Fact]
    public async Task Mobile_preferred_send_is_tagged_with_the_saved_email()
    {
        var (_, sender, _) = await SendAsync(NotificationChannels.Viber, Listed, DeliveryResult.Sent, true, snapshot: "{\"x\":1}");

        Assert.Equal(EmailRetryKinds.Message, sender.Tag!.Kind);
        Assert.Equal("{\"x\":1}", sender.Tag.EmailSnapshotJson);
    }

    [Theory]
    [InlineData(NotificationChannels.Both, true, "{}")]   // Both: the email already went out
    [InlineData(NotificationChannels.Viber, false, "{}")] // fallback off
    [InlineData(NotificationChannels.Viber, true, null)]  // nothing to send later
    public async Task No_tag_when_a_late_failure_must_not_send_the_email(string channel, bool fallback, string? snapshot)
    {
        var (_, sender, _) = await SendAsync(channel, Listed, DeliveryResult.Sent, fallback, snapshot: snapshot);

        Assert.Null(sender.Tag);
    }

    [Theory]
    // Broadcast: the dialog's tick box decides, also for an unlisted country.
    [InlineData(Listed, false, 0)]
    [InlineData(Unlisted, false, 0)]
    [InlineData(Unlisted, true, 1)]
    public async Task Fallback_override_also_covers_unlisted_countries(string phone, bool tick, int emails)
    {
        var (_, _, sent) = await SendAsync(NotificationChannels.Viber, phone, DeliveryResult.Failed, true, fallbackOverride: tick);

        Assert.Equal(emails, sent);
    }

    [Fact]
    public void Pass_carries_join_link_and_meal_stubs_in_one_message()
    {
        var stub = new MessageTemplates.MealStubLine("Lunch ₱150.00 · VALID ON MARCH 15, 2030", Guid.NewGuid());
        var details = new MessageTemplates.PassDetails(
            Title: "Open", StartsAt: new DateTime(2030, 3, 15, 8, 0, 0), EndsAt: new DateTime(2030, 3, 15, 16, 0, 0),
            Venue: "Course", Name: "Mia", Token: Guid.NewGuid(), QrPng: [1, 2, 3], EditUrl: "https://app/r/x",
            MeetingUrl: "https://meet/golf", ShowPass: true, EventTitle: "Open", Stubs: [stub], SmsPassUrl: null);
        var m = MessageTemplates.Pass(details);

        // Viber: one message, QR first (sent with the text as one card), then the stub image.
        var viber = Assert.Single(m.Viber);
        Assert.Equal(3, m.Sms.Count);
        Assert.All(m.Sms, p => Assert.Null(p.Images));
        Assert.EndsWith("/qr.png", viber.Images![0].Path);
        Assert.EndsWith("/stub.png", viber.Images![1].Path);
        Assert.Contains("https://meet/golf", viber.Text);                      // join link
        Assert.Contains(stub.Caption, viber.Text);                             // stub list
        Assert.Contains($"No. {stub.Number}", viber.TextWithoutImages);         // text-only Viber
        Assert.Contains("Please present your QR entry pass", m.Sms[0].Text);  // no public base: no link
    }
}
