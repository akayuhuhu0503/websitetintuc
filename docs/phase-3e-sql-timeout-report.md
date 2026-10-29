# Phase 3E — SQL timeout / Unblock migration, 19/10/2026

**Batch nguồn thật trên Test đã được mở chặn và kiểm chứng PASS LOCAL. Điều tra nguyên nhân timeout lịch sử vẫn PARTIAL / ROOT CAUSE UNKNOWN. CHƯA PRODUCTION READY.** Không Apply Dev hoặc crawl nguồn.

Run `.local/evidence/phase3e/phase3e-20261019T053606Z-89b5fb51`, pointer `.local/phase3e-current.json`. Đã đọc yêu cầu đính kèm, SRS1.0 đầy đủ, checkpoint/acceptance/3C/3D và code importer/approval; SRS trích và SHA lưu riêng trong run. Dùng nguyên batch Test3D `7768b8bf-25ee-4b6e-99e8-94b117a72694`, planSHA `2801713bf65b42e24d8c74e03394187d5d7919b792a0da29fd859aad04e80e0a`, checkpoint3A không đổi.

## Chín câu trả lời

| Câu hỏi | Kết quả có bằng chứng |
|---|---|
|Timeout chính xác ở đâu?|3D lỗi trước reportmkdir/CommitAsync, thuộc đường tiền kiểm; probe SQL SELECT độc lập cũng timeout. Thiếu command trace lịch sử nên **không thể xác định chính xác câu SQL cũ**. 3E đã gắn operation/stage/SQL fingerprint/session/transaction và không tái hiện timeout.|
|Nguyên nhân gốc?|UNKNOWN. Đã xác minh inefficiency:60 câuSQL kiểm receipt lặp +1binding, plan scan AuditLogs; giảm61→2. Không gọi đây là nguyên nhân duy nhất của SLEEP_BPOOL_STEAL/categorytimeout.|
|Sửa gì?|Receipt validation bounded theojob; reconciliation trước retry; phân biệt COMMIT chưa chắc chắn; prepare/hash nguồn trước SQLtransaction; readback theo batch; diagnosticTest redacted và giải phóng connection khi guard lỗi. **Không migration/index/timeout/.env/SQLconfiguration changes.**|
|Commit thật Test?|**PASS**:37Keep/3Blocked. Không gọi37 là37bài mới;0source content/assets/comparison mới.|
|Readback/verify/repeat?|**PASS**:37receipt/37source/37revision được audit tham chiếu,58media/79alias thuộc phần batch; verify0errors. Repeat37AlreadyCommitted/3Blocked,0delta source/revision/assets/aliases/audit/import-run.|
|Nội dung mới Test?|**0**.3new/7ảnh là đề nghị Dev từ3B, đã có trên Test trước3E. Các fixture kỹ thuật mới không cộng vào40real nguồn.|
|Dev nguyên vẹn?|**0khác/17nhóm**:37content/47revision/74alias/53NotScanned41ảnh12PDF/70capture68snapshot/8ngoại lệ/2videometadata0file. Identity/env giữ nguyên.|
|Dev có thể trình duyệt?|**READY FOR TECHNICAL REVIEW / NEEDS_APPROVAL**, bằng chứng Test đã đủ cho phần pipeline; chưa READY_EXECUTE. Giữ manifest3D3new/7ảnh/1comparison/6Keep; cần authority/trust/freshbackup/freshpreview/proof, positive signedDev NOT RUN.|
|Bước tiếp ngắn nhất?|Trình review gói độc lập3bài và authority; nhận export/approval bounded để kiểm chứng228URL chưa có snapshot. Không chờ xử lý xong PDF/video để chuẩn bị phần độc lập. Nếu timeout tái diễn, dùng instrumentation mới theo đúng session/fingerprint, không retry mù.|

## Chẩn đoán và giới hạn kết luận

`diagnostic-before/operations.jsonl` và `summary.json` đo connect và từng câu SELECT:receiptbatch3D=0,3ID đã có Test, không thấy lỗi kết nối. Preflight trước sửa hiệu năng chạy thành công, trạng tháiABSENT_CONFIRMED37/0receipt; đã kiểm stamp của các dòng chưa commit. Vì vậy không có căn cứ nói sửa một truy vấn đã chữa dứt điểm timeout vốn đã không tái hiện trước sửa.

Trace `preflight-instrumented-trace`:recoveryproof **61EFcommands**, một fingerprint lặp60lần;3.175s cho toàn bước. `receipt-plan.xml` là SHOWPLAN ước lượng, có ClusteredIndexScan khoảng23.316rows, không phải actual execution plan/IO statistics. Schema hiện chỉ có PK audit và indexContentId/CreatedUtc, không có indexAction/Actor; hai cột này không được tự sửa kiểu/index để mở rộng phạm vi. Không thêm index vì chưa đủ bằng chứng nó là nguyên nhân timeout30s và giải pháp đọc theojob đã kiểm được.

