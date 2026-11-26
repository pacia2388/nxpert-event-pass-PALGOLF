namespace EventRegistration.Api.Services;

/// <summary>
/// The inline-image limits of a composed email (config section Mail:InlineImages). The web editor
/// loads the same numbers (<c>GET api/settings/mail-limits</c>) so its help text and checks match.
/// </summary>
public class MailImageOptions
{
    public int MaxSourceBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxImageBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxTotalBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxCount { get; set; } = 5;

    /// <summary>
    /// Request body cap: the inline-image budget as base64 (about a third larger), the 25 MB
    /// attachment cap, and 1 MB for the text and multipart framing. The real 25 MB check still runs
    /// on the built email, so this only lets a valid request reach it.
    /// </summary>
    public long RequestLimitBytes =>
        (long)Math.Ceiling(MaxTotalBytes * 4.0 / 3.0) + MailAttachments.MaxEmailBytes + 1024 * 1024;
}

/// <summary>Allows a compose/save request as large as the configured inline-image budget.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MailRequestBodyLimitAttribute : Attribute, Microsoft.AspNetCore.Mvc.Filters.IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(
        Microsoft.AspNetCore.Mvc.Filters.ResourceExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.ResourceExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<MailImageOptions>>().Value;
        var feature = context.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = options.RequestLimitBytes;
        await next();
    }
}
