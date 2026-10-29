# Phase 3G — exact approval và lần thực thi ngày 21/10/2026

**BLOCKED_BACKUP — đã ký approval, CHƯA APPLY.** Người dùng đã duyệt đúng batch/hash trong phiên; không còn chờ phê duyệt nghiệp vụ cho chính packet này. Theo yêu cầu dừng khi bất kỳ guard không đạt, đã dừng khi bộ backup không hoàn tất. Website **CHƯA PRODUCTION READY**.

Run `phase3g-apply-20261021T025312Z-e91d1bf1`; evidence `.local/evidence/phase3f/phase3g-apply-20261021T025312Z-e91d1bf1`; pointer `.local/phase3g-apply-current.json`. Kết quả cuối nằm trong `stopped-result.json`; không tái sử dụng thư mục/log cũ.

## Phạm vi được duyệt và chữ ký thực tế

- Batch `26afcb7a-b3c1-4708-9009-c3d296e0afdc`.
- Canonical plan/manifest SHA-256 `a6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f`; giữ nguyên, không tạo manifest thay thế.
- Tối đa 3 content mới (Article:3666/3670/3673), 4 revision (có 1 comparison DraftDocument:5), 7 JPEG / 2.048.267 byte, 6 alias. 6 Keep và 30 Blocked giữ đúng plan; không coi 30 mục là 30 bài mới.
- Approval `.local/approvals/phase3c/80ed3e4c-f2e9-4814-bc18-42f66c07f02f.ps1`, SHA-256 `c8fa859ce10cea9c20433241e995244a13170a3d06f39d10e72ec63a4799a299`; action **Apply**. Không execute file approval.
- Người duyệt được ghi đúng là chủ dự án/người dùng trong phiên, tham chiếu `user-confirmation.json`; không giả tên pháp lý hoặc hồ sơ bên ngoài. Native Authenticode **Valid**, Code Signing EKU, chain CurrentUser, private key không export, thumbprint `94797BD79E275B09DEF613036BC00DF4EA91345F`. `signature.json` PASS cho exact plan/manifest/target/operator. Chứng chỉ Dev không cấp quyền Production.
- Approval hết hạn `2026-10-21T14:54:56.6641831Z` (21:54:56 ngày 21/10 giờ Việt Nam). Đã ký không đồng nghĩa backup hoặc Apply đã đạt; không tự gia hạn approval.

## Kiểm tra thực chạy

| Kiểm tra | Kết quả | Bằng chứng trong run |
|---|---|---|
| 127 dependency: byte length/SHA, plan/review/tool và `.env` | PASS | `initial-guards.json`, `baseline-comparison.json`; các hash lấy từ packet đã duyệt |
| SQL identity/schema/service/capacity | PASS tại thời điểm kiểm tra | `sql-identity.json`, `schema.json`, `sql-service.json`, `capacity.json`; DB GUID đúng, 10 migrations/latest `20261015091551_V05EditorialDocuments` |
| Current Defender và exact-byte/proof binding | PASS preflight | `defender-current.json`, `initial-preflight-native/reconcile.json`; engine `1.1.26090.9`, definitions `1.461.118.0`, service/AV/realtime bật. CLI revalidate proof-set/SQL receipts và checkpoint. Không scan lại hoặc chuyển 53 asset Dev sang Clean |
| Fresh readonly preflight đúng packet | PASS | `initial-preflight-native-exit.json`, `reconcile.json`: ABSENT_CONFIRMED, Expected 10, Committed 0, Missing 10, Errors [] |
| Chữ ký approval thật | PASS | `signature.json`, `signed-approval-reference.json`, receipt cạnh approval |
| SQL Dev backup | PASS phần SQL | `partial-sql-backup-readback.json`: COPY_ONLY/CHECKSUM/VERIFYONLY, HEADERONLY đọc đúng database/checksum/COPY_ONLY, SHA đọc lại; **không phải restore drill** |
| Backup đầy đủ media/snapshot/keys/dependency | FAIL / INCOMPLETE | `backup-error.json`, `backup-path-diagnostic.json`; dừng, không phát hành receipt backup hoàn chỉnh |
| Bảo toàn Dev sau khi dừng | PASS 0 mismatch/17 nhóm | `dev-before.json`, `dev-after-stopped.json`; `identity-after-stopped.json` PASS; `.env` nguyên |
| SQL Test backup mới sau scan, 127 dependency + signed approval backup | NOT RUN | Thực thi dừng trước các bước này |
| Preflight ngay trước Apply, safe-apply/verify/readback/repeat/smoke mới | NOT RUN | Không có Apply, không báo verify/idempotency giả |
| Full regression/build/UI toàn website | NOT RUN | Không thay code ứng dụng; FAST DELIVERY |