Sau sửa:recoveryproof2EFqueries (binding +boundedreceiptlist), thường1.56–1.72s trong các lần thực tế, vẫn kiểm60proof thật/SQLreceipt/path/hash/bytes/policy/TTL/currentengine. Không native scan mới, không sửa nhãnClean hoặc nguồn53Dev. Đây là so sánh vài lần local, không benchmark/khả năng chịu tải Production.

Metadata `diagnostic-*/tables.rows` là tổng row_count của **các index**, không phải số bản ghi nghiệp vụ; không dùng để báo số content/source. Số dữ liệu nguồn lấy từ readback định danh40 và preservation riêng.

`blocking-observation.json`:một quan sát sau run thấy session61/BRKR TASK/open_transaction_count1, blocking0; không có lock/wait rows trong phạm vi mẫu. Không kết luận không từng có blocking hoặc tự coi session này bị lỗi; không KILL. `memory-observation.json`:maxmemoryconfigured2147483647MB/min16MB, OS low-memory signalfalse tại thời điểm lấy mẫu, available~2.29GB; không đổi cấu hình. Không suy các snapshot hiện tại mô tả áp lực tài nguyên lúc3D. Không đọc nội dung database khác, không QueryStore/XE/serverlogging changes.

Lỗi3D “Execution Timeout Expired” phù hợp command execution timeout; **câu SQL/connection-phase cụ thể lịch sử chưa chứng minh**. Không có timeout/-2/lockwait được quan sát trong trace3E. Poolingruntime vẫnfalse theo ràng buộc application role; không bỏ role/quyền để tăng tốc. SQLcategorytimeout lịch sử là incident riêng, vẫn MONITORING/ROOTCAUSEUNKNOWN.

## Preflight → Commit → Readback

Các log mỗi invocation ở thư mục `*-trace`, có UTCstart/end, operationId, batchId, expectedRows37, commandId/fingerprint, sessionId, timeout30s mặc định, transactionNONE/ACTIVE. Log chỉ số lượng tham số, không giá trị, SQLtext/connectionstring/PII/password. Guardlocaltarget và sp_setapprole đo riêng bằng DIRECT_SQL events, không log secret. Reader event là thời gian mởreader; Step tổng bao gồm đọc/hashing. `timings*.json` tổng hợp, raw JSONL giữ nguyên.

| Thao tác | Kết quả / thời gian ghi nhận |
|---|---|
|Freshpreflight|checkpoint~0.085s,proof~1.562s,reconcile~1.743s;ABSENT_CONFIRMED37|
|Commit thật|~23.395s cả37dòng;37COMMIT_SENT/37ACK;37Keep/3Blocked|
|Readback lần1|verify~9.243s,snapshot~0.398s;0errors|
|Reconcile trướcrepeat|COMMITTED_VERIFIED37,missing0,errors0|
|Repeat đầu|~14.502s gồm reconcile~10.020s;37AlreadyCommitted/3Blocked,không thêm dữ liệu|
|SQLreadback40|trước/sau và quanhrepeat có JSONL riêng;40real/60media,hash0mismatch|

Các số ở bảng là thao tác thực, không cộng thành bộ test. Lầnrepeat/verify cuối sau guarddiagnostic cuối lưu riêng `repeat-final`, `verify-post-final-code`, không ghi đè các lượt trước. Lần commit mới37Keep xảy ra trước thay đổi cuối chỉ bảo vệ logger khi connectionđóng; importer/policy không đổi ở sửa cuối. Sau code cuối chạy lại SQLtests, Commit cùngbatch (reconcile/AlreadyCommitted) và verify/readback/HTTP.

`data-comparison.json`:40source records/payload/revisions/assets/files trước/sau commit không đổi. Chỉ +1ImportRun/+75AuditLogs (37Import+38warnings) trong lầncommit37Keep,0aliases mới. Batchreadback DataSHA **2832dae5b005f435271fa8a06a4fd8399c999f302e2f23e643edcef7e6603f20**, giữ nguyên qua verify/repeat. Batch37 có58media, toàn40real có60media; không ép hai phạm vi bằng nhau. Không rollbackpublication của37real vì đềuKeep và đã công khai trước đó.

## Retry, partial và commit chưa chắc chắn

`SourceBatch.ReconcileAsync` kiểm batch/plan/session/proof/store, receipt identity/hash/duplicate/owner, ImportRun/CrawlRun, revision/media/alias/nội dung thực và stamp của dòng thiếu. Trạng thái ABSENT_CONFIRMED / PARTIAL_VERIFIED / COMMITTED_VERIFIED / NEEDS_RECONCILIATION. Query/proof thất bại dừng, không retry vô điều kiện. Dòng đã có receipt đi verify, Commit lặp chỉ trảAlreadyCommitted; không lưu/đăng lại bài. Mỗi dòng còn thiếu vẫn kiểm lại trong serializable+applock.

Sau gửi COMMIT, exception/cancellation không bị gán “rollback chắc chắn”. TrảNeedsReconciliation/dừngbatch; trước COMMIT chỉ ghiRollbackConfirmed khi rollback thực thành công; rollback lỗi cũng chuyểnNeedsReconciliation. BatchCommand/SafeApply trảexit≠0 cho trạng thái này. Diagnostic kết thúc Step nghĩa call đã trả về; kết quả từng item/exit mới là quyết định thành công nghiệp vụ.

