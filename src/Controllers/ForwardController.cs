using System.Threading.Tasks;
using ForwardProxy.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForwardProxy.Controllers;

[ApiController]
[Route("[controller]")]
public class ForwardController(ILogger<ForwardController> logger, IForwardService forwardService) : ControllerBase
{
    public async Task<IActionResult> Forward()
    {
        if (!Request.Headers.ContainsKey("x-forward-url"))
        {
            logger.LogWarning("x-forward-url header is missing in the request.");
            return BadRequest("Missing x-forward-url header.");
        }
        
        var forwardModel = await forwardService.ForwardAsync(HttpContext);
        
        if (forwardModel.Headers != null)
        {
            foreach (var header in forwardModel.Headers)
            {
                try
                {
                    Response.Headers[header.Key] = header.Value;
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Failed to set response header {header.Key}: {ex.Message}");
                }
            }
        }

        var exposeHeaders = new List<string>
        {
            "ETag",
            "Server", 
            "Location",
            "x-Amz-Cf-Id",
            "X-Amz-Cd-Pop",
            "X-Cache"
        };
        
        if (Response.Headers.ContainsKey("Access-Control-Expose-Headers"))
        {
            var existingHeaders = Response.Headers["Access-Control-Expose-Headers"].ToString().Split(',')
                .Select(h => h.Trim()).Where(h => !string.IsNullOrEmpty(h));
            exposeHeaders.AddRange(existingHeaders);
            exposeHeaders = exposeHeaders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        
        Response.Headers["Access-Control-Expose-Headers"] = string.Join(", ", exposeHeaders);

        logger.LogInformation("Response Headers Forwarded: {0}", string.Join(", ", Response.Headers.Select(h => $"{h.Key}: {h.Value}")));
        return StatusCode(forwardModel.StatusCode, forwardModel.ResponseData);
    }
}