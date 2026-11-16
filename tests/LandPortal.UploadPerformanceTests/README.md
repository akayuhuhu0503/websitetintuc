# Focused HTTP / SQL upload performance fixture

This console fixture creates a **new** `LPPerf_<12 hex>` LocalDB instance with
fresh `LandPortal_Test` files under `.local/tests/perf-upload-*`. All 18 official
migrations are applied only to that newly guarded empty test database. It does
not load `.env`, private deployment configurations, existing accounts or media.
The four Identity users are synthetic; their random passwords remain in memory.
Mail is disabled. The Web host and generated SQL instance are stopped afterward;
private evidence and test files are retained. No operational database is used.

Run from the repository root using its pinned SDK:

```powershell
powershell -NoProfile -File tests/LandPortal.UploadPerformanceTests/run.ps1 -DotnetPath .local/dotnet/dotnet.exe -Label after
```

The host compiles the real Web startup, controllers, views and middleware. The
test-only configuration source and approved managed upload options are injected
only after an exact fresh LocalDB / Development / loopback / storage / disabled
mail guard. No operating UAT or Production checks are changed.

Coverage is deliberately bounded: list/filter/create/edit, save, JPG/PNG,
small and near-limit PDF/DOCX/XLSX, duplicate/recovered uploads, wrong owner and
article, malformed hash, quarantine, invalid format, review/approved scheduling /
publication, public byte-preserving document download, and fresh permission
revocation. The reported check count includes setup and hash assertions; it is
not a count of separate user journeys or the full regression suite.

`http-metrics.jsonl` records server request elapsed time, EF command duration and
count, without SQL text, query strings or credentials. EF command timing excludes
connection opening, runtime role activation, complete response transfer, browser
rendering and user/network latency. Do **not** label the remaining elapsed time
as CPU or file I/O. `file-components.json` separately measures cached local file
read, SHA256, the actual Office/PDF validator and copy; it is not a disjoint trace
of each HTTP request. No performance thresholds fail tests on noisy developer
machines.

Near-limit fixture bytes are cached under `.local/perf-document-fixtures` so a
before/after comparison uses identical SHA256 values. `-Focus Near` uploads only
those three documents. For a historical baseline, `-BaselineHost` accepts a
retained host directory under this fixture's `.local/tests/perf-upload-*` tree.
It copies the complete old host output after building the current driver, keeping
its runtime DLLs unchanged. Record the baseline source revision and file hashes
alongside results; do not compare different Office fixture sizes.

A separate optional video measurement uses an already verified local FFmpeg /
FFprobe tool configuration (paths and SHA256 fields only; no downloaded binary):

```powershell
powershell -NoProfile -File tests/LandPortal.UploadPerformanceTests/run.ps1 -DotnetPath .local/dotnet/dotnet.exe -Label video -Focus Video -VideoTools C:\private\video-tools.json
```

It creates one 2-second synthetic MP4, uploads it through real session/chunk HTTP,
then waits for the real Web background service and SQL durable queue to transcode
MP4 and generate a poster. Test DI enables managed video only in the isolated
host; a valid, unavailable loopback scanner is used, and the result must remain
`NotScanned` with technical validation. `video-timing.json` includes queue wait
and status polling; it is neither an IIS throughput benchmark nor an end-to-end
playback regression suite. The video flag is absent during normal measurements.

Evidence from 16 November 2026 is summarized in the performance report. Runtime
SQL query reductions are repeatable counts; the small before/after timing samples
also include JIT, LocalDB, file-cache and machine-load variance. No IIS result is
claimed from these isolated runs.
