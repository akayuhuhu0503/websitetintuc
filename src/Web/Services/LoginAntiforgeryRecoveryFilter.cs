using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LandPortal.Web.Services;

/// <summary>Reject stale login submissions, then obtain a fresh GET without replaying credentials.</summary>
public sealed class LoginAntiforgeryRecoveryFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not AntiforgeryValidationFailedResult ||
            !HttpMethods.IsPost(context.HttpContext.Request.Method) ||
            !context.HttpContext.Request.Path.Equals("/admin/login", StringComparison.OrdinalIgnoreCase)) return;

        // Validation has already failed; the login action never runs. Fixed local destination,
        // no credentials/token copied, no cookie removal and no automatic POST retry.
        context.HttpContext.Response.Headers.CacheControl = "no-store, private";
        context.HttpContext.Response.Headers.Location = "/admin/login?formExpired=true";
        context.Result = new StatusCodeResult(StatusCodes.Status303SeeOther);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
