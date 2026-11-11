# Managed CMS image upload without antivirus (review branch)

Scope: administrator-only media upload through `POST /admin/media/upload` for JPEG/PNG. Inspired by `quanlyduan` attachment upload (authenticated user, extension/size cap, random storage name). It is **not** an identical copy of the report-attachment policy: the public GDLA origin requires stronger media validation.

Status (2026-11-11): **Release build, isolated service/controller checks and real HTTP/SQL Server fixture workflow PASS (48 final-run checks); not merged/deployed/enabled on IIS.** See `managed-upload-iis-handoff.md` for test-host scope and deployment limits. The hosting organization must approve the CMS-only exception before enabling it.

## Behavior

- Staging/UAT exact-target only, when private configuration contains `Media:AllowManagedImageWithoutAv=true`. False/omitted by default; Production and Development do not inherit this switch.
- Still requires authentication, `ManageMedia` + content/site role, antiforgery, one multipart file, limits (image bytes, pixels, frames), MIME/extension match, decoded image validation and private storage.
- JPEG and PNG only: decode, rewrite to canonical JPEG/PNG with SkiaSharp, strip client metadata and appended bytes; hash/check canonical output and bound size. Uploaded original bytes are never used as public image.
- `ScanStatus=NotScanned`, `ValidationStatus=ImageDecodedNoAv`. No AV `Clean` claim. CMS picker, home thumbnails and public `/media` understand only this exact policy, while checking current publication rights; public serving rechecks bytes and SHA256.
- GIF/WebP, PDF/Office, citizen forms, video, and 703 legacy assets preserve existing scanner/quarantine/import rules.
- Turning flag off denies public serving of these not-scanned images (fail closed). Do not enable on IIS without operational acceptance and rollback planning.

## Required before deployment

1. Review security exception and approve the limited JPEG/PNG staff-only scope. Confirm there are no legal/hosting policies forbidding it.
2. On a disposable UAT copy, restore dependencies and run a **focused build/test** (no full regression): Web and Infrastructure build; media policy test; authenticated PNG/JPEG image upload, deliberately malformed image, forced PDF rejection; CMS draft/preview/publish with a real test image; response returns NotScanned not Clean.
3. Confirm old media and SQL schema18 remain unchanged; no migrations or 703-media copy.
4. Build and deploy Web + Infrastructure together, with snapshots/backups and rollback. **Do not use Runtime18 DLL-only hotfix** for this change.
5. Explicitly set the private `Media:AllowManagedImageWithoutAv=true` only after above approvals/checks; verify upload and publish. Do not commit private config to Git.

Risk: image decoding libraries may have vulnerabilities; canonicalization and access control reduce but do not eliminate risk. Regular patching, least privilege and incident monitoring remain necessary.
