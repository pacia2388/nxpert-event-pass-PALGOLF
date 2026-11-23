using EventRegistration.Api.Services;

namespace EventRegistration.Api.Tests;

// Viber/SMS text of a mail plan: a link keeps its address, since mobile messages have no hidden links.
public class PlainTextLinkTests
{
    [Theory]
    [InlineData("<p>See the <a href=\"https://example.com/tees\">list</a>.</p>", "See the list (https://example.com/tees).")]
    [InlineData("<p><a href=\"https://example.com/tees\">https://example.com/tees</a></p>", "https://example.com/tees")]
    [InlineData("<p><a href=\"https://example.com/tees\"></a></p>", "https://example.com/tees")]
    [InlineData("<p>Write to <a href=\"mailto:golf@example.com\">us</a></p>", "Write to us (golf@example.com)")]
    [InlineData("<p>Click <a href=\"javascript:alert(1)\">here</a></p>", "Click here")]
    [InlineData("<p>No <b>links</b> here</p>", "No links here")]
    public void Links_keep_their_address(string xhtml, string expected) =>
        Assert.Equal(expected, InvitationMessageBuilder.PlainText(xhtml));
}