Tác vụ parse/hashraw của Prepare chuyển ra trước transaction; exact-byte đối chiếu materialization trongSqlImporter vẫn giữ để bảo vệ race. Đối với batch37Keep thực tế không materialize file trongtransaction. Chưa thiết kế lại materialization của batchnewmedia để loại hết fileIO trong transaction; đây là giới hạn cần đo tiếp nếu mở batch lớn, không tự bỏ byte checks.

## Test sau sửa cuối và bàn giao

Final focused **5/5PASS,0FAIL**, `phase3e-tests-20261019T055423Z-39200874`,17targetedpaths/0drift,buildWeb/CLI/Tests/dependencies0warning/error. Không full137/video/Worker/UIhàngloạt. Lượt4/5 do test stale còn đòi nhãnFailed cũ được giữraw; đã cập nhật assert chính xác NeedsReconciliation và lý do stale, không bỏ kiểm tra.5/5trước guardlogger cuối giữ riêng, không cộng thành10/10.

-60genuineproof:2SQLauditqueries;copybyteđúng nhưng proofpath không córeceipt bị từ chối. Không fakeClean/EICARSMTP.
-COMMITSQL thực rồi tiêm lỗi mấtack ởclient:NeedsReconciliation, contextmới COMMITTED_VERIFIED, repeat0mới. **Fault injection có nhãn**, không gọi là timeoutSQLtựnhiên.
-Partial1/2→đốisoát→resume; khácplanhash/stale bịchặn; precommitfault rollback thật, retrycontextmới; sourcecomparison giữeditor/public.
-HTTPsmoke11kiểm tra riêng:health/4bài/chuyên mục/JPEG+PDF đúngSHA/PDFRange206/guestadminpreviewACL. Owned5310dừngfinally. Không tính11HTTPthành11SQLtest hoặc fullsuite.

File mới: `src/Infrastructure/MigrationDiagnostics.cs`; MigrationCli `SourceBatchReconciliation.cs`, `SourceBatchReadback.cs`; scripts `phase3e.ps1`, `diagnose-phase3e.ps1`, `test-phase3e.ps1`; Tests `Phase3ETests.cs`. Sửa Configuration/RuntimeRoleInterceptor (diagnostic/close-on-error), MediaRecovery, SourceBatch, BatchCommand/SafeBatchCommand, test runner/evidenceallowlist/staleassert. Không migration/package/index/Identity/env thay đổi.

Đã có backupTest3C thật còn hạn<=12h làm checkpoint; không tạo lại whole-repo manifest hay backup hàng nghìn file cho thay đổi query/Keep. Không restore hoặc ApplyDev. Các fixture Test append riêng, không đổi nguồnDev hoặc xóa/reset SQL. Seal targeted/evidence riêng trongrun; các incidentrawV04/D15/v05 vàseal3D giữ nguyên, không tái tạo giả.

## Lệnh và checkpoint tiếp tục

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3e.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/diagnose-phase3e.ps1 -ReportRoot .local/evidence/phase3e/DIAGNOSTIC_UNIQUE_RUN_ID
```

Lệnh batch đã chạy vàexit/raw ở `preflight-final.log`, `commit-real.log`, `repeat-final.log`, `verify-post-final-code.log`. Helper `.local/phase3e-batch.ps1 -Action Preflight|Reconcile|Commit|Verify -Label NEW_UNIQUE_NAME -NoBuild` dùng exactplan/checkpoint/proof3C, chỉSQLTest; không dùng lại label cũ. Scriptpublic `scripts/phase3e.ps1` nhận tham số explicit, Test-only; không có Devwrite flag. Previewcũ giữ nguyên, không có preview3E thường trực.

Nếu timeout tái diễn:chỉ1invocationTest có diagnostic, bắt đúngsession từSQL_START/CommandId/operationId; lấyrequests/sessions/waitingtasks/tranlocks đúngDB/session với observer2s và tối đa vài mẫu; phân biệt connect/role-activation/guard/proof/commit/readback và trước/sauCOMMIT. Ghi thời điểm resource aggregates/plan fingerprint so với lượt3E. Nếu quyền thiếu thì lưublocked, không cấp quyền/cấu hình. Nếucommitkhôngchắc chắn chạyReconcile trước, không ép chạyCommit. Chỉ xem xét migration/index riêng khi có actualplan/reads cho đúng fingerprint và backupTest; không tự ápDev hoặc thaySQLmemory/timeout.

P0 dữ liệu Test đã mở chặn; **điều tra nguyên nhân gốc vẫn còn**. Chưa mở rộng228URL/crawl/NukeVietSQL trong lượt3E vì không muốn kéo dài P0 và chưa có approval/mapping. PDF3233/Video40/41, Dev53NotScanned, authority signer/quyềnDev, scannerProduction/MFA/SMTP/restore/UAT/3Word/TLS/cutover vẫn riêng. Website CHƯA PRODUCTION READY.
