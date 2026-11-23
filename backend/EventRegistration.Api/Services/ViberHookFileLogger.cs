using EventRegistration.Api.Controllers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventRegistration.Api.Services;

/// <summary>
/// Writes the Sinch webhook's log lines (<see cref="SinchWebhooksController"/>) to
/// <c>logs/viber-hook/viber-hook-yyyyMMdd.log</c> under the API folder, one file per day, on top of
/// the normal log. Other categories are not written here. A write failure never breaks the webhook.
/// </summary>
public sealed class ViberHookFileLoggerProvider(string folder) : ILoggerProvider
{
    public static readonly string Category = typeof(SinchWebhooksController).FullName!;
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) =>
        categoryName == Category ? new FileLogger(this) : NullLogger.Instance;

    public void Dispose() { }

    private void Write(LogLevel level, string message, Exception? ex)
    {
        var now = DateTimeOffset.Now; // server time (Philippine time in production)
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{(ex is null ? "" : Environment.NewLine + ex)}{Environment.NewLine}";
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"viber-hook-{now:yyyyMMdd}.log"), line);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class FileLogger(ViberHookFileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(logLevel, formatter(state, exception), exception);
        }
    }
}
