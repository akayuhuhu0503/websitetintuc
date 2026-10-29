# Phase 3G Fix — định danh, signer, Defender và fresh preview

**PARTIAL: định danh vật lý đã chứng minh, recovery proof và preview/preflight đạt; Apply Dev vẫn BLOCKED. CHƯA PRODUCTION READY.**

Run `phase3g-fix-20261019T095047Z-94f0e99e`, 19/10/2026. Evidence `.local/evidence/phase3f/phase3g-fix-20261019T095047Z-94f0e99e/`; scanner evidence `.local/evidence/phase3c/phase3g-fix-20261019T095047Z-94f0e99e/`; pointer `.local/phase3g-fix-current.json`. Không sửa raw/manifest của lượt 3G cũ.

## Sáu kết luận

1. **Cùng instance: đã chứng minh.** Kết nối độc lập qua `.\SQLEXPRESS` và `MinhAnh\SQLEXPRESS` đều trả PID SQL **20776**, trùng Windows service `MSSQL$SQLEXPRESS` đang Running. Cùng `DB_NAME() = LandPortal_Dev`, database ID50 và GUID `b499750c-a0c8-4003-b7ee-dce4938f78dc`; cùng transport Shared memory. Registry instance map là `MSSQL16.SQLEXPRESS`, phù hợp đường dẫn service/data SQL. Windows, ActiveComputerName, configured ComputerName và `SERVERPROPERTY('MachineName')` đều MinhAnh; InstanceName SQLEXPRESS, Edition Express64-bit. **Đích không phải một SQL instance khác.** `SERVERPROPERTY('ServerName')` vẫn `MinhAnh\SQLEXPRESS`, `@@SERVERNAME` vẫn `THANGPD\SQLEXPRESS`: có dấu hiệu tên đăng ký SQL cũ, nhưng không có hồ sơ lịch sử để kết luận thời điểm/người đổi tên máy. Bằng chứng này để người có thẩm quyền xác nhận lại định danh đích; không tự sửa metadata hoặc nới guard. Xem `sql-identity.json`.
2. **Signer: chưa có trong các kho đã được phép kiểm tra.** CurrentUser/My và LocalMachine/My có **0 certificate mang Code Signing EKU**, cấu hình có0 trusted thumbprint và chưa có approval file. Không kết luận về token/HSM/kho từ xa chưa được cung cấp. Chưa có certificate cụ thể nên thumbprint/trust chain/identity signer hiện **N/A**, không bịa giá trị. Đã mô tả chính xác các điều kiện code thực thi trong [hướng dẫn ký](dev-import-signing-guide.md); không tạo hoặc cài certificate, sửa trust/.env/quyền.
3. **Recovery proof mới: PASS trong scope60.** Resume cùng JobId `301f013b-ff04-43d5-8f2f-460b8bfae8f8`, quét lại **53 public Test copies +7 ảnh mới public**, không quét original Dev53. Defender thật engine `1.1.26090.9`, definitions `1.461.102.0`. Kết quả nguồn **60Clean/0Infected/0ScanFailed**, hash nguồn/proof0mismatch;0missing/duplicate exact SQL receipt. SQL job hiện120 scan receipts (60cũ+60mới). New proofset SHA `e4e554ef2a65566067dfc1191e2db133536319eb7532ea6efd0519182e341510`. Native proofs và SQL audit append riêng; không StageTest, không đổi flags Dev/Test media. TTL24h và current definitions vẫn được kiểm lại khi preview/preflight; bản cũ không bị sửa.
4. **Fresh preview/preflight: PASS chỉ đọc cho hai phạm vi**, bảng dưới. Reconcile đều `ABSENT_CONFIRMED`,0committed/0errors: chưa có Apply các plan mới. Không gọi kết quả này là verify sau nhập hoặc idempotency. Plan bind schema3, Dev media-root, checkpoint và proofset mới. Đã đối chiếu exact SourceIdentity với hai batch Test thật và hash/dung lượng file cần dùng; không đưa technical fixtures vào manifest.
5. **Còn cần authority:** xác nhận lại tuple định danh trong mục1; chọn signer/chain/thumbprint được duyệt và cung cấp approval Authenticode thật theo exact plan/operator/action/expiry. Business Dev Apply đã USER_CONFIRMED, không yêu cầu lại chỉ vì thiếu tệp business approval. Backup Dev mới + SQL/media/header/hash/VERIFYONLY vẫn bắt buộc sát thời điểm Apply. Batch mở rộng còn521 tài nguyên dùng scan metadata cũ, không nằm trong refresh60: cần refresh/binding theo policy trước đề nghị Apply phạm vi đó; không giả định preview đồng nghĩa mọi asset đã được quét bằng definitions hiện tại.
6. **Apply Dev: BLOCKED,0content/0revision/0asset đã nhập.** Không gọi safe-apply/rollback, không migration/metadata/Identity/env/grants/crawl/deploy/push. Không full regression hoặc UI/video không liên quan.

## Kết quả preview thực tế

| Phạm vi | Rows | Content mới | Revision dự kiến | Asset mới | Alias thêm | Keep | Comparison | Blocked |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
|Ưu tiên cohort40|40|3|4|7|6|6|1|30|
|Mở rộng checkpoint3F|272|210|211|528|420|6|1|55|

