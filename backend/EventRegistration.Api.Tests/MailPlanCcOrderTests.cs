using EventRegistration.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// CC addresses keep the order the organizer entered them in (not alphabetical).
[Collection("api")]
public class MailPlanCcOrderTests(ApiFixture api)
{
    [Fact]
    public async Task Resolved_addresses_keep_entry_order_and_drop_duplicates()
    {
        using var scope = api.Services.CreateScope();
        var audience = scope.ServiceProvider.GetRequiredService<MailPlanAudience>();

        var resolved = await audience.ResolveAsync([], ["zoe@example.com", "Adam@example.com", "mia@example.com", "ZOE@example.com"]);

        Assert.Equal(["zoe@example.com", "adam@example.com", "mia@example.com"], resolved.Select(r => r.Email));
    }
}
