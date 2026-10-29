# Phase3H-Fix — 21/10/2026

**Workflow kiểm chứng hiện tại PASS LOCAL; điều tra nguyên nhân SQL vẫn PARTIAL / UNKNOWN / MONITORING.** 13 packet cho210 nội dung READY_FOR_APPROVAL; **chưa ký, chưa Apply Dev, chưa READY_FOR_EXECUTION khi thiếu approval/backup/fresh guards. CHƯA PRODUCTION READY.**

Run `phase3h-fix-20261021T050440Z-e5776be2`, pointer `.local/phase3h-fix-current.json`; bằng chứng `.local/evidence/phase3f/phase3h-fix-20261021T050440Z-e5776be2`. [Lệnh ký/backup/Apply/verify/rollback](phase-3h-fix-runbook.md), [13 packet](phase-3h-batches.md).

## Tám câu trả lời

|Câu hỏi|Kết quả có bằng chứng|
|---|---|
|1. Nguyên nhân timeout?|**Chưa xác định.** Một lần tái hiện Test đạt3.089ms, không timeout; không observer thì preflight đạt2.846ms. Các bước replay/Dev tiếp theo cũng đạt. Không nói đã FIX ROOT CAUSE.|
|2. Đã sửa query/transaction nào?|**Không.** Chỉ thêm công cụ đo/observer/actual plan và test guard. Query hiện chậm nhất25,7ms; chưa đủ bằng chứng để rewrite/index hoặc đổi lifetime/config. Không migration, tăng timeout, đổi pooling/role hoặc giữ observer làm workaround.|
|3. Replay Test?|**PASS:20Keep**,20/20COMMITTED_VERIFIED,0missing/0errors; verify0mismatch; repeat20AlreadyCommitted; DataSHA không đổi. Đây là20 bài Test đã có, **0 bài/revision/asset nguồn mới**. Không biến Keep replay thành kiểm thử tạo mới20bài.|
|4.15 nội dung còn chờ?|**15/15 đạt**,0BLOCKING/0NEEDS_REVIEW; fresh Test preflight và Dev preview/preflight đều đạt.|
|5. Tổng đủ điều kiện?|**210 nội dung/210revision/524asset/420alias** trong13packet để duyệt. B01/b13 fresh; b02–b12 giữ packet/SHA cũ, được hỗ trợ bởi Dev17groups không đổi và boundproof/currentengine/TTL không đổi; không tuyên bố rerun11 preview đó.|
|6.52 dòng còn chặn?|**Vẫn52**, không giao với15b13;22staging+30Devprotected. Không giải quyết thêm bằng cách bỏ file/scanner/alias checks.|
|7. B01 READY?|**READY_FOR_APPROVAL.** Replay và fresh preflight đạt; thực thi vẫn gated exact owner approval, chữ ký, fresh Dev+Test backup và guard tại thời điểm Apply. Không có authority Apply từ prompt này.|
|8. Exact packet mới?|BatchId **`aa52120d-a6b9-41b9-b4b5-22ced4330113`**, manifestSHA256 **`41c7bf061b472ec0d8aa5129eaa9384bb27e1966baa977fc74c82e78f63d6dfa`**, đích MinhAnh\SQLEXPRESS/LandPortal_Dev;20content/20SourceRecords/20revision/60asset/40alias.|

Review b01 `.local/signing/reviews/phase3h-fix-20261021T050440Z-e5776be2/b01.json`, SHA `2f6d732ee6af59b9028728ecf9812eabe79026acbab28d56fcc8e9e854a2f793`, `approvedBy=null/signatureCreated=false`. Old b01 `8167414c.../0e6f6a...` giữ lịch sử, không thay packet mới để ký. B13 có Dev BatchId đầu tiên `becfc9bf-4f9e-420b-906b-3adcb613d908`, SHA `84461b83958f22f9da3e07093d6cd2f56f856b85fbfdda5a1348d4517bec49ae`,15/15/15/30.

## Chẩn đoán đúng đường preflight

Trước chạy đã kiểm hash selected code/CLI/.env/prior seal/plan; live baseline Dev so với checkpoint cũ0diff/17groups, IdentityPASS. Dùng lại CLI cuối3H SHA `35624589caacf0f26737e581175355d7fd5108de24076652b157f96c1e7f5f09`; .NET source không thay nên **không build lại .NET hoặc chạy fullsuite**.

`scripts/diagnose-phase3h-fix.ps1` chạy đúng b01 Test plan `5e75a2c6-166a-495f-8852-58c0484e8314`/SHA `fd88ea7ec2d2ac92ca890c25d8db844ab5408e908bcf407051d7faf9f238031c`, operation `e7024db3-5859-4cf9-aa2d-c1f52bfedce0`. Observer chỉ session host_process_id của CLI33572 trong Test; connection/command timeout không tăng.6snapshot/0observererror, thấy session52/blocking0/open transaction0 tại mẫu có request; mẫu nhanh khác không bắt được request, **không kết luận không từng có blocking**.

