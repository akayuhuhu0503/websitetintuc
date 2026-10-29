# Cập nhật sau owner approval — 22/10/2026

Batch9 đã **APPLIED/VERIFIED LOCAL**, Dev259/270revision/602media/518alias;0mismatch/repeat9AlreadyCommitted. Article3075 parser và nhậpTest1bài5ảnhPASS;PDF3205vẫnBLOCKEDpolicy48MiB. Xem [báo cáo tiếp nối](phase-3l-approved-batch9-report.md) và `.local/phase3l-approved-current.json`. Báo cáo bên dưới là checkpoint **trước phê duyệt**, không dùng nhãn unsigned/250 cũ để thực hiện lại batch. CHƯA PRODUCTION READY.

---

# Phase 3L — PARTIAL

Run `phase3l-20261022T031058Z-5a15a959`, pointer `.local/phase3l-current.json`. Evidence riêng ở `.local/evidence/phase3f/<runId>`, proof ở `phase3c/<runId>`, restore ở `phase3j/<runId>/restore`. **`new9-final-ready.json` là checkpoint sau sửa cuối**; `new9-ready.json` và các preview trước đó là bằng chứng theo thời điểm.

**Chưa ký approval mới, chưa Apply Dev. CHƯA PRODUCTION READY.** [Runbook](phase-3l-runbook.md).

## SQL: xác định được operation, chưa xác minh nguyên nhân gốc

Đã capture đúng template có fingerprint cũ `089e3a4e68313e7e8f8e3f485cfda69faab9024ccae3d7d5885d0d8a535fa0f3`:

```sql
SELECT CASE WHEN EXISTS (
 SELECT 1 FROM [AuditLogs] AS [a]
 WHERE [a].[Action] = N'Phase3C.InventoryBinding'
   AND [a].[Actor] = @ToString AND [a].[Detail] = @sha
) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
```

Hai parameter là nvarchar4000. Diagnostics chỉ ghi template metadata cố định, tên/type/size parameter, không ghi values, mật khẩu application role hoặc audit payload.

- Controlled reproduction `sql-second`: SQL-2 sau35.034,8ms; SPID61, clientConnectionId `6015de03-2932-4509-8592-4e113299d698`; transactionNONE. Role activation trước đó PASS2,1ms.
- Một mẫu ownPID ghi `PAGEIOLATCH_SH`, blocking_session_id0 và transactioncount0. Observer mất kết nối sau đó, nên không có wait timeline đủ35giây. Không kết luận “không blocking” cho toàn bộ thao tác.
- SQL báo physical_memory_low=true, working memory158.700KiB; TestAUTO_CLOSEON. Đây là quan sát/hypothesis, chưa chứng minh nguyên nhân.
- Actual XML plan của đúng template, chạy riêng bằng operator/System.Data.SqlClient: AuditLogs25.680hàng; clustered scan đọc24.927hàng, trả1result;1.221logicalreads/3physicalreads/1.297read-aheads. Nodeelapsed14ms, cảprobe36ms. Runtime-role synchronous comparison trên cache đã warm:11msPASS. Plan này không phải plan thu được ngay trong hanging runtime query.
- Probe Microsoft.Data.SqlClient riêng: sync timeout5s FAIL-2/11,1s; async PASS17,2s nhưng role activation mất16,8s, actualEXISTS31ms. Không đủ căn cứ kết luận lỗi riêng async driver.

Không sửa index, NOLOCK, CommandTimeout, AUTO_CLOSE, pooling, isolation, SQL settings hoặc quyền. Thử nghiệm giữ connection mở không khắc phục được lỗi35s nên đã revert trước build cuối. Initial observer harness đã cancel một own readonly probe khi observer lỗi; giữ exit-1/raw riêng, không đếm đó là SQLtimeout. Script cuối không hủy CLI sớm chỉ vì observer lỗi và không copy lại toàn bộ Test fixtures.

**ROOT CAUSE UNKNOWN / MONITORING.** Dev3K historical preflight không có detailed trace; không phục dựng giả. Bản mới hỗ trợ trace innerTest dependency của Dev qua `MigrationDiagnostics:RecoveryTestRoot`, giữ guard diagnostics Development/Test.

## Tạo packet và kiểm chứng sau sửa cuối

SQL-readiness riêng inventory9 PASS trước refresh. Command này luôn trả `ScannerChecked=false`, `EligibleForApply=false`, không có quyền thay preflight hoặc Apply.

| Thao tác | Kết quả / thời gian process |
|---|---|
| Test preview9 | PASS36.904ms; một binding reader mất25.652ms, rủi ro vẫn còn |
| Test preflight9 | PASS2.758ms; ABSENT_CONFIRMED9/0commit/0errors cho Keepplan mới |
| Dev preview9 | PASS9.070ms;9CreateNeedsApproval |
| Dev preflight9 | PASS2.138ms; ABSENT_CONFIRMED9/0commit/0errors |

