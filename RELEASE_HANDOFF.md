# Trạng thái bàn giao — 02/11/2026

Source bàn giao: [`ed7f5e64d68a18e953c9531421e5bf90c0d9a56c`](https://github.com/akayuhuhu0503/websitetintuc/commit/ed7f5e64d68a18e953c9531421e5bf90c0d9a56c), nhánh `main`. Commit tiếp theo chỉ ghi SHA này vào tài liệu; mã ứng dụng/publish không đổi. Dùng `git rev-parse HEAD` và kết quả push để đối chiếu HEAD cuối. Không tuyên bố PRODUCTION READY.

Publish cuối 02/11/2026: **PASS**, Release Web/Worker/MigrationCli/UatDataHandoff; `.local/publish/TTDL-IIS`, 396 file đối chiếu manifest, 0 mismatch. Đây là **cấu hình fixture kỹ thuật**, không phải cấu hình TTDL được cấp. `.env` thật không thay đổi; không kết nối SQL hoặc cài IIS/service. Hai tài liệu bàn giao ở gốc Git này đi kèm `START.md` trong gói. LAN public smoke 6 URL HTTP200; kiểm tra JS UUID ở HTTP context PASS, không thực hiện restore revision. Không chạy lại full regression/E2E BA.

## Phần đã lập trình và giới hạn bằng chứng

- CMS revision/draft/autosave/duyệt-trả bài/xuất bản/gỡ-khôi phục/hẹn giờ, quyền theo vai trò/chuyên mục, mật khẩu BA 8 ký tự đủ 4 nhóm/đổi lần đầu, CAPTCHA/lockout/idle; không MFA. Giữ các PASS LOCAL đã ghi nhận, không chạy lại toàn suite.
- Tin/chuyên mục/tìm kiếm tiếng Việt, văn bản/dự thảo, công khai thông tin, lịch tuần có phiên bản, Word/PDF/Excel, thống kê 34 tỉnh, video nội bộ, tương tác có kiểm duyệt/phân công/ACL/outbox, menu/footer, tin liên quan/xem nhiều có source. Dữ liệu và cấu hình thực chưa đầy đủ không được thay bằng dữ liệu giả.
- FR-06 email nhận góp ý: CODE COMPLETE offline, nullable theo revision, validate khi có nhập, preview/history/public đúng revision. **DEPLOYMENT BLOCKED** vì migration chưa Apply. Không nhận build là PASS SQL/LAN.
- FR-10 featured fingerprint đã triển khai trên LAN; kiểm tra offline PASS. E2E bật/tắt→gửi→trả→duyệt và bản công khai giữ nguyên **CHỜ BA** dùng credential hiện hành; credential cũ Writer/Reviewer MISMATCH, không reset. Kịch bản tại đầu `docs/PROJECT_STATE.md`.
- Giao diện dùng layout/CSS/partials chung cho public và admin; không social URL/chứng nhận NCSC giả. Rà soát source và HTTP không thay nghiệm thu giao diện đầy đủ của BA.
- Delta cuối: sửa UUID khi khôi phục lịch sử trên HTTP LAN; launcher nhận biết đúng process đang chạy ngay khi CIM thiếu State, không mở trùng/ghi đè process record.

## SQL — DBA, chưa được phép thực thi tự động

Source có **17 migration**. Shared SQL checkpoint do DBA cung cấp: `WIN-S1IBJB84OOO/website_tin_tuc`, 31 bảng/2 migration; chưa truy vấn lại trong lượt bàn giao.

1. Khi backup được phép, dùng packet riêng `shared-sql-207-resume` SHA `664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7`. Fresh Inspect/prefix/history/transaction/backup phải PASS. Packet chỉ tiếp tục **14 migration cũ**, từ sau `20261012083717_CmsRevisionWorkflow` đến `20261027025057_AuditRecoveryLookup`; không mở rộng packet sang FR-06.
2. Lệnh Inspect của packet (DBA chạy khi được phép, credential được hỏi an toàn):
   ```powershell
   powershell -NoProfile -File scripts/recover-shared-sql-207.ps1 -PacketDirectory <PACKET> -Mode Inspect -ManifestSha256 664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7 -TemporaryCertificateTrust -ReportFile <NEW_PRIVATE_JSONL>
   ```
   Chỉ sau phê duyệt scope và backup hợp lệ: cùng lệnh đổi `-Mode Apply`, thêm `-ConfirmTarget WIN-S1IBJB84OOO/website_tin_tuc -BackupFile <VERIFIED_SERVER_BAK>`. Không chạy thẳng schema.sqlcmd, không DROP/RESTORE/history cleanup.
3. Sau đối soát 70 bảng/16 migration, chuẩn bị packet delta riêng `20261102042625_DraftConsultationEmail` (hai cột nullable, vẫn 70 bảng/17 migration). Cần fresh state, backup hợp lệ, exact approval mới; packet cũ không bao phủ. Có thể sinh SQL **offline** từ tools/MigrationCli `schema-script --from-migration 20261027025057_AuditRecoveryLookup --to-migration 20261102042625_DraftConsultationEmail --output <SOURCE>/.local/publish/<NEW>/fr06.sql`; đây không phải lệnh Apply.
4. Runtime role `LandPortalRuntime` và mapping login ứng dụng là bước DBA riêng theo `docs/shared-sql-iis.md`; không dùng `sa`, không cấp db_owner cho Web/Worker. Ngoại lệ TLS đã duyệt cho công cụ shared SQL không tự nới mặc định TLS của UAT.

Backup mới đang tạm hoãn; giữ guard, không chạy DDL. LAN giữ binary tương thích trước migration email, hết hạn 09/11/2026 10:55:08 VN, SMTP Disabled. Không copy Identity/dữ liệu LAN sang server.

## Bàn giao ngoài Git và điều kiện đưa vào vận hành

- Developer: gói Publish, packet recovery nguyên hash, package UAT Data Handoff đã kiểm kê + manifest + media/provenance riêng qua kênh an toàn. 33 Ready /252 PendingReview /29 Excluded là checkpoint gói trước, không phải kiểm kê mới hoặc dữ liệu đã nhập đích. Không nhập Excluded, người dân, Identity/secret/audit Dev; scanner đích phải xác nhận đúng byte.
- TTDL/DBA: máy IIS, SQL/schema/role, hostname HTTPS, private home/kho media/keys/backup, danh tính dịch vụ/ACL, Defender policy/pin, FFmpeg/ffprobe được phép, SMTP chính thức. File `.env` thật chưa có đủ thông tin môi trường đích; không dùng cấu hình fixture Publish để chạy server.
- BA: FR-10 E2E; FR-06 sau schema; UAT nghiệp vụ toàn bộ. Lịch Word còn cần hai tuần nghiệp vụ khác; không công bố lịch TTDL thành lịch lãnh đạo Cục. Xác nhận thông tin footer/Dịch vụ công và dữ liệu thống kê chính thức.
- Trước SMTP thật: giữ Mail Disabled/DeliveryEnabled=false; kiểm kê ba thư thử Pending theo ID/event/receipt, người có quyền quyết định xử lý riêng. Không retry/gửi mù hoặc chuyển outbox LAN lên Production.
- NukeViet cần nguồn public truy cập được hoặc export chính thức và mapping duyệt. QLDD còn ngoại lệ file/coverage và media cần quét; không đồng nghĩa chuyển đủ nguồn.
- Nghiệm thu UAT/ATTT, restore đúng bản đích, giám sát SQL timeout, cutover/DNS/rollback do TTDL và chủ dự án duyệt. Thiếu các điều kiện này thì chưa PRODUCTION READY.

Kết luận: source và quy trình Publish bàn giao được khi kiểm tra cuối/push xác nhận; **chưa đủ bằng chứng chốt CODE COMPLETE toàn SRS**, không che các mục BA/SQL chưa nghiệm thu bằng PASS build.
