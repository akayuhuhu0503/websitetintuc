# Phase 3F — báo cáo thu thập và nhập dữ liệu Test

**PARTIAL tổng nguồn. Phạm vi Test đủ điều kiện: PASS LOCAL. CHƯA PRODUCTION READY.**

Run `phase3f-20261019T070725Z-fa27c9d0`; trạng thái máy đọc và bằng chứng nằm tại `.local/evidence/phase3f/phase3f-20261019T070725Z-fa27c9d0`. Không Apply Dev, grant quyền, sửa `.env`, deploy/DNS/email hoặc phục dựng raw artifact lịch sử.

| Nội dung | Kết quả thực |
|---|---|
|Bóc tách nguồn|272 bản ghi theo loại, 267 canonical phân biệt; tăng232 bản ghi / **227 canonical** so với baseline40|
|Nhập mới SQL Test|**210 nội dung, 524 media**:181JPEG,127PNG,215PDF,1GIF; không tính fixture|
|Scanner thật|**542 SHA mới Clean**,0Infected/0ScanFailed; ảnh vượt chính sách vẫn Rejected/NotScanned/private|
|Coverage|1358URL/286candidate; **11 thiếu snapshot,3 cần kiểm tra parser**; tổng nguồn vẫn UNKNOWN|
|Còn chặn trong plan|25 dòng theo loại:22 mới/3 đã có; không đồng nghĩa25 bài độc lập|
|Bảo toàn Dev|37content/53NotScanned(41ảnh,12PDF)/70capture,68snapshotSQL/8ngoại lệ/2videometadata,0file; **0 sai khác/17 nhóm**|
|Bảo toàn Test cũ|40 nội dung/60 tài nguyên: source/content/revision/asset/file trước và sau giống hoàn toàn|

## Quyền thu thập và dữ liệu thật

Người dùng đã xác nhận trực tiếp quyền đọc/thu thập công khai QLDD và gdla. `authority-reference.json` trỏ bản ghi máy đọc của xác nhận thật; không yêu cầu thêm approval độc lập cho crawl. Bản ghi giới hạn cửa sổ vận hành và quota, không phải giấy phép tự ký. Không đăng nhập, vượt CAPTCHA, đọc CSDL gốc hay ghi lên nguồn.

Đã chạy12 work order cho228 ứng viên thiếu snapshot ban đầu; tiếp tục5 URL phát hiện mới và1 trang danh sách có giới hạn. Cùng session3A, một kết nối, nghỉ>=2giây, checkpoint trước GET và sau mỗi bước. QLDD có819 GET reserved mới (858 cộng dồn từ39), thêm2 probe gdla riêng;795.010.112 byte mạng. Không lấy sốURL hoặc nhãn Article/Announcement trùng canonical làm số bài mới.

## Delta đã triển khai và kiểm chứng

- Work order ràng buộc SHA/session/SourceIdentity, ưu tiên detail/file liên quan; tái sử dụng collector, checkpoint và importer hiện có.
- Hai snapshot gốc2024/3 vẫn rỗng hoặc thiếu title xác minh. Không lấy sidebar làm bài. Raw mới1716/3668 có canonical news, h1 và body thật: sửa hintDraft cũ bằng các selector/canonical chặt chẽ, replayoffline thêm2 bài và0GET. Không đổi Announcement/Static hoặc tài liệu có bảng metadata.
- PNG SHA`8d3197a85aa5ca0dcc631e8f84af5ff3b83f81260cc1a0af1a984b6b462538d2` vượt30MP từng làm batch9 dừng. Đã giữraw, cô lập MediaUploadException theo từng file và replay response đã bound mà không GET lại. Không tăng pixel limit.
- Preview phát hiện canonical thuộc nhiều danh tính ngay trong cùng plan; giữ review và không tự chọn loại/merge theo title/ghi đè bài đang công khai.
- NukeViet offline bundle giữ namespace/canonical/raw/hash, sao file private, tạo checkpoint và gợi ý trùng; chưa có SQL write adapter hoàn chỉnh hoặc nghiệm thu nguồn thật.

File mới chính: `src/MigrationCli/DetailBatch.cs`, `Phase3FCommand.cs`, `tests/LandPortal.Tests/Phase3FTests.cs`, `scripts/phase3f.ps1`, `scripts/test-phase3f.ps1`. Sửa SourceParser/SourceInventory/InventoryProcessing/InventoryReport/InventoryCommand/InventoryModel/CoverageAudit, SourceBatch/BatchReadiness/BatchCommand/SafeBatchCommand/CLIProgram, diagnostic root và test runner/evidence allowlist. **Không migration, index, SQL configuration hoặc CommandTimeout change.**

## Commit, reconciliation và chống trùng

Batch đầu`30f5f690-0920-4580-8659-7a42193f1e88` tạo169/Keep28 rồi dừng Announcement3589. Đối soát SQL xác nhận PARTIAL_VERIFIED:197 receipt/53 chưa commit/0 lỗi; dòng thất bại đã rollback. Không retry mù hoặc sửa plan/hash đã có receipt. Ba cặp Article/Announcement3589/3607/3684 dùng cùng canonical là nguyên nhân; có thêm guard và test từ chính plan thật.

Plan mới`869e46ee-815e-4304-a72c-0bb27a9d3cc3`, SHA`b20de33fd991f55aa00529f466c38e947c598e153802bf7f42f986969482c78a`:41Create/206Keep/25Blocked. Commit thành công,0Failed/NeedsReconciliation. Tổng169+41=210 nội dung mới. Verify247receipt/247revision/582media/499alias,0 lỗi. Repeat247AlreadyCommitted/25Blocked, DataSHA`0619ee4e8627cc58a9d10e99506467f66a3bea7ce4ba866167719d9c2a090f3f` giữ nguyên. Không sửa published/editorial của bản nguồn đã có.

