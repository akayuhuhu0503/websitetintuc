# LAN FR-06 và typography — 05/11/2026

LAN đã nâng đúng `LandPortal_LanDemo` trên SQL Express local từ 15 lên 17 migration bằng `LanDemoSetup upgrade-fr06-schema`. Không chạy lại: `20261027025057_AuditRecoveryLookup` và `20261102042625_DraftConsultationEmail` đã APPLIED/VERIFIED. Hai cột email nullable đã có; 69 bảng nghiệp vụ đối soát dữ liệu cũ 0 mismatch. Không bootstrap, import, reset, GRANT hoặc sửa SQL dùng chung/SQL nguồn Giấy hủy.

## Công cụ nâng cấp

Command mới tách khỏi `upgrade-statistics-schema` cũ. Dùng binary **không** có `LandPortalLanSchema15`; cấu hình private hiện hành qua `LAN_DEMO_HOME` và `LAN_DEMO_CONFIG`, chỉ chấp nhận config bên trong home LAN. `Inspect` xác minh server/database, sysadmin local, transaction, exact prefix và cột; 17/17 là no-op, không tạo backup.

```powershell
$env:LAN_DEMO_HOME='<private LAN home>'
$env:LAN_DEMO_CONFIG='<active private JSON inside LAN home>'
$env:DOTNET_ENVIRONMENT='Development'
$env:ASPNETCORE_ENVIRONMENT='Development'
rtk proxy dotnet '<LanDemoSetup.dll>' upgrade-fr06-schema --mode Inspect --report-root '<new .local report directory>'
```

Chỉ Apply khi prefix chính xác 15/17 và có phê duyệt. Phải dừng Web/candidate bằng launcher; tạo snapshot bằng `scripts/backup-file-snapshot.py` (media/snapshots/keys, active config, pointer cũ, binary cũ), kiểm tra hash và danh mục đầy đủ. Backup root riêng dưới `.local/backups`, ACL operator/SYSTEM; thư mục con `sql` cho đúng SQL service ghi/đọc backup, không cấp quyền thư mục private files. Còn process giữ cổng hoặc transaction khác thì dừng. Không thay mật khẩu/quyền SQL để vượt guard.

```powershell
rtk proxy dotnet '<LanDemoSetup.dll>' upgrade-fr06-schema --mode Apply --report-root '<new .local report directory>' --backup-root '<existing unique .local/backups restore point>' --file-manifest '<verified file-manifest.json>'
```

Công cụ tạo đúng một SQL COPY_ONLY/CHECKSUM, HEADERONLY/VERIFYONLY, hash, receipt gắn history15/server/database. Backup có sẵn sẽ chặn, không ghi đè/tạo lặp. Sau đó fresh history/digest/quiescence, EF migration tới đúng migration17, readback 2 cột/index và digest cũ. Lỗi giữa DDL phải reconcile; không blind retry, DROP, xóa history hoặc restore tự động. Không coi VERIFYONLY là đã restore SQL thực tế.

Restore point thực tế: `.local/backups/fr06-fonts-20261105T031748Z-2e9aba/`; receipt tại `.local/fr06-fonts-20261105T031748Z-2e9aba/apply/`. Bản sao 789 tệp đã kiểm tra hash; SQL restore **NOT RUN**. Giữ kín toàn bộ cấu hình, keys, backup và receipt, không gửi Git/ZIP công khai.

LAN hiện chạy Web chuẩn schema17, không dùng binary compatibility15 nữa. Không rollback binary15 lên schema17 (guard sẽ chặn). Nếu bản mới lỗi, dừng và đối soát; chọn binary schema17 tương thích hoặc trình phương án phục hồi SQL/media theo đúng restore point, không tự restore đè dữ liệu phát sinh.

## Font và phạm vi

`font-faces.css` + `typography.css` dùng chung hai layout public/admin. Roboto: giao diện, form, bảng, nội dung bài, CMS/editor; Merriweather700: H1 bài loại Tin tức và tiêu đề lead trên desktop. Không áp font serif cho văn bản, bảng, menu, form hoặc admin. Màu và bố cục giữ nguyên. CSS chỉ đổi trình bày, không sửa HTML đã lưu; ngoại lệ `!important` duy nhất trong file mới dành cho font-family đã dán inline trong body/editor.

Sáu WOFF2 gốc từ Google Fonts, tổng185.068 byte, chia Vietnamese/Latin/Latin Extended; Roboto400/500/600/700/800 dùng chung byte theo subset, Merriweather700. Font-display swap, không CDN trình duyệt; license OFL và URL/hash nằm trong `wwwroot/fonts/` và THIRD_PARTY_NOTICES.

## Kiểm tra và việc BA còn cần làm

Build Web schema17/công cụ PASS. Exact migration/readback/digest PASS; home/login/tin/chuyên mục/văn bản/dự thảo HTTP200. GCN đọc nguồn thật, Roboto14px/8cột/default3row, desktop gần hết chiều ngang, năm không wrap; mobile cuộn bảng, không tràn. Home ở390/768/1366/1920 không tràn; font tiếng Việt tải local, không font error. Launcher xác minh PID đang sống + đúng binary + HTTP; bỏ qua PID đã kết thúc còn dấu TCP đóng, không tạo process trùng khi LAN khỏe.

**CMS FR-06 E2E CHỜ BA ĐĂNG NHẬP:** không có session BA hợp lệ; không reset hoặc thử credential cũ. Writer tự mở `/admin/create`, chọn Văn bản dự thảo, nhập email nhận góp ý hợp lệ, lưu nháp và preview; gửi duyệt đúng revision. Reviewer duyệt/xuất bản khi nội dung nghiệp vụ đủ điều kiện. Kiểm tra email bản công khai; sửa email ở nháp mới phải giữ email công khai cũ, và lịch sử revision phải giữ đúng từng email. Không công bố bài thử hoặc gửi email thật chỉ để hoàn thành kiểm tra. SMTP/Worker LAN vẫn tắt. Không gọi HTTP200/build là PASS workflow có xác thực hoặc nghiệm thu UAT/Production.
