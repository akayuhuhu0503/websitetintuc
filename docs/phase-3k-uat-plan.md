> Chuẩn hiện hành: SRS v1.2 ngày 23/10/2026. Máy chủ TTDL; đội phát triển bàn giao nguồn Git, Trưởng phòng build/cài đặt. Hạ tầng đích chưa được cấp. Video mới tải trực tiếp, không nhúng YouTube.

# Phase3K — gói chuẩn bị UAT, chưa phải biên bản UAT đã đạt

Trạng thái **PREPARED / UAT NOT RUN**. Áp dụng SRS v1.2 chính thức; không dùng kết quả kỹ thuật local làm chữ ký nghiệm thu. Tài khoản cán bộ và quyền UAT phải được đơn vị phê duyệt; không sao chép tài khoản giả lập Dev/Test, reset mật khẩu hoặc tự cấp quyền.

| Mã | Vai trò / thao tác | Điều kiện đạt | Bằng chứng UAT cần lưu | Trạng thái |
|---|---|---|---|---|
| U01 | Biên tập viên sửa nháp bài nguồn trên bản sao UAT | Public title/body/cover/category không đổi trước publish; dữ liệu nguồn không bị ghi đè | URL, revision ID/hash trước/sau, ảnh màn hình | NOT RUN |
| U02 | Hai biên tập viên cùng sửa; autosave đến muộn sau gửi duyệt | Conflict rõ ràng; không ghi đè/đưa trạng thái lùi | HTTP409/rowversion/generation/audit, bản soạn được giữ | NOT RUN |
| U03 | Biên tập viên gửi duyệt, người duyệt duyệt đúng revision | Không tự duyệt/nâng quyền; sửa tiếp làm approval cũ không dùng được | revision submitted/approved và audit, endpoint phủ định | NOT RUN |
| U04 | Người có quyền xuất bản/gỡ/khôi phục/hẹn giờ | Chỉ revision đã duyệt; worker kiểm quyền khi chạy, restart/race không đăng trùng | SQL schedule/receipt/revision/public URL/worker log | NOT RUN |
| U05 | Editor upload ảnh/PDF/Word/Excel và preview | Giới hạn format/size, private/quarantine trước Clean, ACL theo revision và quyền hiện hành | Hash, engine/proof, guest GET/HEAD/Range và thu hồi quyền | NOT RUN |
| U06 | Nhập tay/lịch DOCX, sửa preview, xuất bản/khôi phục | Không đổi public trước publish; restore tạo revision mới; chọn phiên bản cũ và tải Word gốc; gỡ lịch thu hồi cả lịch sử | Phiên bản 1/2/3, GET/HEAD/Range/byte hash; 3 Word nghiệp vụ thật khác tuần cho AC-16.1 | AC-16.2 PASS LOCAL mới; UAT NOT RUN; AC-16.1 BLOCKED: đã có1Word tuần43 TTDL; cần2Word thật khác tuần, chưa công khai thành lịch lãnh đạo Cục |
| U07 | Người dân gửi bình luận/hỏi đáp/liên hệ/góp ý | CAPTCHA đúng purpose/TTL/one-use, SQL thật, private/kiểm duyệt trước public | Receipt/SQL/audit, SMTP sink Test, private search/lookup negative | NOT RUN UAT |
| U08 | Cán bộ phân công/chuyển, SLA, Excel/batch; lọc bình luận theo bài | Quyền endpoint từng hồ sơ; lọc ContentId giữ phạm vi chuyên mục và phân trang; export chống công thức | Audit phân công, calendar version, export/batch receipt; không lộ bài ngoài phạm vi | NOT RUN UAT; lọc bài PASS LOCAL mới |
| U09 | Quản trị xử lý outbox Uncertain | Không retry mù; quyết định có quyền/bằng chứng/audit | Sink Test + delivery evidence + reconciliation audit | NOT RUN UAT |
| U10 | Biên tập viên tải video trực tiếp từ máy | Upload/scan/ffprobe/transcode/poster/Range/nháp ACL/restart đúng byte | Input/output SHA, FFmpeg version/proof/SQL lease/HTTP | NOT RUN UAT |
| U11 | Quản trị tài khoản/quyền/menu/cấu hình và nhật ký | Không tự nâng quyền, revoke có hiệu lực; draft không đổi live; private logo/banner không lộ; /admin/audit chỉ quản trị toàn hệ thống | 2 session, endpoint403, config revision/live pointers; audit không chứa credential/chi tiết hồ sơ riêng tư | NOT RUN UAT; audit/revoke PASS LOCAL mới |
| U12 | Tìm kiếm/tra cứu tiếng Việt và public inquiry | Có dấu/không dấu, SQL paging/filter; không lộ draft/PII/chưa duyệt | Query, SQL/public IDs và HTTP negative | NOT RUN UAT |
| U13 | Đối soát dữ liệu chuyển theo AC05.1–05.3 | 50 URL cũ từ cả2 nguồn,20 bài có media local, tổng đối chiếu từng loại | Danh sách chọn mẫu thật, mapping301/hash/report ngoại lệ | BLOCKED: NukeViet/coverage chưa đủ |
| U14 | Bootstrap riêng, đăng nhập CAPTCHA/MFA và thu hồi phiên | Bootstrap/Web cùng password ≥14; thiếu/weak credential không tạo quyền; CAPTCHA không thay password/MFA; password outcome có audit; challenge/replay/recovery fail-closed | Binary cuối; tài khoản do đơn vị cấp; không ghi secret/OTP | NOT RUN UAT: bootstrap/password audit PASS LOCAL mới; MFA lịch sử riêng, cần identity/policy đích |
| U15 | DBA/vận hành backup/restore/backout | SQL+media+keys+config nhất quán trên recovery DB riêng; không mất cập nhật sau backup | Restore thật mới nhất, RPO/RTO, hash/relations/ACL, biên bản | PARTIAL: actual restore250 local PASS riêng; backup mới nhất/target chưa restore thật |
| U16 | Nghiệm thu HTTPS/IIS/Worker/SMTP/scanner/NFR | Host/TLS hợp lệ, không public private files, monitor/alert/least privilege, 4G/a11y/browser/an ninh | Target staging được cấp và ATTT/QA ký nhận | BLOCKED hạ tầng đích; profile Staging/LandPortal_Uat đã có |

