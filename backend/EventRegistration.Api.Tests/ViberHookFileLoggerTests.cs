using EventRegistration.Api.Services;
using Microsoft.Extensions.Logging;

namespace EventRegistration.Api.Tests;

// Sinch webhook lines go to logs/viber-hook/viber-hook-yyyyMMdd.log; other categories never do.
public class ViberHookFileLoggerTests
{
    [Fact]
    public void Writes_only_the_webhook_category_to_a_daily_file()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "viber-hook-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new ViberHookFileLoggerProvider(folder);
            var hook = provider.CreateLogger(ViberHookFileLoggerProvider.Category); hook.LogInformation("report {MessageId}", "msg-1");
            provider.CreateLogger("EventRegistration.Api.Services.PassService").LogInformation("not here");

            var file = Path.Combine(folder, $"viber-hook-{DateTimeOffset.Now:yyyyMMdd}.log");
            var text = File.ReadAllText(file);
            Assert.Contains("[Information] report msg-1", text);
            Assert.DoesNotContain("not here", text);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
