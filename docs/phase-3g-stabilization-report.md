# Phase 3G Stabilization — 21/10/2026

**Packet READY_FOR_APPROVAL tại thời điểm kiểm tra; chưa ký approval, chưa Apply Dev. SQL ROOT CAUSE UNKNOWN / MONITORING. CHƯA PRODUCTION READY.**

Run `phase3g-stabilize-20261021T021233Z-20e31fbb`, pointer `.local/phase3g-stabilization-current.json`. Yêu cầu đầy đủ và SHA được lưu trong run. Code/build/evidence các lượt cũ giữ nguyên, không phục dựng incident artifact.

## Tám kết quả bàn giao

|Câu hỏi|Kết quả hiện tại|
|---|---|
|1. Nguyên nhân timeout?|**Chưa xác định.** Không tái hiện trong các scoped SQL tests, Test/Dev preview/preflight và receipt/readback hiện tại. Không gọi ROOT CAUSE FIXED.|
|2. Các test FAIL/NOT RUN cũ?|Lượt cuối `tracking-provenance-final`: **4/4 PASS,0 FAIL,0 NOT RUN**, codeDrift0, build0warning/error. Bao gồm resume/idempotency, giữ tracking cũ/comparison revision, rollback trước commit và negative media/Production/Dev authorization. Không cộng các lần chạy thành full suite.|
|3. Sửa SQL/index?|**Không.** Không đổi statement nghiệp vụ, index, CommandTimeout, pooling, SQL config, quyền hoặc NOLOCK. Chỉ bổ sung instrumentation redacted và kiểm thử lineage tracking.|
|4. Scanner policy?|**Giữ strict policy, phương án B.** Exact bytes/hash + SQL receipt + đúng job/allowlist/Test scope + native engine/definitions/current identity + TTL24h; signatures phải còn recent72h. Không có phê duyệt policy A, không thêm version tolerance.|
|5. Fresh proof?|**Có:**60 public Test copies Clean,0 Infected/ScanFailed; native control Clean/EICAR-Infected/cancelled-health-ScanFailed đạt; validate60PASS, resume0newreceipt. Chỉ7 ảnh mới overlay vào Dev; originalDev53 không scan/promote/Stage.|
|6. Batch/hash mới?|Batch **`26afcb7a-b3c1-4708-9009-c3d296e0afdc`**; canonical plan/manifest SHA **`a6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f`**.|
|7. Packet READY?|**READY_FOR_APPROVAL**, không READY_EXECUTE/APPLIED. Test preflight37eligible/0receipt/0errors; Dev preflight10eligible/0receipt/0errors. Cần revalidate tại lúc vận hành, proof/approval vẫn có thể bị guard chặn khi điều kiện đổi.|
|8. Cần duyệt gì?|Chủ dự án xác nhận **exact batch/hash/phạm vi mới** trước ký action Apply. Không dùng xác nhận “sửa tracking” làm approval. Sau đó cần signed approval đúng SID/cert/action, backup Dev mới và backup SQLTest proof receipts/dependency; preflight/Apply/verify/smoke theo quyền. Không phê duyệt Production hoặc policy A trong lượt này.|

## SQL: bằng chứng và giới hạn

`MigrationDiagnostics` ghi connection/clientConnectionId/SPID, fingerprint, timeout, transaction, thời gian và SqlException number. Guard và role activation cũng có span kết nối/câu lệnh; không log connection string, password, SQL parameter values hay body. Test diagnostics vẫn chỉ Development/SQLTest; không bật trace runtime Production/Dev. Batch operation có operation ID/stage/batch ID khi batch đã được tạo; trước preview chưa có batch ID, không điền một GUID giả.

Lượt cuối4SQLgroups:230EF reader commands, max24,85ms; guard connection max152,35ms; direct guard/role commands max40,93ms;0SQL_FAIL/CONNECTION_FAIL. Đây là thời gian local đo trong bộ test giới hạn, không benchmark Production. `sql-diagnostic-summary.json` và trace gốc có toàn bộ fingerprint/session/transaction.

