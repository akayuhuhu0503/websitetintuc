# Phase 3G — backup path fix và resume Apply, 21/10/2026

**PARTIAL: BACKUP PASS, APPLY BLOCKED bởi guard chữ ký của CLI.** Đã thử đúng một lần `safe-apply` sau backup/approval/preflight đạt. Guard từ chối trước khi ghi dữ liệu; dừng và reconcile theo yêu cầu người dùng, không retry hoặc bỏ kiểm tra. **CHƯA PRODUCTION READY.**

Run `phase3g-resume-20261021T030612Z-9f553132`; evidence `.local/evidence/phase3f/phase3g-resume-20261021T030612Z-9f553132`; pointer `.local/phase3g-resume-current.json`; kết quả `final-result.json`. Run cũ và backup dở được giữ nguyên. [Runbook backup mới](phase-3g-backup-resume-runbook.md).

## 1. Đường dẫn và thay đổi đã thực hiện

Lỗi cũ là Windows PowerShell `Copy-Item` khi sao chép snapshot `snapshots/survey/cuc-quan-ly-dat-dai-trao-doi-huong-dan-thao-go-vuong-mac-trong-quan-ly-dat-dai-tai-son-la-3681.html`: nguồn 173 ký tự còn nguyên, đích lồng sâu 269 ký tự vượt MAX_PATH thông thường 260, trả `DirectoryNotFoundException`. Không phải mất tệp nguồn hoặc lỗi SQL backup. Chi tiết/hash ở run trước, không phục dựng artifact.

Thêm ba script:

