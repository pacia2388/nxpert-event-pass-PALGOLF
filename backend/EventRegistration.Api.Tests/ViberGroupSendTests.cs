using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Sinch.Conversation.Messages.Message;
using Sinch.Conversation.Messages.Send;

namespace EventRegistration.Api.Tests;

// The Viber send rule against a stand-in for Sinch, without a database: text + QR as one card,
// each meal stub as its own message, and a refused stub fails the whole pass so the email goes out.
public class ViberGroupSendTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "test", "viber-group");

    private static readonly ViberSettings Settings = new()
    {
        Enabled = true, ProjectId = "p", KeyId = "k", KeySecret = "s", AppId = "a", Region = "us",
        PublicApiBaseUrl = "https://events.example.com",
    };

    // The confirmation for an in-person event with two meal stubs: one Viber part, three images.
    private static OutgoingMessage Pass(Func<Task<DeliveryResult>>? email = null)
    {
        var stubs = new List<MessageTemplates.MealStubLine>
        {
            new("Lunch ₱150.00 · VALID ON MARCH 15, 2030", Guid.NewGuid()),
            new("Dinner ₱200.00 · VALID ON MARCH 15, 2030", Guid.NewGuid()),
        };
        var details = new MessageTemplates.PassDetails(
            Title: "Open", StartsAt: new DateTime(2030, 3, 15, 8, 0, 0), EndsAt: new DateTime(2030, 3, 15, 16, 0, 0),
            Venue: "Course", Name: "Mia", Token: Guid.NewGuid(), QrPng: [1, 2, 3], EditUrl: null, MeetingUrl: null,
            ShowPass: true, EventTitle: "Open", Stubs: stubs, SmsPassUrl: null);
        return new OutgoingMessage(EmailRetryKinds.Pass, MessageTemplates.Pass(details), email);
    }

    [Fact]
    public async Task Pass_with_two_meal_stubs_is_one_card_then_one_message_per_stub()
    {
        var sinch = new FakeSinch(acceptFirst: int.MaxValue);

        var result = await sinch.SendAsync("+639171234567", Pass());

        Assert.Equal(DeliveryResult.Sent, result);
        Assert.Equal(3, sinch.Sent.Count);
        var card = sinch.Sent[0].CardMessage;
        Assert.NotNull(card);
        Assert.Contains("Present the QR entry pass", card.Description);
        Assert.EndsWith("/qr.png", card.MediaMessage!.Url.ToString());
        Assert.All(sinch.Sent.Skip(1), m => Assert.EndsWith("/stub.png", m.MediaMessage!.Url.ToString()));
    }

    [Fact]
    public async Task A_refused_meal_stub_fails_the_pass_and_the_email_goes_out()
    {
        // Sinch takes the card, then refuses the first stub picture.
        var sinch = new FakeSinch(acceptFirst: 1);
        var emails = 0;
        Task<DeliveryResult> SendEmail()
        {
            emails++;
            return Task.FromResult(DeliveryResult.Sent);
        }

        var viberResult = await sinch.SendAsync("+639171234567", Pass());
        Assert.Equal(DeliveryResult.Failed, viberResult);
        Assert.Single(sinch.Sent); // the second stub is never tried

        var chain = new NotificationDelivery(NullLogger<NotificationDelivery>.Instance);
        var dispatcher = new MessageDispatcher(null!, chain, null!);
        var route = new MobileRoute("Viber", new FakeSinch(acceptFirst: 1), ["63"]);
        var delivery = await dispatcher.SendAsync(new Recipient("+639171234567", NotificationChannels.Viber), Pass(SendEmail),
            route: route);

        Assert.Equal(1, emails);
        Assert.Equal(["email"], delivery.Channels);
        Assert.Equal("Viber", delivery.FellBackFrom);
    }

    // Viber with a stand-in for Sinch: accepts the first sends and refuses the rest, recording what it accepted.
    private sealed class FakeSinch(int acceptFirst)
        : SinchViberService(Settings, NullLogger<SinchViberService>.Instance, new Env(Root))
    {
        public readonly List<AppMessage> Sent = [];
        private int _calls;

        protected override Task<string?> PostAsync(SendMessageRequest request)
        {
            _calls++;
            if (_calls > acceptFirst)
                throw new HttpRequestException("Sinch refused the message");
            Sent.Add(request.Message);
            return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
        }
    }

    private sealed class Env(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