EF trace đọc84commands:2recovery-proof+82reconcile,86runtime-roleactivations; max thời gian mởreader25,7169ms. Command từng timeout trong3H fingerprint `089e3a4e68313e7e8f8e3f485cfda69faab9024ccae3d7d5885d0d8a535fa0f3` hiện25,7ms, clientConnectionId `f5dceefa-d0c7-46b9-9004-69984a9f1953`/session52. Phase durations:checkpoint113,6ms,proof1.834,5ms,reconcile480,3ms. Trace có operation/stage/commandID/fingerprint/clientConnectionId/SPID/transaction/time; không log SQL secret/connection string/parameter values/body.

Thời gian SQL_READER là mởreader, không phải đã consume hết rows. Để phân biệt physical reads với rows trả về, một bounded equivalent audit predicate probe đã lưu **hai actual XML plans** ở `audit-actual-plan-captured`:

|Predicate tương đương|Rows returned|ActualRowsRead tại AuditLogs scan|Logical reads|Scan elapsed|
|---|---:|---:|---:|---:|
|Inventory binding exactactor/SHA|1|24.988|1.228|8ms|
|Proof receipt theo đúngjob,TOP181|60|25.564|1.289|3ms|

Toàn probe178ms gồm connection/XML/IO; đây là query parameterized trên operator metadata connection, **không byte-identical EF SQL, không benchmark Production hoặc bằng chứng nguyên nhân timeout**. Không cộng counters của parent operators vào scan. Index hiện có chỉ PK_AuditLogs và ContentId/CreatedUtc; thiếuActor index làm scan nhưng scan hiện chỉ vài ms, không thêm index theo phỏng đoán. Lần capture đầu gặp khác biệt kiểu nvarchar Showplan nên không lưu XML; summaryPASS cũ **không được dùng làm bằng chứng capture**. Lần sau sửa nhận diện cột, bắt buộc đủ2plan, có XML thực và hash. Không tạo plan giả.

Scope dữ liệu: Test có4.785Contents/517SourceRecords/6.821Revisions trong metadata table tổng, không phải số nguồn thật; có dữ liệu ngoài cohort/fixture kỹ thuật. Preflight lọc đúng source/kind/id,stamp theo ContentId và AuditLogs theo BatchId/actor; chỉ materialize rows trong batch/proof. FullAudit scan đọc cả lịch sử/fixture về mặt vật lý nhưng trả60proof của job, không đưa cả bảng Contents/Assets vào bộ nhớ để chuẩn bị packet. Readback thực chỉ20source/20revision/60media/40alias.

### Resource, connection và giới hạn

Test `AUTO_CLOSE=ON`; snapshot SQL `process_physical_memory_low=true`, SQLphysical~176.616KB; OSavailable3.016.216KB/lowmemorysignalfalse. Đây là dấu hiệu cần theo dõi, **chưa phải causal proof**. Observer giữ Test mở trong lần tái hiện nên không loại trừ cold/AUTO_CLOSE. Sau observer dispose, preflight và replay Test, Dev flows vẫn đạt. Không đổi AUTO_CLOSE, global SQLmemory, service, firewall/AV hoặc KILL session khác.

DbContext main/overlay đều dispose ở phạm vi CLI; runtime poolingfalse được giữ để tránh đưa approle session vào operator pool. Nhiều activation là overhead đo được, chưa chứng minh gây timeout. Proof validation/hash/native probe không có transaction; Commit chuẩn bị/hash trước serializable transaction rồi audit/commit từngitem. Snapshot không thấy transaction mở của diagnostic CLI. Query timeout30s và connection default giữ nguyên; CLI preflight hiện không truyền Console cancellation token tới mọi query, được giới hạn bằng timeout. Diagnostic có deadline90s cho **child readonly của chính nó**, không kích hoạt trong run; không hủy session ngoài lượt.

## Replay Test và fresh packet thực tế

|Bước thực, không observer giữ connection|Kết quả / thời gian toàn CLI|
|---|---|
|Test preflight b01|ABSENT_CONFIRMED20/0receipt/0errors;2.846ms|
|SafeApply Test exact plan|20Keep,0newrealcontent/revision/asset;11.414ms|
|Reconcile|COMMITTED_VERIFIED20/20/0missing/0errors;13.050ms|
|Verify/readback|PASS0errors;12.493ms|
|Idempotent repeat|20AlreadyCommitted,0newimportreceipt;15.281ms|
|Verify/readback sau repeat|PASS0errors;13.006ms; DataSHA giống|
|B13Testpreflight|15eligible/0errors;2.921ms|
|Fresh B01Devpreview/preflight|20CreateNeedsApproval/0Blocked;11.940ms/2.243ms|
|Fresh B13Devpreview/preflight|15CreateNeedsApproval/0Blocked;5.771ms/2.134ms|