Mỗi buổi ghi: người thực hiện/đơn vị, thời điểm, environment/database, binary/migration/hash, case, input không chứa PII thật, expected/actual, artifact đường dẫn mới, PASS/FAIL/BLOCKED/NOT RUN và người duyệt. Không lưu mật khẩu/secret/OTP vào biên bản. Không mở tra cứu hồ sơ riêng tư khi chưa có cơ chế sở hữu được PO/ATTT duyệt.

## Điều kiện cần đơn vị vận hành cung cấp

- Target staging/Production: tên máy, OS/kiến trúc, SQL instance/database dự kiến riêng, SQL version/edition/capacity và tài khoản dịch vụ/least privilege do DBA cấp. Không dùng LandPortal_Test chứa fixture làm Production.
- IIS/site/app pool, cổng loopback backend, HTTPS binding/hostname gdla.gov.vn, chứng chỉ/renewal/expiry alerts, reverse proxy trusted hops nếu có; quyền triển khai chỉ khi được duyệt riêng.
- Media/temp/quarantine/snapshot/keys roots ngoài webroot; ACL, backup retention/offsite/encryption, dung lượng video và quotas, worker lifecycle/restart/monitor/alert.
- Scanner engine/policy PII/cloud submission/quarantine, chứng minh Clean/EICAR/failure/definitions và proof đúng byte trên đích. DefenderTest không được dùng làm provider Production.
- SMTP host/port/TLS/from/relay rules và secret qua kho bí mật của đơn vị; chỉ test sink tại local. Không gửi thư thật hoặc chép secret vào `.env`/repository.
- Người phụ trách MFA, phương thức được ATTT duyệt, quy trình enrol/recovery/revoke/hỗ trợ người dùng; tài khoản UAT và vai trò/phạm vi được phê duyệt.
- Thời gian UAT/cutover, người chấp thuận coverage/ngoại lệ, mapping NukeViet, DNS301 và quyết định rollback. Public crawl authority không cấp quyền triển khai hoặc Apply Dev mới.

