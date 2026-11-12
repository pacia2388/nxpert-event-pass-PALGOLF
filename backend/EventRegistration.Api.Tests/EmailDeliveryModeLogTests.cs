using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace EventRegistration.Api.Tests;

// At startup the API says where email goes (SMTP or the on-disk outbox), so a run's log
// shows it without anyone opening the local settings.
public class EmailDeliveryModeLogTests
{
    [Fact]
    public async Task Startup_log_says_email_goes_to_the_outbox_when_email_is_off()
    {
        await using var app = new LoggedApi();
        using var client = app.CreateClient();

        Assert.Contains(app.Logs.Messages, m => m == "Email delivery: outbox (SMTP is off).");
    }

    private sealed class LoggedApi : ApiFixture
    {
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        }

        public override async ValueTask DisposeAsync()
        {
            await ((IAsyncLifetime)this).DisposeAsync();
            await base.DisposeAsync();
        }
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Capture(Messages);

    public void Dispose() { }

    private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