- `scripts/backup-approved-batch.ps1`: `-BackupRoot` cấu hình được, thư mục con ID ngắn, bắt buộc root **đã có** ACL private và quyền SQL service; không tự cấp quyền. Kiểm tra dung lượng, guard đúng đích, COPY_ONLY/CHECKSUM/VERIFYONLY/HEADER, full file snapshot, dependency backup, restore mapping, receipt cuối sau khi mọi phần đạt.
- `scripts/backup-file-snapshot.py`: extended-length Windows paths `\\?\`, inventory nguồn trước/sau, giữ nguyên tên/cấu trúc và bytes, CreateNew không ghi đè, SHA/length từng file, giữ thư mục rỗng; không follow reparse. Không bỏ qua lỗi đọc thư mục. Có readback và thử phục hồi file riêng.
- `scripts/test-backup-file-snapshot.py`: bốn kiểm tra kỹ thuật tập trung, không phải dữ liệu nguồn hoặc full suite.

Không sửa importer, scanner, approval verifier, migration hoặc binary đã review. Guard Apply hiện buộc backup dưới project `.local/backups`; vì vậy **không** tạo `C:\LPBackups`, đổi allowlist hoặc ACL để dùng đường dẫn ngoài. Dùng root private có sẵn và child ngắn `gaae71ea6`, `d/` và `t/` cho hai database. Đường dẫn đích file Dev dài nhất thực tế **226 ký tự**; Python vẫn hỗ trợ đường dẫn dài cho source/dependency. Không đổi tên/bỏ qua file nguồn và không đổi bảo vệ đường dẫn Windows.

## 2. Bộ backup hoàn chỉnh

Vị trí:

`C:\TrungTamDuLieuVaThongTinDatDai\website_tin_tuc\.local\backups\LandPortal_Test-20261021T015310Z\gaae71ea6\`

| Thành phần | Bằng chứng thực |
|---|---|
| `d/LandPortal_Dev.bak` | COPY_ONLY/CHECKSUM/VERIFYONLY + HEADER đúng Dev; 11.559.424 byte; SHA `9e7dcb3cb0a790e777024a47765206d38a685b46f91c37cb5d3cb1fc5ae63a17` |
| `t/LandPortal_Test.bak` | COPY_ONLY/CHECKSUM/VERIFYONLY + HEADER đúng Test, bảo vệ job/receipt scanner mới; 197.224.960 byte; SHA `7ef24cb605002357c8f1e86941a611c3a39a4af3a19ff052638604fdb39437fd` |
| Dev media/snapshot/keys + private environment copy | **130 file /46.758.697 byte**; đầy đủ inventory, bytes/hash nguồn và đích khớp; `d/file-manifest.json` |
| Exact dependencies | **132 file /37.009.351 byte**: 127 allowlisted + signed approval/receipt, xác nhận người dùng, scope và audit vị trí backup. Tên file được giữ trong `p/dependency/...`; `dependency-manifest.json` ánh xạ nguồn/đích/hash |
| Thử phục hồi file | **PASS 130 file**, khôi phục vào `r/` riêng dưới private backup, không thay active storage; hash/length khớp |
| Metadata/khôi phục | `RESTORE.md`, file/dependency manifests, `backup-location-audit.json`, `backup-LandPortal_Dev.json`, `backup-LandPortal_Test.json`, `backup-complete.json` |
| Dung lượng/quyền | Khoảng169GB free khi kiểm tra; parent SDDL trước/sau giống nhau, không cấp SQL/Identity/NTFS quyền mới |

**BACKUP PASS**, 0 thiếu file/0 hash mismatch. Sau chỉnh cuối để không bỏ qua directory đọc lỗi, đã chạy lại bốn focused checks và strict source inventory/readback của cả130+132 file, đều PASS (`backup-tests-final.json`, `file-readback-final.json`, `dependency-readback-final.json`). Không cộng những kiểm tra này thành full regression. SQL restore drill **NOT RUN**, không restore đè Dev hoặc Test; VERIFYONLY không được gọi là restore. Bản backup lỗi trước vẫn INCOMPLETE, không nâng nhãn thành PASS.

## 3. Approval và preflight

Batch vẫn **`26afcb7a-b3c1-4708-9009-c3d296e0afdc`**, canonical plan/manifest SHA **`a6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f`**. Phạm vi dự kiến3content/4revision/7JPEG/6alias, giữ6Keep/1comparisonDraftDocument5/30Blocked.

Approval đã ký `.local/approvals/phase3c/80ed3e4c-f2e9-4814-bc18-42f66c07f02f.ps1`, SHA `c8fa859ce10cea9c20433241e995244a13170a3d06f39d10e72ec63a4799a299`; actionApply, đúngSID/Dev, hết hạn **21/10/2026 21:54:56VN**. Đã kiểm tra lại Authenticode **Valid**, certificate CurrentUser/CodeSigningEKU/exactthumbprint/date, plan/manifest/scope đều PASS. Không ký mới, không gia hạn, không mở rộng authority. Metadata approval không có backup path; đổi vị trí backup không đổi plan, target media-root identity hoặc phạm vi ký. Audit riêng `approval-backup-binding-audit.json` ghi việc này; không execute approval PS1.

Đích `MinhAnh\SQLEXPRESS` / `LandPortal_Dev`, databaseGUID `b499750c-a0c8-4003-b7ee-dce4938f78dc`, connection target `.\SQLEXPRESS`; registeredTHANGPD là tên lịch sử chủ máy đã xác nhận. Schema10migrations/latest `20261015091551_V05EditorialDocuments`, serviceRunning, `.env`/tool/manifest không đổi. Không đổi SQL metadata hoặc quyền. `signature-current.json`, `signature-before-apply.json`, `sql-identity.json`, `schema.json` là evidence mới.

Defender vẫn engine **1.1.26090.9**, definitions **1.461.118.0**, service/AV/realtime hoạt động. Proof-set/SQL receipts/byte hash được CLI strict revalidate trong preflight và safe-apply trước guard approval. Không cần refresh hoặc thay proof-set; không scan/promote originalDev53. `before-apply/reconcile.json` **ABSENT_CONFIRMED10expected/0committed/0errors**, không timeout ở preflight này.

## 4. Guard mới phát hiện khi thực thi và điểm dừng

`safe-apply` exit1 tại `ApprovedBatchExecution.CreateAsync`:

> Approval signature is not from a configured trusted signer.

Đã chẩn đoán chỉ đọc, không retry Apply:

1. Tái tạo chính native PowerShell signature subprocess của code, cả khi approval bị giữ FileShare.Read và khi không lock: **exit0, signature Valid, thumbprint94797... đúng**, nhưng stderr chứa **616 byte CLIXML progress “Preparing modules for first use”**.
2. Code hiện từ chối mọi stderr không rỗng nên composite guard trả false. Bằng chứng `signature-native-locked.json` và `signature-native-unlocked.json`; không phải yêu cầu cấp thêm trust/quyền hoặc tạo certificate mới.
3. Probe standalone chỉ đọc, đặt `$ProgressPreference='SilentlyContinue'` trong child, giữ nguyên kiểm tra exit/status/thumbprint/stderr: **Valid, đúng signer, stderr0, compositeWouldAccept=true** (`signature-progress-only-readonly-probe.json`). Đây **không** phải app đã sửa hoặc Apply PASS.

Đã giữ guard ứng dụng nguyên và dừng theo chỉ thị “bất kỳ guard nào không đạt”; không sửa verifier để cho qua, không lọc stderr mù, không ký lại approval. Bước tiếp theo là sửa hẹp presentation của PowerShell child để progress không lẫn error stream, vẫn fail-closed với error thực và signature/hash/pin sai; kiểm thử trực tiếp rồi đánh giá lại binary/approval/packet trước lần Apply mới. Không coi certificate standaloneValid là appguardPASS.

## 5. Verify bảo toàn và việc chưa chạy

`after-denial-reconcile/reconcile.json`: **ABSENT_CONFIRMED**, expected10/committed0/missing10/errors0. SQL readback `batch-no-write-readback.json`: **0 ImportRun /0 import receipts /0 DevAuthorization** cho batch. Không retry, không cần rollback.

Dev trước/sau: **37 nội dung nguồn/37SourceRecords/47revision/74alias/53NotScanned (41ảnh/12PDF)**; 70capture/68SQLsnapshot;8ngoạilệ;2video metadata/0file. **0 mismatch/17nhóm**, Identity/credentials/stamps/roles/claims và `.env` nguyên. Đã nhập thực tế **0content/0revision/0asset/0alias**. `dev-after-denial.json`, `identity-after-denial.json` và `apply-denied-summary.json`.

| Trạng thái | Phạm vi |
|---|---|
| PASS | Backup đầy đủ, SQLVERIFYONLY/header/hash, file restore, final focused4/4, strict source/file inventory, standalone signature/scope, target/schema, strict Defender/preflight, reconcile/bảo toàn |
| FAIL | Một lần actual safe-apply: application native signature guard từ chối stderr progress |
| BLOCKED | Nhập batch Dev hiện tại; không gọi APPLIED hoặc PASS overall |
| NOT RUN | Post-Apply safe-verify/readback/idempotency repeat/smoke bài mới hoặc mediaACL sau nhập; SQLrestore drill; fullregression/buildC#/UItoànweb |

Không có nội dung mới để smoke; không tạo fixture hoặc sửa bài nguồn Dev để demo. Build C# không chạy vì không thay code/binary ứng dụng. Có wrapper transport báo WindowsAccessDenied trước khi child được khởi chạy; kiểm tra không có report/Apply mới rồi mới chạy file helper, actual safe-apply chỉ1lần. Lỗi tra cứu path/shell phụ không tính là test PASS.

Trước batch mở rộng khoảng210bài: giải quyết lỗi native signature bridge trên, kiểm lại freshness/byte/receipt toàn bộ asset và alias conflicts, manifest exactdelta theo Dev lúc đó, backup còn hiệu lực và approval riêng đúng batch/hash/phạm vi; approval3bài hiện tại không cấp quyền cho210. Không copy database Test hoặc nhận fixture vào Dev. SQLtimeout **ROOT CAUSE UNKNOWN / MONITORING**, rawartifact incidents **PARTIAL** và các blocker Production trước đây giữ nguyên. Không `.env`, grants, Production/DNS/email/crawl/commit/push changes.