|Bước thực|Thời gian|Kết quả|
|---|---:|---|
|Auxiliary Test connect|74,24ms|PASS; provider System.Data.SqlClient, tách khỏi EF/Microsoft.Data.SqlClient trace|
|Receipt query theo đúng recovery job|32,61ms|60 rows returned; không nhầm với số rows read|
|Readback3 SourceRecords thật đã có Test|13,08ms|PASS;Article3666/3670/3673,nguồn qldd,không fixture;0 import mới|
|Test preview|23.295ms|PASS;37eligible/3Blocked; không commit batch Test mới|
|Test preflight|3.787ms|PASS;ABSENT_CONFIRMED37/0receipt/0errors|
|Dev preview|12.534ms|PASS;3new/4revision/7JPEG/6newalias|
|Dev preflight|2.408ms|PASS;ABSENT_CONFIRMED10/0receipt/0errors|

Preview timings là toàn quá trình parser/hash/SQL, không phải một SQL statement. `sql-pipeline-stages.json` tách checkpoint/recovery/reconcile; `sql-stage-probes.json` ghi fingerprint/session/transaction/elapsed riêng. Receipt actual execution plan `receipt-actual-plan.xml` có scan operator **24.987 ActualRowsRead/1.228 ActualLogicalReads**, trả60rows. Chưa có bằng chứng scan này gây timeout30s; không thêm index theo phỏng đoán. Giữ tối ưu2EFqueries cho recovery-proof validation từ3E; resume worker có kiểm receipt riêng, không khẳng định mọi đường chạy đều2queries.

Observer chỉ lấy bounded snapshots của session Test tương ứng, không thấy blocking/transaction bất thường trong mẫu; không thể loại trừ contention/worker/resource pressure tại thời điểm lịch sử. Không KILL session, dừng preview cũ, sửa service/memory hoặc query database ngoài dự án. Theo dõi tiếp bằng trace mới nếu timeout tái diễn, reconcile trước retry.

## Proof và phương án B

Code chính sách `MediaRecovery`/`RecoveryEvidence`/`DefenderInquiryScanner` **không đổi**. Phân biệt SHA bytes của resource (`Sha256`), SHA file proof JSON (`ProofSha256`), SHA output log scanner (`Result.OutputSha256`) và SHA proof-set bind vào plan; chúng không thay thế nhau. Engine/definitions/time/result được giữ riêng trong proof/result dù current identity hiện serialize chung thành chuỗi.

- Engine **1.1.26090.9**; definitions **1.461.118.0**, cập nhật `2026-10-20T07:02:06Z` lúc kiểm tra.
- Job mới **2fb80968-969a-4223-bd13-c33295b2badb**, đã technical review/register theo yêu cầu Stabilization phương án B: đúng60hash public Test trước đó,53protectedTestcopies+7ảnh mới. Quyền review này không phải quyền ký/Apply Dev. `reviewed-recovery-job.json` bind SHA yêu cầu thực và inventory.
- Job cũ301f... giữ **180 receipts**, không nới3/resource hoặc che failed attempt. Job mới **60 receipts/1resource**, validate/resume không thêm receipt.
- Proof-set `.local/evidence/phase3c/phase3g-stabilize-20261021T021233Z-20e31fbb/scan/proof-set.json`, SHA **`3ebcf7e9d9b93648147ec4874cc7bbf464162d6e892474ea990311a817498f68`**.
- Hạn sớm nhất **22/10/2026 09:18:45 VN** (`2026-10-22T02:18:45.3921363Z`), còn bắt buộc khớp current engine/definitions/policy lúc Apply. TTL không cho phép dùng definitions cũ.
- Control thật: JPEG public → Clean; ZIP EICAR68byte chuẩn → Infected; hủy health subprocess thực → ScanFailed. Không mô phỏng scanner hoặc coi ScanFailed là Clean; cancellation không chứng minh một outage tự nhiên. Không thay AV/cloud/firewall/service policy.

Phương án B đã chạy bằng recovery-register/scan/validate hiện hành rồi fresh preview/hash, không thêm bypass. Khi chuẩn bị Apply, scan/resume cùng job với report mới theo giới hạn, validate ngay trước review/signing/preflight. Nếu definitions đổi sau khi duyệt làm proof invalid: dừng, refresh và tạo plan/hash/approval mới; không âm thầm thay proof-set dưới plan đã ký. Strict B giảm khoảng cách scan–thực thi, **không bảo đảm packet bất biến qua mọi lần cập nhật definitions**. A cần quyết định ATTT riêng, chưa triển khai.

## Exact packet và provenance

