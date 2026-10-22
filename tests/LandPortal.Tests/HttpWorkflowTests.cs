using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
// Retain the regression entry point; Phase2A tests continue exercising real SQL,
// Identity, CSRF, direct endpoint permissions, public isolation and stale requests.
public static class HttpWorkflowTests
{
    public static Task RunAsync(string workspace, IConfiguration config, DbContextOptions<PortalDbContext> options) => Phase2aHttpTests.RunAsync(workspace, config, options);
}
