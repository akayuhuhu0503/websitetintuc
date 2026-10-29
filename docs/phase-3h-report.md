# Phase 3H — verified import packets, 21/10/2026

**PARTIAL. 12 packet READY_FOR_APPROVAL để xem xét; thực thi ON HOLD vì SQL timeout. Không ký hoặc Apply Dev mở rộng. CHƯA PRODUCTION READY.**

Run `phase3h-20261021T035215Z-47db620c`; pointer `.local/phase3h-current.json`. Bằng chứng ở `.local/evidence/phase3f/phase3h-20261021T035215Z-47db620c`; job scanner ở namespace Phase3C cùng run ID. [Danh sách batch/manifest](phase-3h-batches.md), [runbook](phase-3h-runbook.md), [mapping NukeViet](phase-3h-nukeviet-mapping.md).

## Kết quả cần duyệt

1. **210/210 nội dung Phase3F chưa có trong Dev40**, giao với3 bài Phase3G bằng0. Cohort lấy từ Create đã có receipt SQL của hai manifest Phase3F:169+41; không lấy4.773 hàng Test hoặc cộng trừ số lịch sử. SourceIdentity, fingerprint/revision đang public Test, capture Network, URL và SHA được đối chiếu; không fixture.
2. **195 nội dung trong12 packet đã qua preview/preflight Test và Dev**: dự kiến195content/195SourceRecords/195revision/509asset/390alias. Còn15 nội dung b13 đã có TestKeep/preflight và proof nhưng **Dev preview bị Connection Timeout**, chưa có Dev plan/BatchId/manifest. Không gọi toàn210 READY_TO_APPLY.
3. **524/524 tài nguyên có proof mới Clean**,0 thiếu proof trong cohort,0Infected/0ScanFailed;625.746.619 byte,309ảnh(181JPEG/127PNG/1GIF)+215PDF. Native Microsoft Defender engine1.1.26090.9,definitions1.461.118.0; trạng thái cuối cùng vẫn cùng engine/definitions. `fresh-proof-summary.json` lưu TTL sớm nhất **22/10/2026 11:05:12 VN**; definitions đổi làm proof mất hiệu lực sớm hơn. Không quét hoặc promote53 bản gốc Dev. File media SQLTest liên quan582 SHA đã đọc thực và kiểm hash/kích thước,0mismatch;582 là tập có cả nguồn cũ, **không phải582 file vừa quét**.
4. Trong55 dòng lịch sử,3 Article3589/3607/3684 được giải quyết trong phạm vi receipt thật; loại các ứng viên typed staging chưa commit khỏi packet, không merge/tự chọn theo title. **52 dòng còn giữ chặn/ngoài phạm vi**:22 staging chưa commit và30 danh tính Dev được bảo vệ. Nhóm nguyên nhân giao nhau:18 thiếu/lỗi file,31proof,8alias,2video metadata; không cộng thành59 bài. Xem `blockers-by-identity.csv`/`historical-55-blockers.json`. Ngoài ra15 nội dung b13 hiện chờ SQL; không nhập nhằng với55 dòng lịch sử.
5. Chia13 nhóm trọn nội dung và tài nguyên thiết yếu,≤20content/≤60SHA/job;524 SHA không dùng chéo batch và420 alias không trùng trong cohort. **12 có Dev BatchId/SHA/review chưa ký; b13 chỉ là nhóm chờ preview**, chưa phải manifest Dev hợp lệ. Bảng chi tiết ở [phase-3h-batches.md](phase-3h-batches.md).
6. B01 **READY_FOR_APPROVAL về packet**, chưa READY_TO_APPLY: BatchId `8167414c-a130-4a34-b626-722a7f7a9f06`; manifestSHA256 `0e6f6a334b6083bc0bb1fa3ea6f87589bdf31454bc33be552c9a13137564957e`; đích MinhAnh\SQLEXPRESS/LandPortal_Dev; delta tối đa20content/20revision/60asset/40alias. Review `.local/signing/reviews/phase3h-20261021T035215Z-47db620c/b01.json`, SHA `e7b40f3439711f32d4a63c5ed6e850a7ed0c6b134cdb841285a7fdee7286baa9`; `approvedBy=null`, `signatureCreated=false`. SQL timeout mới khiến mọi thực thi phải qua fresh guard trước ký/Apply.
7. Cần chủ dự án xác nhận **từng BatchId+manifestSHA+delta+target** trước ký riêng. Approval Phase3G không được tái sử dụng. Sau xác nhận còn fresh proof/target/stamps/preflight, backup Dev hiện tại và updated SQLTest receipt/dependencies đầy đủ. Không tự ký thêm, cấp quyền, sửa `.env` hoặc trust. Certificate CurrentUser94797BD79E275B09DEF613036BC00DF4EA91345F vẫn có chain/EKU/hạn hợp lệ; public technical fixture Authenticode vẫn Valid. Không tạo/trust lại certificate.

