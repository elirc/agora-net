using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Filters;

/// <summary>Opt-in mapping for local-only operations whose transaction has rolled back on failure.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class LocalSqliteWriteAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var error = context.Exception as SqliteException ?? (context.Exception as DbUpdateException)?.InnerException as SqliteException;
        if (error?.SqliteErrorCode is not (5 or 6)) return;
        context.Result = new ConflictObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Local database is busy",
            Detail = "The local operation could not complete because of database contention. Reload current state before retrying.",
        });
        context.ExceptionHandled = true;
    }
}
