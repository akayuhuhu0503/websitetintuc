# Hoàn thiện P0 và ứng viên UAT — 23/10/2026

**Phạm vi sửa dưới đây PASS LOCAL; toàn hệ thống còn OPEN_CODE/BLOCKED, chưa READY FOR UAT toàn bộ và CHƯA PRODUCTION READY.** Run `p0-ready-20261023T025552Z-35443a36`, pointer `.local/p0-ready-current.json`. Không lập lại hồ sơ `bao-cao-tien-do`, không chụp ảnh, không full regression, crawl, migration, Apply hay cấp quyền Dev.

## P0 đóng thêm trong phạm vi local

| Tiêu chí | Thay đổi và bằng chứng mới |
|---|---|
| FR-02 / BR-03 | Block rỗng/lỗi được ẩn, kể cả Featured; correlation chỉ giữ trong log vận hành, không trả dữ liệu fallback. Kiểm tra failed block chứa stale card vẫn bị loại. |
| FR-04 / BR-08 | Đúng nhãn “Trả lời của Cục Quản lý đất đai”; HTTP kiểm tra nội dung/tên/câu trả lời tiếp tục encode, không thực thi script. |
| FR-12, lọc theo bài | Inbox Comment nhận ContentId sau permission/category scope, giữ bộ lọc qua SQL paging 20+1; GUID sai/trạng thái sai trả400; không lộ hồ sơ ngoài phạm vi. |
| FR-16 / AC-16.2 | Bạn đọc chọn phiên bản đã xuất bản, tải Word gốc đúng phiên bản/byte; nháp không xuất hiện. Restore tạo v3 mới. Gỡ lịch thu hồi lịch sử và GET/HEAD/Range của Word. Tamper chuyển ScanFailed/cách ly và không tự trở lại Clean khi phục hồi byte. AC-16.1 vẫn BLOCKED thiếu3 Word nghiệp vụ, không lấy fixture làm UAT. |
| FR-11, audit | Ghi kết quả password/lockout/MFA-required bằng mã actor, không ghi mật khẩu/CAPTCHA/tên nhập/IP. `/admin/audit` chỉ quản trị có cả users.manage + roles.manage và scope toàn hệ thống; chỉ SELECT metadata, không SELECT Detail riêng tư. Thu hồi quyền trên cùng cookie trả403; phân trang/lọc và ngày sai được kiểm tra. |
| An toàn bootstrap UAT | CLI và Web chung policy password≥14, lockout5/15 phút. Thiếu/weak credential bị chặn trước tạo role/user; không tạo/reset tài khoản hoặc cấp quyền Dev trong lượt này. |

Danh sách admin đổi sang sort/page ID rồi materialize trong cùng scope và giữ đúng thứ tự. HTTP kiểm tra hai trang15, lọc tiêu đề, phạm vi chuyên mục đạt. **Không kết luận đã chữa nguyên nhân SQL.** Lần đầu Test `/admin` thực sự timeout SQL-2/30s; raw log còn nguyên. Sau sửa không quan sát lại ở focused run, root vẫn **UNKNOWN/MONITORING**, không tăng timeout/index/pooling/settings.

## Kiểm chứng và bảo toàn

- Code cuối: build Tests/Web/Worker/CLI **0 warning,0 error**. `checked-boundaries/p0-tests.json`: **3/3 nhóm PASS**, không cộng các lần trước hoặc6 subreport HTTP thành full suite. HTTP Test có CAPTCHA/Identity thật, inbox/audit, gửi/duyệt/đăng đúng revision và Word parser/worker/restart/ACL thực.
- `native-word-proof-readback.json`:3 native Clean +1 EICAR Infected, engine1.1.26090.9/definitions1.461.156.0; sau cố ý tamper một Clean trở thành ScanFailed/cách ly. Đây là fixture kỹ thuật Test mới, không phải dữ liệu nghiệp vụ hoặc proof cho53 nguồn Dev.
- Lưu các lỗi trước: timeout lần đầu; harness thiếu Worker.dll lần hai; kiểm thử tamper quá sớm khiến bước đọc tiếp theo bị chặn đúng policy lần ba. Đã sửa harness/thứ tự kiểm thử; không bỏ guard. Lần cuối nêu trên PASS.
- Release public HTTP **11/11 PASS**, có hash ảnh đúng byte; không login/form POST vào Dev. Host5396 tạm đã dừng.17 offline ops-preparation checks PASS riêng; không IIS/SQL UAT execution. Không ảnh mới/full regression.
- Dev **264 nguồn/275 revision/615 media(372ảnh,243PDF)/528alias**,53 original NotScanned;70 capture/68 SQL snapshot,8 ngoại lệ,2 video metadata/0 file. **17 nhóm0diff**,615 media/hash/snapshot và Identity/.env giữ nguyên, migration vẫn `20261015091551_V05EditorialDocuments`.
- **0 nội dung nguồn mới nhập Test/Dev.** Test readback4837 Contents/6883 Revisions/1775 media gồm fixture; SourceRecords545/alias821/snapshot2429 giữ số cũ, không coi đó là số chuyển nguồn. So checkpoint dossier: +13 content/+21 revision/+11 tệp kỹ thuật qua các lần test, không đếm vào migration.14 nội dung thật pending Test theo checkpoint trước không được Apply Dev.
- Test SQL backup riêng mới COPY_ONLY/CHECKSUM/VERIFYONLY trước fixture, receipt trong run; SQL restore bản này NOT RUN. Không cần rollback Dev vì0 ghi dữ liệu Dev.