## Phần mềm thay đổi và kiểm thử thật

Không migration/DDL, không đổi importer, scanner engine/policy, CMS, video hoặc signature bridge. Mở rộng **job manifest recovery có giới hạn**, do factory legacy chỉ nhận53+7 và không dùng được với cohort210.

|File|Thay đổi|
|---|---|
|`src/MigrationCli/MediaRecovery.PublicBatch.cs`|Factory scope public Test: trọn1–20 item giữ nguyên provenance parent, nguồn đã có receipt/revision thật;≤60 uniqueSHA, không protected Dev/quarantine; kiểm byte/format/SQL revision links; complete native Clean bắt buộc|
|`src/MigrationCli/MediaRecovery.cs`|Scope/parent binding tùy chọn tương thích inventory legacy; giới hạn/retry legacy giữ nguyên; set public phải đầy đủ và fail-closed nếu khôngClean, không rơi về nhãnClean lịch sử|
|`src/MigrationCli/RecoveryCommand.cs`|`recovery-public-inventory`, Test registration qua pipeline audit hiện có|
|`scripts/phase3h.ps1`|CLI hash-pin, mutex, explicit profile; Dev chỉ readonly preview/preflight/reconcile; không action ký/Apply; hỗ trợ CLI absolute/relative|
|`tests/LandPortal.ScaleTests/{Program.cs,ScaleSqlChecks.cs,LandPortal.ScaleTests.csproj,packages.lock.json}`|Focused scope/provenance/negative proofs và SQLTest thật; không đưa fixture vào cohort|

Final CLI `.local/build/phase3h/final/bin/MigrationCli/debug/MigrationCli.dll`, SHA `35624589caacf0f26737e581175355d7fd5108de24076652b157f96c1e7f5f09`; scan build ban đầu và mọi artifact cũ giữ nguyên. Proof đã tạo trước hardening nonClean vẫn được final code validate lại ở preview/preflight; không quét lặp lại để tăng số.

|Kiểm tra sau thay đổi tương ứng cuối|Kết quả / bằng chứng|
|---|---|
|Build cuối|PASS0warning/0error; `final-build.log`|
|Focused C# scope/projection|**6/6PASS**, `focused-final-tests/focused.json`; trước đó5/5 là lần cũ riêng, không cộng|
|SQL proof trên final code|**4/4PASS**, `sql-proof-focused/sql-focused.json`: native60 thực/currentengine/durable receipts, incomplete/duplicate/wrongbinding/unregistered receipt/Dev scope denied;0newscan|
|Wrapper sau sửa cuối|ParserPASS,3/3negativeguardsPASS,0SQL; `wrapper-checks.json`; positive SQL wrapper NOTRUN do timeout, vòng thực chạy CLI cùngbuild trực tiếp|
|Native quét/resume|13job/524Clean; b01stop2 rồiresume58, sau đóreuse60/0newscan; không tăng3receipt/resource hoặc180/job|
|Preview/preflight|Test13/13 Keep/preflightPASS; Dev12/12 plan/preflightPASS; b13DevpreviewFAIL connection và DevpreflightNOTRUN|
|Test replay/readback mới|**BLOCKED** tại fresh `preflight.recovery-proof`, trước safe-apply. Apply/verify/repeat NOTRUN; không ghi20Keep receipt hoặc tạo thêm source để demo|
|SQL/media/source integrity|`delta-audit.json`0problem/collision; `test-materialized-media-readback.json`582file0hashdiff; Dev17groups0diff|
|HTTP/UI/full regression|NOTRUN; không thay media ACL/web hoặc công bố Dev mới; không cộng6+4+wrapper/scans/preflight thành fullsuite|
|Rollback|Không cần vì0Apply mở rộng; expanded Dev rollback NOTRUN. Engine unchanged, kế hoạch withdraw-publication/retain-source theo runbook; không tuyên bố restore drill|

## Backup Test và timeout

