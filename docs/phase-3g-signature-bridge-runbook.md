# Signature bridge / exact Dev import runbook

[Kết quả thực](phase-3g-signature-bridge-report.md): **APPLIED_VERIFIED_LOCAL**, batch26af.../a6b... đãcommit3/4/7/6, repeat0delta. Không chạy lại helper Apply của run cũ vào các report đã tồn tại. Không ký approval mới hoặc mở rộng batch.

## Kiểm tra bridge riêng

Project `tests/LandPortal.SignatureTests` khôngSQL. Dùng **public technical signed fixture** có sẵn, không dùng real approval làm raw-output fixture. Restore/build vào directory mới; các lệnh đã dùng trong run:

```powershell
# At workspace root, dotnet from scripts/common.ps1. Set NEW build/report paths.
rtk proxy .local/dotnet/dotnet.exe restore tests/LandPortal.SignatureTests/LandPortal.SignatureTests.csproj --locked-mode --artifacts-path $newBuild
rtk proxy .local/dotnet/dotnet.exe build tests/LandPortal.SignatureTests/LandPortal.SignatureTests.csproj --no-restore --artifacts-path $newBuild
rtk proxy .local/dotnet/dotnet.exe ($newBuild+'/bin/LandPortal.SignatureTests/debug/LandPortal.SignatureTests.dll') $publicSignedFixture $newReport
```

Run đầu tạo lock bằng `--use-lock-file`; lockfile hiện có để những lượt sau dùnglocked.8groups là focusedsignature checks; khôngfullsuite/SQLTestfixturewrites. Native tamper/unsigned/unpinned/hashnegative và realPowerShellerror dùngengine thật; expired/trust envelope tests là policy negative, không tự cài certificate hết hạn để tăng bằng chứng. Failclosed với bất kỳstderr/unknownbytes/nonzero/khôngJSONduynhất/noncesai/hashsai/nativefalse/untrusted/EKU/date/pinsai. Không thêm filterstripCLIXML.

## Operator build và approval

CLI mới ở `.local/build/phase3g/phase3g-bridge-20261021T032845Z-fb25abad/bin/MigrationCli/debug/MigrationCli.dll`, SHAe52a8f...; giữ binary cũ và packet cũ. `build-attestation.json`/`operator-upgrade-audit.json` công khai lý do đổitool đã được usercho phép, không ghi đè `batch-review.json`. Approval ký PlanSHA/target/SID/expiry/actions, không ký binaryhash; reuse chỉ vì đúngmanifest và scope nguyên. CurrentUsercertificate94797... không cần tạo/trust lại. Signedapproval80ed... đã dùng, expiry21/10 21:54:56VN; không tự gia hạn. Sauhạn hoặc packetproof/hashđổi phải theo quy trìnhexactapproval, không fallback.

Dev hiện40/60media, không còn baseline37/53toànDB. Bảo vệ riêng37old/53original và47oldrevision. Updatepreview/delta cho batch tiếp; không trừ tay210 hoặc copy TestDB.53oldNotScanned vẫnkhôngcóphêduyệtpromote.

## Readonly verify / reconcile

Trong operatorprocess Development đã nạp correctnamedDB/roots/pin/mailDisabled theo runbook; `.local/phase3g-bridge-context.ps1` là context của run hiện tại, tự kiểm exactplan/approval/binary/8checks, không tựApply. Chuẩn bị `$freshOut` dưới Phase3F unique:

```powershell
. ./.local/phase3g-bridge-context.ps1
$freshOut='.local/evidence/phase3f/readback-'+[Guid]::NewGuid().ToString('N')
rtk proxy $script:DotnetPath $cli batch-reconcile --report ($freshOut+'/reconcile') @deps
rtk proxy $script:DotnetPath $cli safe-verify --report ($freshOut+'/verify') @deps
```

Chỉ còn dùng được khi currentDefenderproof vẫnstrict-valid; proof drift thì tool phải reject, không đổi`$deps` dưới signedplan cũ. Reconcile mongđợi10committed/0missing/0errors choeligible scope,30Blocked giữnguyên. `ImportRuns.Status`legacyPartial không tựđổiSQL; đối chiếureceipts/actualreadback. Thao tácwritekhác cầnexactapprovedscope vàfreshbackup; codeDemandtiếptục kiểmchữký/hash/scope/backup mọi lần. Rollback cầnsignedactionRollback đúngphạmvi, chưachạyvìkhônglỗi. KhôngrestoretoànDBđểxửlýlỗinhỏ.

## Chạy lại web local đọc dữ liệu

Web không đổi source trong mốc này; smoke dùng build đã có, mailDisabled/unscannedbypassfalse/SQLDev. Profile helper xác minh existingpacket và process config; khôngsqlmigrationstartup:

```powershell
. ./.local/phase3g-bridge-context.ps1
$env:Public__BaseUrl='http://127.0.0.1:5387'
rtk proxy $script:DotnetPath .local/build/phase3g/stabilization-current/bin/Web/debug/Web.dll --contentRoot (Join-Path $script:ProjectRoot 'src/Web')
```

Kiểm `/bai-viet/b4a64a8c-6dcc-462c-aa10-43d15220f1db`, category/search và7ảnh đã nhập; khôngsửa bài/Identity đểdemo. Webtemporary của run đãstop, port5387 khôngđược coi đanglive. KhôngStartworker/email thật/đổiDNS/deploy. SQLtimeoutUNKNOWN vàhistoricalartifactPARTIALgiữriêng, khônggộpHTTP16+signature8thànhfullsuite.
