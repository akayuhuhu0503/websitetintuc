# Focused managed-video HTTP/SQL E2E

Run from the repository on Windows:

```powershell
powershell -NoProfile -File tests/LandPortal.ManagedVideoHttpTests/run.ps1
```

Requires installed SQL Server LocalDB, repository .NET SDK, and operator-supplied FFmpeg/FFprobe paths + SHA pins in private `.local/video-tools.json`. No binaries or credentials are committed. Reuses the existing isolated HTTP test approach; does not run the managed-image/document suite.

Creates a new `LPVideo_<12hex>` instance and fresh `LandPortal_Test` files under one `.local/tests/video-http-*` run. Uses all 18 official migrations only on that newly guarded empty database. No existing LAN/Test/UAT/Production database is contacted. Real Kestrel/Identity cookies, CAPTCHA, CSRF and runtime application role; four generated users/passwords stay in memory. The generated host uses real Web startup/controllers/views; only isolated configuration and test DI VideoOptions differ. Operational UAT/Production guards remain unchanged.

One synthetic MP4, actual FFprobe/FFmpeg transcode and JPEG poster; scanner is truly unavailable. Tests exact upload bytes/hash, permission denial, malformed files, private/public access, approval/publication, HTTP Range and homepage. Includes explicit UAT configuration gating without connecting to its fictitious SQL hostname; no-AV off rejects unscanned playback. Evidence records actual completed assertions, not an invented fixed total. Initial failed runs remain separate.

Owned HTTP process and LocalDB instance stop in finally blocks; private evidence remains for inspection. No operational migrations, backups, real accounts, email, IIS or 703 source media are touched. This is not a browser GUI or live IIS acceptance test.
