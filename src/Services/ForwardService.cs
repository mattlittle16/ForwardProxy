using ForwardProxy.Model;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;

namespace ForwardProxy.Services;
public interface IForwardService
{
    Task<ForwardModel> ForwardAsync(HttpContext context);
}

public class ForwardService(ILogger<ForwardService> logger, IHttpClientFactory httpClientFactory) : IForwardService
{
    private HashSet<string> RequestHeadersToSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "host",
        "x-forward-url",
        "content-length",
        "transfer-encoding",
        "connection",
    };

    private HashSet<string> ResponseHeadersToSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "transfer-encoding",
        "connection",
        "upgrade",
        "proxy-connection",
        "proxy-authenticate",
        "proxy-authorization",
        "te",
        "trailers",
        "content-length",
        "content-type"
        //"server" // Optional: you might want to include this
    };

    public async Task<ForwardModel> ForwardAsync(HttpContext context)
    {
        logger.LogInformation("ForwardService ForwardAsync method called");
        var client = httpClientFactory.CreateClient("ignore-ssl");

        var message = new HttpRequestMessage(new HttpMethod(context.Request.Method.ToUpperInvariant()), context.Request.Headers["x-forward-url"].ToString());

        await AddHeadersAndCookiesAsync(message, context);

        var response = await client.SendAsync(message);
        
        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);    
        foreach (var header in response.Headers)
        {
            if (!ResponseHeadersToSkip.Contains(header.Key))
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
        }
        
        foreach (var header in response.Content.Headers)
        {
            if (!ResponseHeadersToSkip.Contains(header.Key))
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
        }
        
        return new ForwardModel
        {
            StatusCode = (int)response.StatusCode,
            ResponseData = await response.Content.ReadAsStringAsync(),
            Headers = responseHeaders
        };
    }

    private async Task AddHeadersAndCookiesAsync(HttpRequestMessage message, HttpContext context)
    {
        // Forward appropriate headers from the original request
        foreach (var header in context.Request.Headers)
        {
            if (!RequestHeadersToSkip.Contains(header.Key))
            {
                try
                {
                    if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    {
                        // If it's a content header and we have content, try adding it there
                        message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Failed to add header {header.Key}: {ex.Message}");
                }
            }
        }

        // Add cookies from the context to the request
        if (context.Request.Cookies.Count > 0)
        {
            var cookieValues = context.Request.Cookies.Select(c => $"{c.Key}={c.Value}");
            var cookieHeader = string.Join("; ", cookieValues);
            message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }
        
        if (context.Request.ContentLength > 0)
        {
            context.Request.Body.Position = 0; // Rewind the stream to the beginning
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            message.Content = new StringContent(body, System.Text.Encoding.UTF8, context.Request.ContentType ?? "application/json");
        }
    }
}