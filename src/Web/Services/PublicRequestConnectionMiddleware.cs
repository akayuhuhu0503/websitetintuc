using System.Data;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Services;

// Keep one non-pooled, role-validated connection for a content page's controller,
// components and response-start statistics. No content/permission cache or SQL
// transaction spans the request. Each new request still activates and verifies
// its own runtime role; EF readers and individual transactions finish normally.
public sealed class PublicRequestConnectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!DuePublicationMiddleware.ChecksRequest(context)) { await next(context); return; }
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
