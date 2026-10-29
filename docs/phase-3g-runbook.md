# Runbook Phase 3G — tiếp tục Apply Dev an toàn

**CurrentStabilization21/10:** [báo cáo](phase-3g-stabilization-report.md)/[exactpacket runbook](phase-3g-stabilization-runbook.md). SQLcuối4/4PASS,rootUNKNOWNmonitor;strictBfreshnativeproof118đãđạt,newbatch26afcb7a-b3c1-4708-9009-c3d296e0afdc/SHAa6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f **READY_FOR_APPROVAL**,unsigned/notApplied. Chờownerexactbatchconfirmationtrướcký;freshbackups+proofcheckstạiApply. Khôngdùngoldpacket14849/bad423...cũ.

**Checkpoint21/10 mới nhất: [tracking-fix PARTIAL](phase-3g-tracking-fix-report.md).** Owner chọn sửa trước ký, không phê duyệt exactbatch. Nhãncodeđãsửa/buildPASS, SQLacceptance/preflighttimeout,proof112vs118stale; chưaký/Apply. TrạngtháiREADY20/10dướiđâylàlịchsử. KhôngchạylệnhApplytrướcSQLtest/reviewrecovery/freshpacket+ownerconfirmation.

**Cập nhật20/10 — dùng packet mới, không dùng plan/proof cũ bên dưới:** [exact batch/manifest/ngoại lệ](phase-3g-final-approval-report.md), [lệnh hiện hành](phase-3g-final-approval-runbook.md). Batch14849e55-a85c-4d7f-94cf-1253ac5103ef/SHA bad4234f01ff503762c966ccf675cdc9168ca945e5753a8eaa1b21ab85bcbafe, READY_FOR_APPROVAL/chưaApply. Process-onlyDevsignerpolicy được owner cho phép và config loader thật đã kiểm; nativeproofdefs112 mới. **Chờ xác nhận exact batch+hash trước khi ký.** BackupDev/updatedSQLTestproofreceipts+dependencybackup rồi freshpreflight/Apply/verify sau approval; tất cả chưa chạy. Ngoại lệ trackingCrawlRun inheritedTechnicalFixture đã trình; không tự sửa importer/bỏguard. Phần bên dưới giữ như lịch sử.

**Checkpoint mới 3GFix:** xem [báo cáo](phase-3g-fix-report.md), [hướng dẫn ký](dev-import-signing-guide.md), pointer`.local/phase3g-fix-current.json`. Cùng instance đã được chứng minh qua hai endpoint/PID/service/databaseGUID; registered name vẫnTHANGPD, không sửa guard/SQLmetadata.60freshproofClean/0byte mismatch, currentdefinitions1.461.102.0; oldproof giữ nguyên. NewproofsetSHA`e4e554ef2a65566067dfc1191e2db133536319eb7532ea6efd0519182e341510`, nằm trong scanRoot của pointer. FreshpriorityplanSHA`f3b5037274f0c6e03dc7d5a6369d52ae98e487fa0c46b4dff70f87b55ada1051`,3new/4rev/7asset/6alias,preflightABSENT_CONFIRMED0errors.ExpandedplanSHA`cf3d4243ff95b0c0feea561a792d17a40b29901eef9555d36b431663970cf347`,210new/211rev/528asset/420alias/55Blocked; giao nhau vớipriority, không cộng hoặcApplycảhai mù.521asset mở rộng chưa đượcrefresh trongscope60.0certificateCodeSigningtrong2storeMy,0trustedthumbprint/approval; **ApplyvẫnBLOCKED**.

Các điều kiện và lệnh lịch sử bên dưới giữ để giải thích checkpoint cũ, không coi đường dẫn/proof cũ là còn hợp lệ. Sau authority xác nhận định danh/chọn signer, dùng plan/proof mới còn hiệu lực hoặc preview lại; backupDev fresh và SQLTestproofdependencies trướcApply. Không chạy helper vào label cũ.

Checkpoint `phase3g-20261019T092316Z-24fe1477`, [báo cáo](phase-3g-report.md). Phê duyệt nghiệp vụ Dev đã **USER_CONFIRMED**; không coi thiếu một tệp business approval riêng là blocker mới. Các gate kỹ thuật hiện hành vẫn bắt buộc.

## Điều kiện để mở chặn

1. DBA/người vận hành xác minh `@@SERVERNAME = THANGPD\SQLEXPRESS` trong khi `SERVERPROPERTY('ServerName') = MinhAnh\SQLEXPRESS`, Windows/MachineName `MinhAnh`. Không tự sửa đăng ký instance hoặc SQL hệ thống. Theo yêu cầu hiện hành, Apply chỉ tiếp tục khi điều kiện đích đã được giải quyết và kiểm lại; không chỉ thay hằng expected để bỏ lỗi.
2. Đơn vị có thẩm quyền chọn/duyệt certificate signer và cơ chế cấp cấu hình `SafeImport__ApprovedSignerThumbprints` hợp lệ. Agent không tự thêm trust, sửa `.env`, cài cert hoặc cấp quyền. File approval thật phải nằm dưới private `.local/approvals/phase3c/`, có Authenticode `Valid`, thumbprint đã duyệt. Không thực thi file approval như script.
3. Refresh proof với engine/definitions hiện hành trên Test public copies theo runbook recovery. Proof recovery cũ 60 tệp còn TTL nhưng khác definitions, không sửa nhãn hay hash; không quét/promote original Dev53. `RecoveryEvidence` hiện validate cả proof set trước khi loại protected53 khỏi overlay Dev: không bỏ validation hoặc dùng tập bị sửa để vượt gate. Refresh cùng JobId theo giới hạn retry hoặc review một job mới nếu công cụ yêu cầu. Chỉ dùng lệnh scan Test có scope/backup/allowlist đúng quy trình; không scanner SaaS/AV configuration change.