Canonical manifest: `.local/evidence/phase3f/phase3g-stabilize-20261021T021233Z-20e31fbb/devpreview/plan.json`. `batch-review.json` kèm metadata nguồn,7SHA ảnh/proof, mapping, blocked/Keep/comparison và compiled tool SHA. Tổng delta vẫn **3 Contents+3SourceRecords/4 revisions/7JPEG(2.048.267byte)/6aliases**;0đổi public/editorial/Identity/original53. Các article mới3666/3670/3673; revision thứ4 là comparison của DraftDocument:5, không đổi public.6Keep/30Blocked vẫn trong plan; không coi30 là bài mới, không import cả Test database.

Tracking mới **Phase3BCheckpointReplay**, checkpoint bind inventoryId/SHA; per-item `SourceRecords.Source/Kind/SourceId` và `Phase3B.Import` receipt bind source identity/plan hash, ImportRun trỏ CrawlRun inventory. SQL tests cuối kiểm đúng chuỗi này và giữ existing tracking. Real packet dùng sealed QLDD checkpoint/network captures và byte cache thật; fixture kỹ thuật có title/Origin/Evidence riêng, không vào packet hoặc số liệu nguồn. Không chỉnh hàng CrawlRun cũ hay nội dung nguồn để demo.

Đích readonly mới: **MinhAnh\SQLEXPRESS/LandPortal_Dev**, expectedDB đúng, GUID `b499750c-a0c8-4003-b7ee-dce4938f78dc`; registeredNameTHANGPD vẫn được ghi đúng cùng xác nhận đổi tên trước đây. Dev/Test cùng10migration,latest`20261015091551_V05EditorialDocuments`. Không migration. Certificate94797... hiện CodeSigning/chain/CurrentUserRoot/NonExportable/còn hạn19/11; native Authenticode trên fixture cũ Valid chỉ chứng minh chữ ký/cert, metadata fixture đã hết hạn **không** dùng làm authorization. Application loader thật từ build mới đọc đúng process-only Dev thumbpin. Manifest guards saiPlanSHA/saiManifestSHA/Testtarget đều reject trước ký/SQL.

Unsigned review `.local/signing/reviews/phase3g-stabilize-20261021T021233Z-20e31fbb/review.json`, SHA **`9c4111468bfb36f3f691179e1954679385ee395c556592b59c8b3f0536fa23e4`**, PENDING_USER_APPROVAL. [Runbook/lệnh exact packet](phase-3g-stabilization-runbook.md).

## Bảo toàn, thay đổi và giới hạn

Dev before/after **0diff/17groups**:37Contents/37SourceRecords/47revisions/74aliases/**53NotScanned(41ảnh12PDF)**/70capture68SQLsnapshot/8ngoạilệ/2videometadata0file. Identity/.env giữ nguyên;0signedapproval/0Apply. Backup Test21/10 đã verified2668files/0diff, được revalidate SHA/age và gate tool trước test; dùng lại trong cửa sổ<=12h. Backup này chưa chứa proof receipts mới hoặc fixture của lượt này; cần backup SQLTest/dependencies mới trước DevApply. `dependency-allowlist.json`127files chỉ **PREPARED_NOT_BACKED_UP**. Không backup/restore Dev, crawl, email, deploy/DNS/push hoặc nâng quyền.

Source changes chỉ instrumentation ở `Configuration.cs`, `MigrationDiagnostics.cs`, `RuntimeRoleInterceptor.cs`; provenance asserts `BatchTrackingTests.cs`; focused runner chọn isolated stabilization build và bật redacted Test trace. Không sửa importer/scanner/approval logic sau tracking patch. App build cuối0warning/error,4/4SQLPASS. HTTP/UI/fullregression/actual batch signature/DevApply/rollbackNOTRUN.

Lỗi chuẩn bị phụ được giữ rõ: harness validate đầu load SqlClient reference stub, một lần metadata NuGetCPM không hợp lệ; đã sửa **harness ignored** dùng cùngSqlClient6.1.6 runtime, validate60/resumePASS. Build phụ có4NuGet warning NU1902/NU1903 cho transitivePkcs9.0.11, không đổi dependencies app hoặc che warning. Auxiliary plan-reader nhận diện kiểu resultset chưa đúng, query readback đầu nhầm Title ởContents; sửa query helper joinRevisions, metadata/data SQL không đổi. Các lỗi render-console/metadata không được tính là SQL timeout hoặc PASS; artifact cuối và raw lỗi tách biệt. Không cộng số kiểm tra riêng thành full suite. Incident artifact lịch sử giữPARTIAL.
