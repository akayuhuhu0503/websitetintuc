# Managed upload HTTP / SQL Server check

Run from the repository on Windows with SQL Server LocalDB and the repository .NET SDK available:

```powershell
powershell -NoProfile -File tests/LandPortal.ManagedUploadHttpTests/run.ps1
```

This focused runner creates a uniquely named `LPNoAv_<random>` LocalDB instance, then an **absent** `LandPortal_Test` database with private MDF/LDF paths for this run. It applies the official 18 migrations only to that fresh database, uses compatibility level 160, provisions its application role and creates four synthetic Identity users. Passwords and the application-role secret stay in process memory. It never loads deployment `.env`, LAN credentials or production configuration.

The generated Kestrel host compiles the current `src/Web/Program.cs` with the production Web assembly, views, middleware and controllers. Only the configuration source (isolated process environment) and `MediaUploadOptions` DI registration differ. The override refuses anything except the generated LocalDB instance, exact test database, loopback Development URL, scoped private storage and Disabled mail. It does not alter operational UAT/Production guards. Document endpoints retain their existing exact SQL Test/private-storage guard.

HTTP checks use real cookies, Identity login, the actual displayed arithmetic CAPTCHA and antiforgery tokens. They cover five formats, private preview/download, persisted draft attachments, submit/review/publish, unauthorized requests and post-publication byte/download verification. Fixture schema provisioning is not evidence for an IIS migration or deployment.

Reports and logs remain under `.local/tests/noav-http-<random>`, outside Git. The host and only the generated LocalDB instance are stopped when finished. Databases/files are retained for diagnosis; there is no DROP, reset, restore or deletion of another environment. Failed runs are kept separately and never counted as successful final checks. No full regression, source-media import or real email is performed.
