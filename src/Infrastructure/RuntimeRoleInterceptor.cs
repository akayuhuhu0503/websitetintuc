using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
namespace LandPortal.Infrastructure;
// The application role confines effective runtime rights to the project database.
// Explicit migration uses the operator connection without this interceptor.
public sealed class RuntimeRoleInterceptor(IConfiguration config) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) { using var command = Command(connection); command.ExecuteNonQuery(); }
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        using var command = Command(connection);var watch=System.Diagnostics.Stopwatch.StartNew();
        MigrationDiagnostics.Record("DIRECT_SQL_START",new{purpose="runtime-role-activation",fingerprint=MigrationDiagnostics.Fingerprint(command.CommandText),timeoutSeconds=command.CommandTimeout,transaction="NONE",pooling=false,connection=MigrationDiagnostics.ConnectionInfo(connection)});
        try{await command.ExecuteNonQueryAsync(cancellationToken);MigrationDiagnostics.Record("DIRECT_SQL_PASS",new{purpose="runtime-role-activation",elapsedMs=watch.Elapsed.TotalMilliseconds});}
        catch(Exception ex){MigrationDiagnostics.Record("DIRECT_SQL_FAIL",new{purpose="runtime-role-activation",elapsedMs=watch.Elapsed.TotalMilliseconds,error=ex.GetType().Name,sqlNumber=(ex as SqlException)?.Number});throw;}
    }
    private DbCommand Command(DbConnection connection)
    {
        var command = connection.CreateCommand();
        // Direct RPC avoids SqlClient's sp_executesql wrapper; SQL requires approle activation at session level.
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "sys.sp_setapprole";
        var role = command.CreateParameter(); role.ParameterName = "@rolename"; role.Value = "LandPortalRuntime"; command.Parameters.Add(role);
        var secret = command.CreateParameter(); secret.ParameterName = "@password"; secret.Value = config["Runtime:RolePassword"] ?? throw new InvalidOperationException("Thiếu mật khẩu application role cho runtime SQL."); command.Parameters.Add(secret);
        return command;
    }
    public static async Task ProvisionAsync(PortalDbContext db, string secret)
    {
        if (secret.Length < 24) throw new InvalidOperationException("Mật khẩu application role quá ngắn.");
        // The outer batch is parameterized; SQL's QUOTENAME safely quotes the required DDL literal.
        await db.Database.ExecuteSqlRawAsync("IF DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NULL BEGIN DECLARE @ddl nvarchar(max) = N'CREATE APPLICATION ROLE [LandPortalRuntime] WITH PASSWORD = ' + QUOTENAME(@secret, CHAR(39)); EXEC sys.sp_executesql @ddl; END;", new SqlParameter("@secret", SqlDbType.NVarChar, 128) { Value = secret });
        await db.Database.ExecuteSqlRawAsync("GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [LandPortalRuntime]; DENY UPDATE, DELETE ON OBJECT::dbo.AuditLogs TO [LandPortalRuntime]; DENY ALTER TO [LandPortalRuntime];");
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.InteractionPublicSnapshots',N'U') IS NOT NULL DENY UPDATE,DELETE ON OBJECT::dbo.InteractionPublicSnapshots TO [LandPortalRuntime];");
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.InquiryAssignments',N'U') IS NOT NULL DENY UPDATE,DELETE ON OBJECT::dbo.InquiryAssignments TO [LandPortalRuntime]; IF OBJECT_ID(N'dbo.InquiryCalendars',N'U') IS NOT NULL DENY UPDATE,DELETE ON OBJECT::dbo.InquiryCalendars TO [LandPortalRuntime]; IF OBJECT_ID(N'dbo.InquiryCalendarExceptions',N'U') IS NOT NULL DENY UPDATE,DELETE ON OBJECT::dbo.InquiryCalendarExceptions TO [LandPortalRuntime]; IF OBJECT_ID(N'dbo.InquiryExtensions',N'U') IS NOT NULL DENY UPDATE,DELETE ON OBJECT::dbo.InquiryExtensions TO [LandPortalRuntime]; IF OBJECT_ID(N'dbo.OutboxReconciliations',N'U') IS NOT NULL DENY UPDATE,DELETE ON OBJECT::dbo.OutboxReconciliations TO [LandPortalRuntime];");
    }
}