Trước scan đã revalidate backup Test cũ SHA/VERIFYONLY/CHECKSUM, đúng target và còn<12h. Trước kiểm tra Keep đã tạo **backup Test mới** chứa recovery receipts: `.local/backups/LandPortal_Test-20261021T015310Z/h3e716c4/t/LandPortal_Test.bak`,197.880.320byte, SHA `80247215499be832294a521e45e7d610c9a06597e68f954e037df479bcea1389`; CHECKSUM/VERIFYONLY đạt. Snapshot b01 raw/cache/media/proof/metadata **206file** ở `h3e716c4/f`, copy/hash/readbackPASS (`test-keep-backup-files.json`). Không restore SQL. Bộ này chỉ phạm vi Test Keep/audit, **không phải backup đủ cho Apply Dev210**.

Ba thao tác readonly bị timeout, không phải ba commit:

- B13Devpreview: post-login14011ms, exit1 lúc04:25:14Z, chưa có plan. Raw `b13-dev-preview.log`. Dev instrumentation hiện Test-only; không có command fingerprint/SPID cho connection failure, không tự dựng.
- Test fresh reconcile: `preflight.recovery-proof`, SqlException-2,35.025ms; transactionNONE, SPID68, clientConnectionId `b17522e4-ab10-4b1e-bc33-89c0fbd2249d`, fingerprint `089e3a4e68313e7e8f8e3f485cfda69faab9024ccae3d7d5885d0d8a535fa0f3`; raw log/trace và `sql-failures-extract.json`. SQL_FAIL và outer FAIL là **cùng một command**, không đếm hai lỗi.
- Controlled aggregate metadata read thêm trên Dev cũng ExecutionTimeout; không có local raw file/command trace, quan sát tool session62060 được ghi **summary**, không giả lập raw. Không gọi lần đó reconcilePASS.

Đã dừng từng batch/bước lỗi, không retry mù, không tăng timeout/SQL config, không stress loop, không Apply trước reconcile. `sql-incident-summary.json` giữ chi tiết và giới hạn trace. **ROOT CAUSE UNKNOWN/MONITORING**, không tuyên bố đã sửa timeout. Kiểm tra Keep chưa tới Apply; chưa có COMMIT-sent hoặc transaction cần rollback từ thao tác này. Supplemental SQL receipt-count reconciliation bị chặn, không báoPASS.

Auxiliary lỗi harness/đường dẫn lưu riêng: identity helper dùng nhầm propertygroups, đã so actualidentityDigest offline; wrapper absolutepath sửa và negativechecks cuối đạt; dependency helper enum mapping ban đầu thiếu raw nonArticle, **chỉ dùng `apply-backup-dependencies-complete-v2.json`**,1850file/~1.28GB explicitallowlist,210raw/524cache/524quarantine/proof/plan/review. V1 giữ lịch sử, không dùng backup. Allowlist là chuẩn bị, chưa snapshot cho expanded Dev.

## Bảo toàn và checkpoint tiếp theo

`preservation-final.json`/`dev-after.json`: **40content/40source/51revision/80alias/60media(48image12PDF)**;53originalNotScanned+7Clean;70capture/68SQLsnapshot/8exception/2videometadata/0file. Tất cả17group hash trước/sau giống,0media/snapshot byte mismatch. Identity digest và `.env` SHA không đổi. Không sửa37bài nguồn cũ hoặc3bài3G. Không có Dev business write, signer/approval mới, grant, Production/DNS/Git/email/crawl trong lượt này.

Tiếp tục đúng12packet đã tạo; không dựng lại210 hoặc scan lại524 nếu proof vẫn strict-valid. Điều tra SQL riêng có kiểm soát, dùng trace/fingerprint thật; reconcile readonly trước thử b13/report mới và trước Test replay. Không chạm packet/JobId cũ để che thất bại. Sau user phê duyệt exact packet và SQL ổn định: backupDev40/60+Testreceipt/dependencies →freshpreflight→signedscopeApply→verify/readback/repeat/smoke. Proof/stamp/manifest đổi thì tạo packet mới, xác nhận lại hash trước ký.

NukeViet mapping/adapter bằng chứng thật còn pending riêng; quyền crawl hai nguồn không bị thu hồi. Historical raw incidents PARTIAL, Production scanner/MFA/restore/UAT/cutover vẫn chưa nghiệm thu. Website **CHƯA PRODUCTION READY**.