Đích vẫn `MinhAnh\SQLEXPRESS` / `LandPortal_Dev`, connection target `.\SQLEXPRESS`, database GUID `b499750c-a0c8-4003-b7ee-dce4938f78dc`. Registered name `THANGPD\SQLEXPRESS` giữ nguyên lịch sử đổi tên đã được chủ máy xác nhận; không sửa metadata hoặc nới guard.

## Backup chưa đạt và điểm dừng

Không cấp quyền SQL/ứng dụng/Identity, không sửa ACL thư mục backup có sẵn. Chọn thư mục con mới của kho project đã có quyền SQL service từ trước:

`.local/backups/LandPortal_Test-20261021T015310Z/phase3g-apply-20261021T025312Z-e91d1bf1/LandPortal_Dev/`

File SQL mới `LandPortal_Dev.bak`: **11.559.424 byte**, SHA-256 `356bf8adcf18d6c032cde12b8a90e25b377c44b580814f563c3c7b7103fcd01d`, HEADER backup 09:55:59 ngày 21/10 giờ máy. SQL backup/VERIFYONLY hoàn tất trước bước Copy-Item; header và hash đã được đọc độc lập sau khi dừng.

Lựa chọn đường dẫn backup lồng quá sâu của lượt này làm đích snapshot `.../snapshots/survey/cuc-quan-ly-dat-dai-trao-doi-huong-dan-thao-go-vuong-mac-trong-quan-ly-dat-dai-tai-son-la-3681.html` dài **269 ký tự**. Windows PowerShell Copy-Item trả `DirectoryNotFoundException`; tệp nguồn còn tồn tại ở đường dẫn 173 ký tự, SHA `0be2bc12a9da5aadc691327caae7727d28e208586951a25c8454b61476baa1c5`. Media copy đã chạy trước snapshot, nhưng bộ sao lưu chưa hoàn tất và chưa được hash readback toàn bộ. **Không được dùng bản này hoặc tự tạo receipt PASS để Apply.** Giữ nguyên các tệp backup dở, không ghi đè backup trước.

Parent ACL SDDL trước/sau bằng nhau. Chỉ tạo thư mục approval private theo quy trình ký đã được duyệt (operator/SYSTEM/Administrators); không cài/trust chứng chỉ lại, không LocalMachine/ExecutionPolicy/User/Machine environment changes. Signer pin chỉ process Development.

Harness có các lỗi riêng đã ghi trong `harness-incidents.json`: interpolation service name, RTK stderr làm Stop wrapper trước report, so sánh ScanStatus string với số, và đọc LASTEXITCODE sau script PowerShell không có native exit. Đã đối chiếu kiểu dữ liệu domain, report chữ ký PASS và native preflight exit0 trước backup; không tính các lỗi wrapper thành PASS của suite hoặc giấu chúng. Lỗi backup thực được giữ riêng và dẫn đến STOP.

## Bảo toàn và checkpoint tiếp tục

Dev trước/sau: **37 contents /37 SourceRecords /47 revisions /74 aliases /53 NotScanned (41 ảnh/12 PDF)**; 70 capture/68 SQL snapshot; 8 ngoại lệ; 2 video metadata/0 video file. Hash các file nguồn khớp. **0 thay đổi/17 nhóm**, Identity/credentials/stamps/roles/claims và `.env` giữ nguyên. Đã nhập **0 content/0 revision/0 asset/0 alias**. Không cần rollback vì chưa chạy Apply hoặc ghi dữ liệu import.

Bước tiếp theo là sửa cách tạo bản backup mới để hỗ trợ đường dẫn dài (hoặc dùng đích project ngắn đã có private ACL phù hợp), verify đầy đủ SQL + media/snapshot/keys + SQLTest receipt + exact dependencies, không đổi registry/ACL hay ghi đè bản dở. Sau đó kiểm lại signer/approval expiry, đúng manifest/hash/target/current Defender, fresh preflight rồi mới Apply. Không cần xin lại phê duyệt nghiệp vụ nếu vẫn đúng batch/hash/phạm vi và approval còn hiệu lực; nếu proof/config/target thay đổi phải dừng theo yêu cầu, không thay packet ngầm. Không chạy lại helper backup hiện tại vào cùng run/path.

Chỉ dẫn CLI vẫn theo [runbook strict packet](phase-3g-stabilization-runbook.md); **không truyền partial backup này cho safe-apply**. Context của lượt trong `.local/phase3g-approved-context.ps1` pin exact bytes; helper `.local/phase3g-apply-backup.ps1` đã dừng, chưa khắc phục long-path và chưa an toàn để chạy lại cùng output. Không sửa source/migration/.env, không grant, email, crawl, push, deploy hoặc DNS. SQL timeout **ROOT CAUSE UNKNOWN / MONITORING** và các incident raw lịch sử **PARTIAL** giữ nguyên.