## Trình tự sau khi đủ điều kiện

- Tạo run ID/evidence mới. Giữ raw run 3G này và mọi manifest cũ nguyên; CLI hiện nhận report trong `.local/evidence/phase3f/<UNIQUE_RUN>/`, không cần sửa source chỉ để đổi namespace mốc.
- Xác minh bằng kết nối rõ `LandPortal_Dev`: `@@SERVERNAME`, `SERVERPROPERTY`, `DB_NAME()`, `Sql__ExpectedDatabase`, schema, instance local, kho media Dev. Dùng `common.ps1` trong PowerShell mới, không kế thừa Test storage. Mail Disabled, không bypass scanner.
- Fresh Preview Dev batch ưu tiên bằng `scripts/phase3f.ps1 -Action Preview -Database LandPortal_Dev -NoBuild` với exact checkpoint/proof/inventory/review SHA. Bản 3G dry-run đã lỗi không có plan. Batch 210 mới cần preview từ checkpoint 3F và Dev bindings riêng; không copy toàn Test, không sửa target trong plan Test.
- Read-only `batch-preflight`/reconciliation với exact plan: không receipt không đối soát hoặc transaction đang áp dở. Stamp đã đổi phải preview lại; kết quả không chắc chắn phải reconcile trước retry.
- **Backup mới bắt buộc sau xác minh đích và trước Apply.** `scripts/backup-project.ps1 -Database LandPortal_Dev -EvidenceDirectory .local/evidence/phase3c/<UNIQUE_BACKUP_RUN>` tạo COPY_ONLY/CHECKSUM/VERIFYONLY, giữ media/snapshot/keys trong private backup. `-InventorySession` chỉ dùng nếu thật sự cần, tránh sao không chọn lọc kho lớn. Không thay quyền database; ACL thư mục backup chỉ theo quy trình được phép. Nếu service không truy cập được folder, dừng và báo lỗi, không mở ACL toàn máy.
- Đọc lại từng file trong manifest backup, SHA `.bak`, RESTORE HEADERONLY đúng Dev, timestamp/size/checksum; receipt phải <=12 giờ. Giữ các dependency plan/proof/raw của batch và SQLTest receipts cần thiết bằng addendum allowlist có hash. VERIFYONLY không phải restore drill. Không ghi đè backup cũ.
- Người có thẩm quyền ký metadata chính xác sau khi có plan mới: tiền tố `# LANDPORTAL_PHASE3C_APPROVAL `; JSON gồm `ApprovalId`, `PlanSha256`, `TargetDatabase`, `OperatorSid`, `ExpiresUtc`, `Actions` (`Apply`, và `Rollback` nếu được duyệt). Tối đa7 ngày; quyền operator phải có sẵn. Không tạo một approval đã ký giả hoặc dùng xác nhận chat thay chữ ký.
- Chạy `safe-apply` qua CLI build hiện hành với exact plan/checkpoint/review/proofs/approval/backup receipt và report mới. Không dùng `batch-commit` Dev hoặc direct SQL để lách. Verify theo cùng plan; so expected delta và dữ liệu nguồn cũ, exact file bytes/scan/public-private ACL. Repeat chỉ khi công cụ kiểm receipt an toàn, không sửa plan đã có receipt.
- Nếu lỗi/ack không chắc chắn: dừng batch tiếp theo, `batch-preflight`/reconcile đọc SQL trước; không giả định rollback. Khi đã xác minh commit, idempotency phải trả AlreadyCommitted và không tăng content/revision/media/alias.
- Smoke đúng bài/chuyên mục/tìm kiếm/media vừa nhập qua loopback; không deploy hoặc chạy UI toàn website. Đối chiếu nhóm nguồn, Identity và `.env` trước/sau; các delta mới phải đúng manifest và old content/editorial bất biến.

## Rollback đúng phạm vi

`safe-rollback` dùng cùng exact plan và signed action `Rollback`, backup fresh, report mới. Chỉ withdraw publication mới thuộc batch chưa bị editor sửa; giữ source/revision/file/alias/audit để đối soát. Đây không phải xóa vật lý toàn bộ batch. Không xóa dữ liệu ngoài batch hoặc restore cả DB cho lỗi nhỏ. Restore toàn DB cần quyết định riêng sau đánh giá dữ liệu phát sinh và kho media. Không chạy rollback khi chưa Apply, hoặc repeat Commit để tự đăng lại bài đã withdraw.

## Lệnh kiểm tra trong lượt 3G

Đã chạy đúng một preview bằng helper private:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3g-readonly.ps1 -Scope Priority -Action Preview
```

Kết quả BLOCKED scanner proof, log giữ trong run. **Không chạy lại lệnh này vào cùng run**: helper từ chối ghi đè evidence. Helper chỉ có Preview/Preflight, không có quyền ghi Dev và không giải quyết gate target/signature. Các lệnh Apply của gói 3F vẫn là NOT EXECUTED; không chạy nguyên template cũ vì proof hiện không hợp lệ. Chưa có lệnh Apply 3G executable do chưa có fresh plan/approval/backup.

Không cần full137, build nếu không sửa source, video hoặc UI toàn site. Mọi lượt mới phải giữ riêng PASS/FAIL/BLOCKED/NOT RUN. SQL timeout UNKNOWN, artifact incidents PARTIAL và Production scanner/MFA/restore/UAT/Word/private lookup/cutover vẫn là các việc riêng. **CHƯA PRODUCTION READY.**
