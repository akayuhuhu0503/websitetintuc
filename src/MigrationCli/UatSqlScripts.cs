using LandPortal.Infrastructure;

namespace LandPortal.MigrationCli;

public static class UatSqlScripts
{
    static string Literal(string value)=>"N'"+value.Replace("'","''")+"'";
    public static string Header(string database,string server)
    {
        if(!UatDeployment.IsTargetDatabase(database)||string.IsNullOrWhiteSpace(server)||server.Any(char.IsControl)||server.Contains("REPLACE",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Confirmed target database and physical SQL identity required.");
        return $"""
:ON ERROR EXIT
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