## Trạng thái phần mềm hiện hành (22/10/2026)

Identity CAPTCHA + TOTP/recovery/replay/revoke đã có bằng chứng local ở delivery;
không bật/reset tài khoản Dev. U14 vẫn NOT RUN UAT đến khi cán bộ dùng tài khoản,
identity và policy MFA đích được cấp. Không dùng TwoFactorEnabled đơn lẻ làm bằng chứng.

Web/Worker có profile Staging; LandPortal_Uat chỉ là tên đề xuất chưa được cấp.
SQL TLS/host/private roots fail-closed; scanner Clamd hoặc MicrosoftDefenderOnPrem
với identity/hash/privacy policy được duyệt, SMTP STARTTLS mặc định Disabled và bootstrap riêng. Source media cũ
vẫn bị fence Development; cần policy scan/release tại đích, không mang proof Test
sang tự mở media UAT. Tra cứu theo SRS v1.2 dùng mã tiếp nhận ngẫu nhiên + email +
CAPTCHA một lần, chỉ trả trạng thái/câu trả lời, không trả PII/tệp/nội dung gốc.
U07/U13 cần kiểm tra mã sai, CAPTCHA sai/hết hạn/dùng lại, giới hạn dò tìm,
GET/HEAD/Range private và cache; không dùng OTP thay cơ chế này. Hạ tầng IIS/SQL/scanner,
relay receipt Production và cutover chưa được nghiệm thu.

Gói ops có exporter 5 Task Scheduler XML với least privilege, IgnoreNew và restart
3 lần/1 phút; chỉ xuất file, chưa đăng ký task/service hoặc cấp quyền. Cán bộ phải
kiểm crash/restart/reboot, queue leases, HTTPS và các phần U01–U16 trên target thật.
Xem scripts/uat-operator-steps.md; giữ 16 case, không cộng focused local thành UAT.

## Bổ sung kiểm tra P0 local 23/10/2026

Run `p0-ready-20261023T025552Z-35443a36`, lần cuối `final-candidate`: 3 nhóm tập trung PASS (có HTTP/SQL Test/worker Defender thật), không full regression và không chụp ảnh. Đây không phải 16 case UAT đã chạy. BR-03 ẩn block rỗng/lỗi; BR-08 đúng nhãn trả lời; U08/U11/U14 và AC-16.2 được bổ sung như trên. File bị đổi byte giữ ScanFailed/cách ly dù byte được phục hồi; không tự đổi lại Clean. SQL timeout danh sách admin Test đã tái hiện ở lần đầu; paging sau sửa đã qua HTTP, root cause vẫn UNKNOWN/MONITORING.


Cập nhật chuẩn bị SRS1.1 ngày23/10: U01/U03 kiểm cả sapo/cover/author/body, chú thích, tag, tin nổi bật đúng revision; U05 kiểm PDF inline và counter GET/HEAD/Range; U11 kiểm tên Anh/chân trang nháp-live, menu FR17 desktop/mobile, slider; U12 kiểm đồng hồ Việt Nam và thống kê phiên SQL không IP/email/tài khoản. U16 thực hiện trên máy chủ TTDL, không dùng provider DefenderTest; MicrosoftDefenderOnPrem cần identity/pin/privacy policy được duyệt và engine E2E tại đích. Các bổ sung vẫn NOT RUN UAT; kết quả local không thay chữ ký UAT.
