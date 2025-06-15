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
        
        return StatusCode(forwardModel.StatusCode, forwardModel.ResponseData);
    }
}