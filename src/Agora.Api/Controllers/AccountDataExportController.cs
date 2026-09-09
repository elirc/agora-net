using Agora.Api.Auth;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;
[ApiController,Authorize,Route("api/me/data-export")]
public sealed class AccountDataExportController(AccountExportService service):ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        try
        {
            var export=await service.CreateAsync(User.GetCustomerId()!.Value,ct);
            Response.Headers.CacheControl="private, no-store";
            return File(export.Bytes,"application/json; charset=utf-8",export.FileName);
        }
        catch(AccountExportTooLargeException exception)
        {return UnprocessableEntity(new ProblemDetails{Title=exception.Message,Status=422});}
    }
}