## UAT kit

Ứng viên mới: `.local/publish/p0-ready-20261023T025552Z-35443a36-final`, **319file**, manifestSHA `50e07d37d9286b7813af13a4cdd10ef9db2745a58187f363d31f3365dff13ced`.

ZIP `.local/publish/p0-ready-20261023T025552Z-35443a36-UAT.zip`,392390909byte; SHA `3be17ed7861ce14ae4470df619892fff9ac06061cd7a5d5091722be9a7840756`. ZIP readback0hashdiff. Có Web/5worker/CLI/schema offline/config mẫu/ops/16UAT; không `.env`, database, fixture, credential, private key hoặc FFmpeg binary. Không chạy thêm task/service/IIS.

Các lệnh sẵn có: `scripts/package-uat.ps1 -RunId <unique>`; trên target được cấp dùng `ops/prepare-uat-target.ps1`, `ops/uat-runtime.ps1 -Mode Preflight -Online`, `ops/install-uat-iis.ps1` (mặc định plan-only), `ops/export-uat-worker-tasks.ps1`. Tham số thật và thứ tự trong kit `ops/START.md`. Không chạy target placeholders hoặc schema unbound.16case vẫn PREPARED/UAT NOT RUN, local PASS không thay chữ ký nghiệm thu.

Bản Release mới đang chạy local tại **http://127.0.0.1:5188/**, admin `/admin`, chỉ loopback/Mail Disabled/no unscanned-upload bypass. Đã xác minh PID/binary của host cũ trước thay; health/home và17nhóm/Identity sau khởi động vẫn0diff. Không phải link dùng chung/UAT. Helper xác minh ban đầu vướng biến HOME chỉ đọc, đã sửa tên biến và kiểm chứng host đang chạy, không restart mù hoặc sửa biến hệ thống; log/ghi chú lần lỗi được giữ.

## Phần còn thiếu và đầu vào cần cấp

| Loại | Điều kiện thực tế |
|---|---|
| OPEN_CODE P0 | FR-10 chưa đủ validator sapo/cover/author khi gửi/đăng, tag/ghim trong mẫu; FR-06 chưa PDF inline/counter; header/footer/menu/slider/traffic metrics còn delta. Không gọi các mục này là đã xong chỉ vì core2A/2B PASS. |
| Cấu hình/dữ liệu P0 | FR-17 cần đồng bộ catalog/menu bằng luồng có quyền, chưa công bố lại qua SQL trực tiếp. Tên nhóm thứ hai **đã chốt SRSv1.0**, không hỏi PO duyệt lại tên. Nội dung TTDL, logo/tên Anh/footer/giấy phép chính thức và DVC URL còn thiếu; DVC **CHỜ TRƯỞNG PHÒNG XÁC NHẬN**. |
| Quyền riêng tư | Private lookup tiếp tục khóa chờ PO/ATTT chấp thuận cơ chế sở hữu; CAPTCHA không thay xác minh danh tính. |
| Chuyển dữ liệu |264/295canonical đã biết ở Dev;31chưaDev=14Test+17held,54typedpending(51thiếusnapshot+3source/parser), tổng nguồn UNKNOWN. Batch9 cbd41632 vẫn UNSIGNED/UNAPPLIED, proof143 stale so156; cần scoped refresh +manifest mới +exact approval, không dùng approval batch cũ.5intro Test cần catalog60 authority. Nuke22snapshot/21asset/22private draft,0CMS import: mapping/HTTP-source acceptance/2asset và adapter nhập còn thiếu.53 gốc không tự scan/promote. |
| BLOCKED hạ tầng | Chưa được cấp máy IIS/SQL/database UAT/HTTPS/private stores/service identities/Clamd/SMTP. LandPortal_Uat chỉ tên đề xuất. Source-media release và trusted SMTP receipt ở target cần tích hợp/policy thật; cấu hình engine đơn thuần không mở fence Test. |
| Nghiệm thu | MFA/service identity/scanner Clean-EICAR-failure/SMTP/restore hiện trạng264/ATTT/multibrowser/UAT16/cutover cần môi trường và người ký. Restore250 lịch sử không thay restore hiện trạng. Raw artifact incident lịch sử giữ PARTIAL; SQL root UNKNOWN/MONITORING. |

Ngắn nhất: đóng các OPEN_CODE còn lại trên Test trong lúc CNTT cấp target và Trưởng phòng cấp thông tin chính thức; chuẩn bị fresh exact batch9 để chủ dự án duyệt riêng. Không tự deploy/DNS, Apply, grants hoặc bật private lookup để rút ngắn danh sách.

File sửa: `AdminController`, `InteractionAdminController`, `MediaController`, `WeeklyScheduleController`, `AuditController` mới; `WeeklySchedulePublication`, `PortalIdentityPolicy` mới; `BootstrapIdentity`, Web/CLI Program; models/views inbox/audit/lịch tuần/public label/home/admin layout; `PublicSiteService`; focused tests và ops/UAT16. **0migration.** Pointer/run giữ lệnh, log, hash và các thất bại thật; hồ sơ tiến độ đã gửi giữ nguyên.
