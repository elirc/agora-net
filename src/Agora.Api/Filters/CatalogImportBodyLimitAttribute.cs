using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Agora.Api.Filters;

/// <summary>Enforces the import bound even for chunked requests and non-Kestrel hosts.</summary>
public sealed class CatalogImportBodyLimitAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        const int limit = 1_048_576;
        var request = context.HttpContext.Request;
        if (request.ContentLength > limit) { Reject(context); return; }
        request.EnableBuffering();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - total)), context.HttpContext.RequestAborted);
            if (read == 0) break;
            total += read;
            if (total > limit) { Reject(context); return; }
        }
        request.Body.Position = 0;
        await next();
    }
    private static void Reject(ResourceExecutingContext context) => context.Result = new ObjectResult(
        new ProblemDetails { Status = 413, Title = "Catalog import JSON may not exceed 1 MiB." }) { StatusCode = 413 };
}