Không dùng4PASS này để xóa các timeout trước đó hoặc tuyên bố SQL đã ổn định theo SLA. Không chạy lại batch đã commit. Không có CMS import/revision/publication mới trong3L.

Sau sửa cuối, **đã chạy lại preflight với cùng plan không đổi byte**: TestPASS2.709ms, DevPASS2.326ms; cả hai9absent/0commit/0errors. Đây là2lần kiểm tra mới trên binary cuối, không cộng với4lần tạo packet thành một fullsuite.

Tối ưu có bằng chứng: vòng reuse trước đây đã đọc receipts của job nhưng lại chạy thêm một `EXISTS` cho từng proof. Bản cuối dùng snapshot SQL mới trong operation, dưới exclusive job lease, cap181 để phát hiện vượt180; kiểm tra mỗi reference qua hashset receipt đó. Không dùng persistent trust cache, không bỏ hash/byte/engine/definitions/TTL/policy hoặc durable binding. Native refresh18 trước sửa ghi20AuditSELECT; reuse18 sau sửa ghi2AuditSELECT. Hai workload khác nhau về native scan, nên **không dùng số này làm benchmark thời gian**, nhưng trace xác nhận đã loại18receipt queries lặp.

`--reuse-only` mới từ chối stale/missing proof trước scanner. Kiểm tra thật:18proof143 được reuse,0native scan/0receipt thêm, proofset bytes giữ nguyên; legacyproof140 bị từ chối rõ ràng, không quét60files. Harness đầu so chuỗi lỗi bị PowerShell wrap dòng nên báoBLOCKED sai; đã giữ raw, sửa riêng whitespace assertion và kiểm lại bằng label mới, không sửa guard ứng dụng.

## Defender và packet9

Chỉ refresh18SHA của job hiện có `4bf9d6d5-4fc8-460d-970e-036e883b0906`:18/18nativeClean, engine1.1.26090.9, definitions1.461.143.0. Durable receipts18→36,2/resource; giữ giới hạn3/resource/180job. Không refresh60legacy, không scan/promote53originalDev. Proof140 của60legacy vẫn STALE. Không khóa definitions hoặc dùng mockClean.

Packet mới:

- BatchID: `e02a8cb2-c7a4-4e9e-8884-737ee3b3cd01`.
- ManifestSHA256: `6e2374fd20fd20d74b48f1fd5c9d9c3d7437a119723f7a37b54514956d710eed`.
- Đích: MinhAnh\SQLEXPRESS / LandPortal_Dev.
- Delta dự kiến: **9content/9revision/18asset/18alias**;8PNG/10PDF,61.571.537byte. Chưa nhập vào Dev.9bài Test đã có từ3J, không đếm thành import3L.
-9Rows giống exact3Kplan; chỉ BatchID, RecoveryProofSha256, CreatedUtc thay đổi. Không giữ ID/hash3K hoặc tái dùng approval3I.
- Unsigned review: `.local/signing/reviews/<runId>/new9.json`. Chứng chỉ94797BD79E275B09DEF613036BC00DF4EA91345F được kiểm chỉ đọc: nativeAuthenticodeValid, CurrentUserchain/EKU/date hợp lệ. Không tạo trust/chứng chỉ mới, mở private key để verify hoặc ký approval.

Independent readback đối chiếu9SourceIdentity/title/body/summary/author/date,18real media bytes/hash/scanstates và18URLaliases; đối chiếu proofSHA, native provider, quarantine bytes, SQL receipts và budget. Conflict/duplication được kiểm qua preflight. Không gọi đó là kiểm toàn bộ Test database chứa fixture.

Proof sớm nhất hết hạn23/10/2026 03:28:36UTC. Engine/definitions/updatedtime đổi có thể làm invalid trước TTL. Chỉ ghi READY_FOR_APPROVAL khi `health-end.json` và `new9-ready.json` xác nhận freshness cuối. Đây chưa phải authority ký/Apply; trước Apply vẫn phải freshcheck và dừng khi timeout/drift.

## Backup và actual restore mới nhất

Backup mới `.local/backups/LandPortal_Test-20261021T015310Z/ll0f39ab66`: Dev/TestCOPY_ONLY/CHECKSUM/VERIFYONLY/header/SHA PASS;661Devfiles+155scoped dependencies,0missing/hashdiff; file restore checkPASS. Không ACLgrant hoặc overwrite.

Addendum `after-proof9`: SQLTest sau18refresh receipts +74tệp exact source/proof/quarantine/plan/unsignedreview/tool; capture/readbackPASS. Các tập dependencies giao nhau, không cộng thành số tài nguyên nguồn. FutureApply phải backup thêm actual signed approval/receipt.

