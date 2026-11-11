# Focused WebBundle verification

Publish with the existing `scripts/build-for-iis.ps1` and a private complete `.env`.
The runner reads bound configuration but never opens either SQL connection.
Use an absolute path for the package argument (dotnet run can change cwd).

```powershell
dotnet build tests/LandPortal.WebBundleTests -c Release
dotnet tests/LandPortal.WebBundleTests/bin/Release/net10.0/LandPortal.WebBundleTests.dll C:\path\TTDL-IIS\web
powershell -NoProfile -File tests/LandPortal.WebBundleTests/check-package.ps1 -WebRoot C:\path\TTDL-IIS\web
```

Real Kestrel HTTP uses the production banner upload controller, permission lookup,
antiforgery and MediaService, with SQLite and a synthetic authentication handler
only in this test project. The production startup/profile is not weakened.
Tests validate configuration precedence, external stores, artifact denial,
PNG normalization/NotScanned/hash, mixed release denial and layout rollback.
Small fixtures stay in a unique temporary folder; actual package input is read-only.

`Worker.dll --verify-package` exits before SQL, scanner or queue processing. This
checks the actual published executable/configuration and launcher, **not** the IIS
service account or a real video processing job. Real IIS mapping/ACL and supervised
Worker startup remain operator acceptance after deployment. Do not interpret this
focused suite as SQL workflow E2E or as a reason to rerun the prior 48/60 E2E suites.