`blocked-final.csv/json` có từng danh tính,URL và trạng thái frontier/format/scan. Lý do giao nhau: missingfile15,alias5,unsupported3,unresolvedattachment5,videometadata2. Các CDN như btnmt.1cdn.vn/monre.gov.vn/moitruongachau.com/media-dlrpd.monre.gov.vn bị allowlist kỹ thuật cũ từ chối; cần adapter tải tài nguyên liên kết an toàn, **không phải thiếu quyền crawl hai nguồn**. PDF legacy lỗi, PNG quápixel và alias cần review vẫn giữ riêng. Không bỏ file không an toàn để tăng số nhập.

## Gói trình duyệt Dev

Fresh preview Dev chỉ dùng cohort40 ban đầu:3bài mới/7JPEG/1revision so sánh/6Keep/30Blocked. **210 bài mới Test không tự đưa Dev.** Schema3 và media-rootSHA đúng; plan`965727ba-89e6-4c60-99e8-fbf948aecc9a`, SHA`fd8870b9977d890419b66acea06f7850ddd9340ba0ee593fbbfefc2faf6e8e04`. `dev-apply-manifest.json` liệt kê content/asset/revision/mappingURL/hash/scanner/dependency/delta.65dependency cũ không sai hash.

Trạng thái: **READY_FOR_TECHNICAL_REVIEW / NEEDS_DEV_APPROVAL, NOT_READY_EXECUTE**. Chưa cung cấp danh tính người duyệt Dev và signer/trust policy. Chưa tạo backup Dev mới hoặc chạy positive external signature. `dev-commands-NOT-EXECUTED.txt` có dry-run/Apply/verify/rollback chính xác với placeholder approvalký thật và receiptbackupDevfresh. Không tự ký/pin cert hoặc grant. Rollback chỉ gỡ publication mới chưa bị editor sửa, giữ raw/revision/file/alias/audit; không reset/restore. Quyền crawl không cấp quyền Apply Dev.

## Kiểm thử và backup

- **Finalfocused9/9PASS**, `phase3f-tests-20261019T083607Z-ee927b17`; Web/CLI/Tests/dependencies build0warning/error; frozen code0drift. Không full137/video/Worker/UItoànsite. Không cộng8/8,7/7,6/6 hoặc HTTP thành fullsuite.
- **HTTP cuối10checks PASS riêng**:2bài mới/chuyên mục/JPEG+PDF đúngSHA/Range206/guestadminACL/ảnhRejected404. Owned5312đã dừng. LượtHTTP đầu FAIL do helper đọc JSONArray sai tạoURLrỗng; raw giữ riêng, sửahelper và chạy lại.
- Hai lượt5/6FAIL trước cũng giữ raw:DbUpdateException chưa rõ innerSQL; lượttrace sau ghi SQLSavefixture24.146s vượt cửa sổHTTP20s trướcGET. Tách persistsetup trước cửa sổ backoff; không tăng SQLtimeout. Không suy ra đã chữa rootcausecategory/3D.
- Test backup COPY_ONLY/CHECKSUM và RESTORE VERIFYONLY PASS,7602 file dữ liệu SQL/media/snapshots/keys/inventory; không seal toàn repository. Addendum65dependency recovery đã copy/hash0mismatch, SQLreceipt trongbak. Attempt copy lồng đường dẫn gặp MAX_PATH được giữ; dùng mappingSHAflattened. **Restore thật NOT RUN.**
- Dev17nhóm,Identity và`.env` nguyên; Test40/60cũ nguyên. Historical V04/D15/v05 artifact incidents vẫn PARTIAL, không tái tạo giả.

## NukeViet và phần tiếp theo

Hai nguồn vẫn chính thức theo SRS1.0. Probe HTTP/HTTPS robots đều không nhận response; HTTPS8:01UTC ghi connectionrefused45.120.229.41:443. Không bypassTLS/firewall hoặc retry vô hạn. Offlinebundle có news/canonical/dedupe/privatefile/checkpoint; documentadapter chưa có mẫu thật, mapping nghiệp vụ và SQLwrite chưa đủ. **0 nội dung NukeViet đã nhập**; fixture không phải export nghiệp vụ.

Thao tác ngắn nhất: work order11detail mới trong`final-source-audit`, xử lý linked-asset allowlist/legacyPDF và5alias; tiếp tục khi lease vận hành hợp lệ, không hỏi lại quyền công khai đã xác nhận. Khôi phục kết nối gdla hoặc nhận export chính thức và duyệt mapping. PDF3233/Video40/41 không chặn nhập phần độc lập.

SQLtimeout vẫn MONITORING/ROOTCAUSEUNKNOWN. Production scanner/MFA/SMTP/restore/UAT/3Word/privateLookup/TLS/cutover giữ trạng thái riêng. Website **CHƯA PRODUCTION READY**.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3f.ps1
# Smoke thật, tên mới; tự bật/tắt Web SQLTest ở loopback5312:
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3f-http.ps1 -Label NEW_UNIQUE_LABEL
```

Web đã kiểm tra tại`http://127.0.0.1:5312`, hiện dừng. Lệnh dữ liệu thực nằm trong log/runbook; không dùng lại label cũ. Helper batch nhận `-PlanLabel test-preview-after-reconcile`; không chạy Commit để đăng lại nội dung đã rollback. CheckpointSQL cuối`.local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/739776f08d05a72048eeff9d10e34e318a58411873e6b2656d0fc53758996e8e.bin`, SHA`739776f08d05a72048eeff9d10e34e318a58411873e6b2656d0fc53758996e8e`.