Đây là **hai phương án có giao nhau**, không cộng số lượng và không Apply cả hai plan liên tiếp khi chưa fresh preview lại. Batch ưu tiên gồm Article3666/3670/3673,7JPEG=2.048.267byte. Batch mở rộng gồm chính3 bài ưu tiên +207 trong210 bài mới Test Phase3F,528assets=623.832.138byte. Article3589/3607/3684 đã có trên Test nhưng hiện bị `INTRA_BATCH_ALIAS_COLLISION` với các danh tính Announcement khi cùng còn mới trên Dev. Không tự chọn bên thắng/merge hoặc ép số210 mới ở Dev thành cùng tập210 Test. `dev-manifest-expanded.json` liệt kê rõ từng dòng chặn và crosswalk; lỗi/lý do có thể giao nhau.

Plan ưu tiên `65808e07-8132-4487-aa6c-6e45894560f8`, SHA `f3b5037274f0c6e03dc7d5a6369d52ae98e487fa0c46b4dff70f87b55ada1051`. Plan mở rộng `e8e1f4e6-0b83-4fd9-91fc-19f649a7e7cd`, SHA `cf3d4243ff95b0c0feea561a792d17a40b29901eef9555d36b431663970cf347`. Preflight expected10/217eligible rows,0committed,0errors.

`dev-manifest-priority.json` và `dev-manifest-expanded.json` là **manifest chuẩn bị**, không phải signed approval. Cả hai có hash/source/scan metadata/mapping/expected delta. Checkpoint gốc giữ scan metadata lịch sử;7 ảnh được overlay RAM từ fresh proofs khi chạy CLI. Với batch mở rộng, **521 asset ngoài recovery set vẫn ghi engine/definitions cũ1.461.96.0**; đã ghi hạn chế, không relabel hoặc báo refresh528/528.

## Scanner controls và giới hạn kết luận

- Control cuối dùng đúng adapter ứng dụng, public Test photo → Clean; ZIP chứa EICAR68byte chuẩn → **Infected, `Virus:DOS/EICAR_Test_File`**, có engine/definitions/outputSHA; hủy health-process thật sau50ms → **ScanFailed**, không Clean fallback. Đây là control kỹ thuật, không phải nội dung nguồn hay sự cố scanner tự nhiên.
- Lượt control đầu dùng ZIP không nén: Clean đạt, EICAR trả ScanFailed `DefenderUnavailableTimeoutOrFileChanged`, nên cả control **FAIL**. Không kết luận nguyên nhân cụ thể từ mã lỗi tổng quát. Giữ nguyên `controls.json/log/exit`; lượt sau dùng fixture ZIP deflate chuẩn như test hiện có, output riêng `controls-compressed.*`, **PASS**. Không thay AV exclusions/remediation/firewall/service hoặc lấy ScanFailed làm Infected.
- DTO failure có Provider mặc định `Clamd` trong artifact khi không có engine result; control thực gọi `DefenderInquiryScanner`, không chạy Clamd. Chỉ Clean/Infected có native engine proof; không gán engine giả cho cancellation.
- Chỉ build harness private kiểm scanner,0warning/error; code ứng dụng/CLI/Web/worker không đổi, không build lại hoặc chạy full suite. Harness dùng DLL hiện hành, các SHA đã ghi trong `code-and-env-preservation.json`. Không cộng60 native scans/2preview/control thành số regression.

## Dung lượng, backup và bảo toàn

Dev ROWS allocated72MiB, used khoảng10,75MiB; Test allocated200MiB, used khoảng187,44MiB. SQLServer2022Express có giới hạn database quan hệ10GB theo [Microsoft](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022#scale-limits). `max_size=-1` của file không xóa giới hạn edition. ỔC còn175.736.238.080byte tại lần đo;7ảnh ưu tiên khoảng1,95MiB,528asset mở rộng khoảng595MiB nằm ở kho tệp, không phải blob SQL. Dung lượng hiện chưa là blocker; phải đo lại trước backup/Apply, không coi số này là dự báo tăng trưởng dài hạn. Xem `capacity.json`.

Trước append SQLTest scan receipts đã xác minh lại SHA `.bak` của backupTest3F còn hạn, COPY_ONLY/CHECKSUM/VERIFYONLY PASS theo receipt đã có; không chạy VERIFYONLY/restore mới trong lượt này. Backup cũ chưa chứa60receipt mới: trước Apply cần bảo vệ SQLTest proof receipts cập nhật và jobproof/files bằng backup/addendum phù hợp, cùng backupDev mới. Không gọi VERIFYONLY là restore drill.

Dev trước/sau và so checkpoint3G: **37content/37source/47revision/74alias/53NotScanned41ảnh12PDF**,70capture/68snapshot,8ngoạilệ,2video metadata/0file; **0khác/17nhóm**, filehash0mismatch. Identity/password/stamp/roles/claims digest và `.env` không đổi.17 source/script paths trong seal3F không drift. Thay đổi chỉ là docs, helper/harness/evidence ignored `.local`; không migration/source application edits.

SQL timeout vẫn **ROOT CAUSE UNKNOWN**; identity chứng minh được không phải bằng chứng nguyên nhân timeout. Incident artifact lịch sử vẫn PARTIAL. [Runbook](phase-3g-runbook.md) và [hướng dẫn ký](dev-import-signing-guide.md) là bước tiếp; scannerProduction/MFA/restore/UAT/Word/private lookup/cutover vẫn riêng.
