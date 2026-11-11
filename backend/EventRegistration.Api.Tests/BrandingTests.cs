using System.Text;
using EventRegistration.Api.Services;

namespace EventRegistration.Api.Tests;

// The name people see on what the app sends out. Internal identifiers keep their old form.
public class BrandingTests
{
    [Fact]
    public void Calendar_invite_names_PAL_GOLF_as_its_maker_and_keeps_the_uid()
    {
        var details = new CalendarInvite.Details(
            "event-interclub@nxpass", 0, "Interclub", new DateTime(2027, 2, 17, 8, 0, 0),
            new DateTime(2027, 2, 17, 17, 0, 0), "Cagayan de Oro", "https://example.com/e/interclub", null,
            "PAL GOLF", "events@example.com", "Mia Santos", "mia@example.com");

        var ics = Encoding.UTF8.GetString(CalendarInvite.Request(details));

        Assert.Contains("PRODID:-//PAL GOLF//Event Registration//EN", ics);
        Assert.Contains("UID:event-interclub@nxpass", ics);
    }

    [Fact]
    public void Emails_are_sent_under_the_name_PAL_GOLF_unless_configured_otherwise()
    {
        Assert.Equal("PAL GOLF", new EmailSettings().FromName);
    }

    [Fact]
    public void Email_footer_names_PAL_GOLF()
    {
        var html = EmailTemplates.TeamRegistrationNotice("Mia Santos", "Eagles", "Leo Cruz",
            "https://example.com/register", 7);

        Assert.Contains("PAL GOLF · this is an automated message", html);
    }

    [Fact]
    public void Emails_wear_PAL_blue_not_the_old_teal()
    {
        var html = EmailTemplates.TeamRegistrationNotice("Mia Santos", "Eagles", "Leo Cruz",
            "https://example.com/register", 7);

        Assert.Contains("#0f5fb8", html);
        Assert.DoesNotContain("#0d9488", html);
    }

    [Fact]
    public void A_configured_app_name_reaches_the_email_footer_and_the_calendar_invite()
    {
        Branding.AppName = "Demo Cup";
        try
        {
            var html = EmailTemplates.TeamRegistrationNotice("Mia Santos", "Eagles", "Leo Cruz",
                "https://example.com/register", 7);
            var details = new CalendarInvite.Details(
                "event-interclub@nxpass", 0, "Interclub", new DateTime(2027, 2, 17, 8, 0, 0),
                new DateTime(2027, 2, 17, 17, 0, 0), null, "https://example.com/e/interclub", null,
                "Demo Cup", "events@example.com", "Mia Santos", "mia@example.com");
            var ics = Encoding.UTF8.GetString(CalendarInvite.Request(details));

            Assert.Contains("Demo Cup · this is an automated message", html);
            Assert.Contains("PRODID:-//Demo Cup//Event Registration//EN", ics);
        }
        finally
        {
            Branding.AppName = Branding.DefaultAppName;
        }
    }
}