DataSHA business readback trước/sau repeat `691f5ce8263a43791f7bf458f59b9dea17375683a38362595760b3b051585ebf`. Keep chỉ tạo tracking/audit và warning theo revision, không sửa source/public/editorial/media. Legacy ImportRuns.StatusPartial không được tự sửa để báo thành công; receipts/reconcile/readback xác nhận20commit. Không có NeedsReconciliation/COMMIT uncertainty trong run này; không cần rollback. Tạo mới/rollback publications không rerun vì importer không thay và các kết quả liên quan trước vẫn được giữ đúng giới hạn, không cộng thành suite mới.

Trước Test replay tái kiểm đúng bộ backup Test3H `h3e716c4`: SQLbak SHA `80247215499be832294a521e45e7d610c9a06597e68f954e037df479bcea1389`,197.880.320byte,<12h; safeapply guard đọc SQLHEADER đúngTest. Snapshot206file/60.181.961byte đã verify nguồn và bản sao0diff. CHECKSUM/VERIFYONLY là kết quả backup3H được tái dùng, không tuyên bố chạy restore drill hoặc tạo backupDev mới. User cho phép tái dùng phần đã đạt/còn hợp lệ; không backup lại vô ích.

## Scanner, signer và bảo toàn

Không native scan mới, không đổi policy. Strict SQL validation thực chạy trên b01(60SHA) vàb13(15SHA); các fileproof còn lại kiểm SHA/currentengine/TTL offline và dùng receipt validation3H không thay, **không gọi đó là full524SQLproof suite mới**. Cả524boundClean files không đổi, engine1.1.26090.9/definitions1.461.118.0 còn hiện hành; earliest expiry22/10/2026 11:05:12VN, driftdefinitions vẫn invalid ngay. OriginalDev53 không scan/promote/Stage.

Signer Inspect chỉ đọc: CurrentUser94797BD79E275B09DEF613036BC00DF4EA91345F, chain/EKU/hạn/nonexportable hợp lệ; native public technical fixture AuthenticodeValid. PowerShellSignatureBridge và signer policy không sửa; không tạo certificate/trust/approval mới hoặc tự ký thay user. Old8/8signature/3H6/6+4/4 là lịch sử, không sao chép thành kết quả lượt này.

Live Dev trước/sau **0diff/17groups**:40content/40source/51revision/80alias/60media(48ảnh12PDF),53originalNotScanned+7Clean;70capture/68SQLsnapshot/8exceptions/2videometadata/0file. Identity/envdigest giống. Original37+3bài3G và mọi published/editor payload/file giữ nguyên.0DevApply/0Devdatawrite,0newTestsource. Không deploy/email/DNS/Gitpush/crawl/reset/restoređè/grants/.envchange.

## File và checkpoint tiếp theo

File mới: `scripts/diagnose-phase3h-fix.ps1`, `inspect-phase3h-preflight.ps1`, `test-phase3h-fix.ps1`; báo cáo/runbook và state/acceptance/bảngbatch cập nhật. Không thay C# importer/scanner/bridge/transaction/schema/index/migration. Hai diagnostic script parser và4negative existing/out-of-scope guard checks cuối đều PASS,0SQL, lưu `script-checks/checks.json`; không gọi6checks này là full regression. HTTP/UI/fullsuite/SQLrestore/DevApply NOTRUN.

`packets.json`/`final-result.json` là checkpoint máy đọc; `b01-backup-dependencies.json` có209 exact scoped files **PREPARED_NOT_BACKED_UP**; backup future phải thêm signed approval/receipt thực và toànDev media/snapshots/keys+SQLDev/Test theo helper. Không dùng backupDev37 trước3G làm backup trước nhập20bài hiện tại.

**Bước tiếp theo:** chủ dự án xác nhận exact new BatchId/SHA/delta/target trước ký; sau đó fresh backup/proof/stamp/target/preflight, Apply/verify/repeat/smoke theo [runbook](phase-3h-fix-runbook.md). Nếu timeout tái diễn: dừng đúng bước, ghi wait/connection/fingerprint, reconcile trước retry; không chuyển trạng thái root cause thành FIXED chỉ vì lượt này PASS. Historical artifact incidents, Production scanner/MFA/restore/UAT/cutover còn riêng; website **CHƯA PRODUCTION READY**.
