# Focused video tool discovery checks

Run on Windows with the existing operator-approved test tool configuration at `.local/video-tools.json` (properties `ffmpegPath`, `ffprobePath`, `ffmpegSha256`, `ffprobeSha256`). Do not commit executables or that local configuration. The harness copies tools into a unique isolated `.local/tests/video-tools-*` directory and never connects to SQL or IIS.

```powershell
dotnet run --project tests/LandPortal.VideoToolTests -c Release -- <repo-root>
powershell -NoProfile -File tests/LandPortal.VideoToolTests/configuration.ps1
```

Validated on Windows: **36/36 native/discovery/status checks** and separately **8/8 offline PowerShell configuration/parser checks**. Release Web and Worker builds: zero warnings/errors.

Coverage: fixed package directory and PATH; missing/invalid/unsafe tools; video-disabled gate and enabled video without manual approval fields; actual versions and H.264/AAC/JPEG encoders; optional legacy pins; actual Windows Execute denial; one synthetic MP4 transcode and fresh JPEG poster; output hash/cache; changed executable refusal; protected Worker status, stale/stopped/missing/tampered status, and persisted hash changes. PowerShell accepts empty paths/pins but rejects malformed explicit overrides and invalid Enabled values.

This is not another workflow E2E run. Existing 48/60 E2E suites were not rerun. Actual IIS service identity, installed tools and shared storage permissions still require operator verification after an approved deployment. No private configuration or operational data changed.

Private minimal runtime config check (offline, never logs values): `dotnet run --project tests/LandPortal.VideoToolTests -c Release -- --configuration <private-validation-json>`. Covers valid Staging, four flags, defaults, disabled SMTP, Runtime18, no manual processor fields, HTTPS and Production rejection. This JSON is a test input, not another operator deployment file.
