using LandPortal.Infrastructure;

namespace LandPortal.MigrationCli;

public static class UatSqlScripts
{
    // sqlcmd (ODBC) defaults QUOTED_IDENTIFIER to OFF without -I. These are
    // session options, not database/server settings. GO makes the parse-time
    // options effective before EF batches and their nested CREATE INDEX EXEC.
    public const string SessionOptions = """
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO

""";
    public static string UnboundSchema(string migrations)=>":ON ERROR EXIT\n;THROW 51000, 'UNALLOCATED UAT TARGET: bind a reviewed target before execution.', 1;\nGO\n-- LANDPORTAL_SCHEMA_BODY\n"+SessionOptions+migrations;
    public static string BoundSchema(string database,string server,string migrations)=>Header(database,server)+migrations;
    static string Literal(string value)=>"N'"+value.Replace("'","''")+"'";
    public static string Header(string database,string server)
    {
        if(!UatDeployment.IsTargetDatabase(database)||string.IsNullOrWhiteSpace(server)||server.Any(char.IsControl)||server.Contains("REPLACE",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Confirmed target database and physical SQL identity required.");
        return $"""
:ON ERROR EXIT
{SessionOptions}
USE [{database}];
GO
IF DB_NAME() <> {Literal(database)} OR COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') <> {Literal(server)}
BEGIN
 ;THROW 51000,'Exact target mismatch; stop without database changes.',1;
END;
GO

""";
    }
    public static string RuntimeRole(string header,string login)
    {
        RequireLogin(login);
        return header+$"""
-- DBA ONLY, AFTER SCHEMA APPROVAL. Never run as the Web/Worker login.
-- Supply Runtime:RolePassword (24..128 chars) through SESSION_CONTEXT in this
-- same SSMS SQLCMD session. No secret is embedded in this artifact.
IF USER_ID({Literal(login)}) IS NULL THROW 51000,'Existing mapped application user required; no user/login creation.',1;
IF DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NOT NULL THROW 51000,'Existing application role: reconcile; no password reset.',1;
DECLARE @secret nvarchar(max)=CONVERT(nvarchar(max),SESSION_CONTEXT(N'LandPortal.RuntimeRoleSecret'));
IF @secret IS NULL OR LEN(@secret)<24 OR DATALENGTH(@secret)>256 THROW 51000,'Private runtime role secret missing or invalid.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @ddl nvarchar(max)=N'CREATE APPLICATION ROLE [LandPortalRuntime] WITH PASSWORD = '+QUOTENAME(@secret,CHAR(39));
 EXEC sys.sp_executesql @ddl;
 {RuntimeRoleInterceptor.PermissionsSql}
 GRANT CONNECT TO [{login}];
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 EXEC sys.sp_set_session_context @key=N'LandPortal.RuntimeRoleSecret',@value=NULL;
 THROW;
END CATCH;
EXEC sys.sp_set_session_context @key=N'LandPortal.RuntimeRoleSecret',@value=NULL;
GO
""";
    }
    public static void RequireLogin(string login)
    {
        if(string.IsNullOrWhiteSpace(login)||login.Equals("sa",StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(login,@"\A[A-Za-z][A-Za-z0-9_]{0,127}\z"))throw new InvalidDataException("Approved non-sa mapped SQL user required.");
    }
}