Sau build cuối, `final-tool-v2` bổ sung13tệp tool/source/packet, hash/readbackPASS. Lần capture tool đầu bị guard từ chối vì sourcecode nằm ngoài phạm vi `.local` của helper; không nới guard. Đã giữ lỗi, tạo bản sao source đúng byte trong private evidence và dùng snapshot mới. Backup SQL/media hoàn chỉnh trước đó không bị ghi đè hoặc coi lỗi này là PASS.

**Đã thực chạy RESTORE DATABASE WITH MOVE/CHECKSUM/RECOVERY** vào `LandPortal_Restore3J_d9d16f42`, đặt READ_ONLY, không HTTP/Worker/mail. Không restore đè Dev/Test và không DROP recovery235 cũ.

- DBCCCHECKDB0errors;10exported tables/schema/migrations match all columns với Dev không thay đổi và trạng thái backup.
- Counts derive từ restored rows: **250content/261revision/584media/500alias/68snapshot**,53NotScanned giữ nguyên; không hardcode235/250 để nghiệm thu.
-661restored files,584media hashes, snapshot hashes, revision pointers, links/source/aliases PASS; đọc bài đại diện3loại và mở thật3PDF.
- Identity và SQLpermissions hash khớp; không log raw credentials. Evidence: `independent-readback-3l.json`, `access-readback-3l.json`.

Phục hồi trên hạ tầng Bộ/RPO-RTO/offsite vẫn NOTRUN.

## Phần còn thiếu

[PDF3205 và18canonical](phase-3l-pdf-and-exceptions.md): cap đầu tiên ở crawler32MiB trước body; importvalidation/recovery/scanner cũng32MiB. Không do Webupload/API đã được chứng minh. Đề xuất48MiB riêng sourcePDFallowlist/policy version xuyên pipeline, chờ duyệt; chưa tăng global hoặc download file.

**0/18canonical nhập thêm.** Nguyên nhân chính trong bảng18:11host/mapping,4oversizePDF,1invalidPDF,1pixel,1srcset. Article3075 có URL cụt từ `srcset` trong rawcache; chưa sửa parser/xóa requiredURL để ép import.3parser identities lịch sử là tập riêng;52typed/50canonical và34Dev-gap chưa đóng. Proof Test không cấp quyền chuyển53Dev thànhClean.

[Handoff hạ tầng/UAT/NukeViet](phase-3l-infrastructure-handoff.md):0new network requests3L; NukeViet giữ BLOCKED_BY_NETWORK theo3K, chưa có export/realinventory, adapter giữ nguyên.16businessUATcases PREPARED/NOTRUN; chỉ có technical readback local. Còn thiếu target, serviceACL, scanner/SMTPProduction, MFAflow, HTTPS301, người kýUAT,3Word nghiệp vụ và privateLookupauthority.

## File, kiểm thử và bảo toàn

ChangedC#: `MigrationDiagnostics.cs`, `MediaRecovery.cs`, `RecoveryCommand.cs`, `RecoveryEvidence.cs`; diagnostics/read-onlySQLreadiness/boundedreceipt materialization. New `scripts/diagnose-phase3l.ps1`; operator harness/probe trong ignored`.local/phase3l-*`. **Không migration/index/model hoặc policy scanner/signer change.**

Build cuối CLI/Infrastructure:0warning/0error, CLI SHA `f13f2485110c15aae44ba714bd04667fe904366ab8a2539135db9aae10aa937a`. **Sau sửa cuối:**4focusednegative guards chạy lại PASS riêng (Devrecovery, inventory hash, proofset hash, diagnostic root); stale-definition reuse denial được ghi riêng; reuse18/no-native-scan và2preflights PASS. Readback9/18/18 và17Devpreservationgroups đã chạy lại sau sửa cuối. Không cộng các lần cũ/mới thành fullsuite. Fullregression, websitewideUI/HTTP, EICAR/failurecontrols và newimport-idempotency NOTRUN.

Dev giữ **250real source/250SourceRecords/261revision/584media(357image227PDF)/500alias**,53originalNotScanned+531Clean;70capture/68SQLsnapshot/8exceptions/2video metadata0file.17groups0diff, Identity/env unchanged; schema20261015091551_V05EditorialDocuments.0DevApply nên không cần rollback. Historical raw artifact incidents giữ PARTIAL.

Bước ngắn nhất: exact owner approval cho packet mới → fresh proof/target/backup kèm signed approval/preflight → Apply9/verify/idempotency/smoke; dừng khi SQLtimeout/drift. Song song sửa3075 với rawfixture/checkpoint provenance, review mapping external resources và largePDFpolicy. **CHƯA PRODUCTION READY.**
