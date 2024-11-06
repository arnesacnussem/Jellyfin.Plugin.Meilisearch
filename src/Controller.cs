using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Meilisearch;

[Route("meilisearch")]
[ApiController]
public class Controller(ILogger<Controller> logger, MeilisearchClientHolder clientHolder) : ControllerBase
{
    [HttpGet("status")]
    public Task<IActionResult> GetStatus()
    {
        if (!clientHolder.Ok)
        {
            Plugin.Instance?.TryCreateMeilisearchClient().Wait();
        }

        return Task.FromResult<IActionResult>(new JsonResult(new
        {
            db = Plugin.Instance?.DbPath,
            meilisearch = clientHolder.Status,
            meilisearchOk = clientHolder.Ok,
            lastIndex = Plugin.Instance?.Indexer.LastIndex?.ToString() ?? "Not yet indexed"
        }));
    }
}