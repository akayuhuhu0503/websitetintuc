# Third-party dependencies

## Phase 2A server file validators

- SkiaSharp 3.119.4 and matching SkiaSharp.NativeAssets.Win32/macOS 3.119.4: MIT. Copyright 2015-2016 Xamarin, Inc.; 2017-2018 Microsoft Corporation. Original package LICENSE and native THIRD-PARTY-NOTICES are preserved in `licenses/media/`; native dependencies have their own notices. Package source and version: https://www.nuget.org/packages/SkiaSharp/3.119.4
- PdfPig 0.1.14: Apache-2.0, authored by UglyToad and contributors. License expression verified in the pinned NuGet nuspec; license text preserved in `licenses/media/`. Exact upstream source revision from package metadata: 88172af1c4d4f440949f59c94966c3880e3f6032. Package source: https://www.nuget.org/packages/PdfPig/0.1.14

These validators perform actual image decoding/PDF parsing, not malware scanning. Uploads remain NotScanned under an explicit Development loopback exception. Production unscanned uploads are blocked. Windows native loading was exercised locally; Linux native packaging/deployment has not been accepted. Package vulnerability audit evidence is recorded in the final Phase 2A report and is not inferred from this notice.

## Frontend dependencies

### Current Phase 2A editor

- Jodit 4.17.3: MIT, Copyright 2013-2026 https://xdsoft.net. Exact npm version and registry integrity are pinned in package.json/package-lock.json. Original package LICENSE.txt is copied with self-hosted ES2021 bundle/CSS to wwwroot/vendor/jodit by `npm run build` and included in local publish. Official source: https://github.com/xdan/jodit ; license: https://github.com/xdan/jodit/blob/main/LICENSE.txt . The npm package contains the same MIT permission text. No paid Jodit PRO plugin or required remote API is used.
- TypeScript 5.9.3 remains Apache-2.0, build-only. Jodit table and clipboard features are bundled locally; captions and image/PDF picking use project-owned code and protected media endpoints.

The editor component was changed because the existing Quill core cannot preserve/edit the actual migrated table/figure model; Phase 1 documented the read-only workaround. Phase 2A requires editable tables/captions and preserves the MVC theme, controller routes and application architecture. Jodit edits HTML tables/figures directly; metadata-only edits preserve the original sanitized body. All saves are sanitized on the server. The 12/10/2026 Phase 2A npm audit of the current lock reports 0 vulnerabilities in its configured feed; this does not replace production security acceptance.

### Historical Phase 0-1 editor (not loaded by current views)

- Quill 2.0.3: BSD-3-Clause. Pinned in package.json/package-lock.json; local dist files and original LICENSE copied to wwwroot/vendor/quill by `npm run build`. No editor CDN request occurs at runtime. Official license: https://github.com/slab/quill/blob/v2.0.3/LICENSE
- TypeScript 5.9.3: Apache-2.0; build-only. Transitive dependency names/versions/integrities are recorded in package-lock.json.

`npm audit --json` was run on 12/10/2026: one low vulnerability, GHSA-v3m3-f69x-jf25 / CVE-2025-15056, affecting Quill 2.0.3 HTML export. No patched stable version exists in the advisory. This app does not call getSemanticHTML or expose an HTML export endpoint. It reads editor DOM and always sanitizes in HtmlPolicy.Clean on the server before storing a revision; posted content returned on validation errors is also sanitized before editor initialization. CSP blocks inline scripts and external scripts. This is a mitigation for the used path, not a declaration that the dependency is vulnerability-free. Reassess the editor or adopt an upstream fix before production. Advisory: https://github.com/advisories/GHSA-v3m3-f69x-jf25

Quill core does not provide source-table editing. In Giai đoạn 1, migrated table bodies are displayed read-only with an explicit notice and preserved byte-for-byte at form submission, then re-sanitized by the server. The title/summary/author/category metadata can still be edited. Table editing and uploads remain Giai đoạn 2 requirements.
