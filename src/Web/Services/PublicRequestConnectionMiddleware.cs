using System.Data;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace LandPortal.Web.Services;

// Keep one non-pooled, role-validated connection for a content page's controller,
// components and response-start statistics. No content/permission cache or SQL
// transaction spans the request. Authenticated CMS pages and small form commands
// use the same lifetime; uploads/streams do not hold a connection while receiving bytes.
// Each new request still activates and verifies
// its own runtime role; EF readers and individual transactions finish normally.
public sealed class PublicRequestConnectionMiddleware(RequestDelegate next, bool adminOnly = false)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var action=context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var editorCommand=action?.ControllerName=="Admin" && action.ActionName is "Save" or "ChangeWorkflow" or "ApproveAndPublish" or "ScheduleFromEditor";
        var adminPage=action is not null && (action.ControllerName switch {
            "Admin" => action.ActionName is "Index" or "Create" or "Edit" or "Preview" or "History" or "Compare" or "EditorReadiness",
            "SiteAdmin" => action.ActionName is "Index" or "Edit" or "Preview",
            "SourceSync" => action.ActionName is "Index" or "Detail",
            "VideoAdmin" => action.ActionName is "Index" or "EditorState" or "Sessions" or "Status" or "Library" or "Subtitles",
            "AdminMedia" => action.ActionName == "Index",
            _ => false
        });
        var admin = context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/admin") &&
            (adminPage&&(HttpMethods.IsGet(context.Request.Method)||HttpMethods.IsHead(context.Request.Method)) ||
             HttpMethods.IsPost(context.Request.Method)&&editorCommand);
        if (!(adminOnly ? admin : DuePublicationMiddleware.ChecksRequest(context))) { await next(context); return; }
        var db = context.RequestServices.GetRequiredService<PortalDbContext>();
        if (db.Database.GetDbConnection().State != ConnectionState.Closed) { await next(context); return; }
        try
        {
            await db.Database.OpenConnectionAsync(context.RequestAborted);
            await next(context);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
