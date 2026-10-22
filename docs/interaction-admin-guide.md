# Quản trị tương tác — delta 2C-Finish, 15/10/2026

Toàn dự án **CHƯA PRODUCTION READY**; 2C/2C-Finish overall vẫn **PARTIAL**. Mốc 2C-SCAN đã nghiệm thu engine Microsoft Defender thật trong Test: Clean/EICAR, ACL tải và worker. Xem [báo cáo scanner](phase-2c-scanner-report.md) và [runbook hiện hành](scanner-operations.md). Provider Test chỉ nhận hash fixture vô danh hoặc tài nguyên nguồn công khai đã xác minh; tệp công dân và Production còn cần hạ tầng/policy được duyệt. Bốn luồng 2C, snapshot kiểm duyệt và riêng tư được giữ. Thử nghiệp vụ chỉ SQL Test/kho Test/mail sink. Không tự grant Dev; Dev hiện Mail Disabled, form không bypass OTP.

Hỏi đáp `/admin/tuong-tac?kind=Inquiry`: lọc SQL trạng thái/lĩnh vực/hạn sắp đến (2 ngày lịch)/quá hạn; nhãn tạm tính luôn hiện. Quyền inquiry.list chỉ metadata danh sách; inquiry.read là PII (giữ tương thích quyền cũ, đồng thời đọc được danh sách). Đọc chi tiết cần inquiry.read và phạm vi hiện tại; người phụ trách đọc hồ sơ được giao, supervisor xem theo quyền, triage có inquiry.assign xem hàng chờ. Không mở PII chỉ nhờ tên vai trò.

Phân công/chuyển trả tại chi tiết: chọn cán bộ active có inquiry.read + inquiry.respond trong roster, ghi lý do, lưu với rowversion/command. Người cũ mất phạm vi hồ sơ khi chuyển (trừ supervisor có quyền riêng); thư trả lời còn xếp hàng cũng kiểm lại người yêu cầu/phạm vi trước gửi. Không gửi thư chỉ vì phân công nội bộ. Lịch sử lý do phân công/gia hạn cho supervisor/triage, không hiển thị cho khách tra cứu. Roster hiện giới hạn 100 ứng viên, cần paging/search khi số cán bộ lớn; server luôn xác minh người nhận.

Tệp private một PDF/JPEG/PNG, 10 MiB, nhận cùng form và OTP. Quyền tải thêm inquiry.file.read; không có scanner Clean thật thì 423, không cho khách tải hoặc xem trong lookup. Scanner/health/GC/retry xem [runbook](inquiry-scanner-runbook.md). Không dùng cờ upload Development mở attachment chưa scan.

Khách tra cứu `/tra-cuu-ho-so`: mã tiếp nhận + email + OTP mới INQUIRY_LOOKUP qua Test sink. OTP 5 phút/5 lần sai, resend tối thiểu 60 giây, 5/email/giờ và 20/phiên/giờ; cookie phiên tra cứu 15 phút, gắn đúng trình duyệt. Không tra cứu bằng mã đơn thuần; phản hồi trước xác minh trung lập. Chỉ trạng thái/hạn/câu trả lời được chia sẻ/lịch sử trạng thái hạn chế; không trả bản gốc/PII/phân công/lý do/file. Dev chưa mail thì báo không khả dụng, không thành công giả.

Lịch `/admin/hoi-dap/lich`: cần inquiry.calendar.manage + inquiry.supervise; tạo phiên bản mới với ngày hiệu lực VN, cuối tuần, cutoff, tính ngày nhận hoặc kế tiếp, ngoại lệ work/off. Mặc định local 10 ngày làm việc, next day, nghỉ thứ bảy/chủ nhật, 17:00; đây là **Tạm tính — chưa phải SLA chính thức**, không phải danh sách lễ pháp lý. SQL đóng băng calendar/policy/received/due; đổi lịch không sửa hồ sơ cũ. Gia hạn cần inquiry.extend + supervise, hạn tăng, lý do, rowversion và audit. Chưa có quy trình bật lịch chính thức khi chưa được nghiệp vụ xác nhận.

Excel `/admin/hoi-dap/excel`: cần inquiry.export + inquiry.read và phạm vi inbox; lọc trạng thái/lĩnh vực, tối đa 1.000 hàng, không nhận IDs do client để vượt phạm vi. Bộ lọc hạn ở inbox chưa áp dụng cho export: link xuất dùng trạng thái/lĩnh vực. Mặc định 6 cột không PII; includePii thêm 5 cột chỉ khi có inquiry.export.pii. XLSX stream/no-store, ô người nhập là inline string, không macro/formula/link ngoài; readback openpyxl gồm tiếng Việt/ký tự ngoài BMP và tiền tố công thức. Audit ai/khi nào/bộ lọc/số hàng, không toàn văn PII.

Batch ở inbox Comment/Inquiry: chọn tối đa 20 hồ sơ, biên tập tên/nội dung/câu trả lời riêng từng mục trước publish; từ chối/ẩn cần lý do. Mỗi item transaction riêng, fresh quyền/status/rowversion; kết quả Success/Forbidden/Conflict/Invalid/Failed riêng, không có thông báo “tất cả đã duyệt”. Không áp dụng Contact/Consultation. Retry giữ command theo payload, một effect/audit; có Conflict tải lại trước quyết định mới.

Quyền mới: inquiry.list, inquiry.supervise, inquiry.file.read, inquiry.export, inquiry.export.pii, inquiry.calendar.manage, inquiry.extend và outbox.reconcile (tất cả tiền tố interaction.inquiry trừ outbox). Chưa apply bất kỳ quyền Dev mới hay 10 quyền vận hành cũ. Hướng dẫn Uncertain tại [outbox](interaction-outbox-runbook.md); quyền catalog không tự mang lại quyền cho tài khoản.

Chạy Test thủ công 3 terminal: `scripts/run-interactions-test.ps1 -Finish -Component Sink|Web|Worker`, Web/Worker thêm -NoBuild sau build; web5219/sink2525. E2E tự sở hữu/dừng process bằng `scripts/test-phase2c-finish.ps1 -Region Finish` hoặc All; credentials/sink chỉ ở artifact ignored, không gửi ra chat. Dev preview: `scripts/run-dev.ps1 -Phase2CFinish -NoBuild -Port 5208`; giữ V04 5198. Mọi shell prefix rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File.

---

Bản hướng dẫn checkpoint 2C trước Finish (giữ lịch sử; phần mới phía trên có hiệu lực):

# Quản trị tương tác 2C

Đây là checkpoint PARTIAL local. Không tự cấp quyền Dev; quyền đọc riêng khác quyền đăng bài. Với quyền `interaction.<kind>.read`, mở `/admin/tuong-tac?kind=Comment|Inquiry|Contact|Consultation`. Các badge và danh sách được lọc SQL theo quyền/scope. Giao diện liên hệ/góp ý không có xuất bản.10 quyền Dev vận hành cũ vẫn chưa cấp.

1. Chọn hồ sơ, đọc original riêng tư; không sao chép email/điện thoại hoặc dữ liệu nhận diện vào trường công khai.
2. Bình luận: nhập tên hiển thị/nội dung đã biên tập. Chỉ người có comment.moderate được duyệt; thêm phản hồi cơ quan cần comment.reply. Consent thiếu sẽ bị chặn. Từ chối/ẩn phải có lý do. Original/history được giữ; reason mã hóa trong audit, history UI đang bổ sung.
3. Hỏi đáp: dùng inquiry.respond để lưu câu trả lời riêng và xếp outbox. Máy chủ thư chấp nhận khác người dân đã đọc. Công khai cần inquiry.publish + consent và bản hỏi/trả lời đã biên tập; private answer không tự được sao chép. Phân công/tệp/tra cứu/SLA/Excel chưa có ở checkpoint này.
4. Liên hệ/góp ý: cập nhật Đang xử lý/Đóng/Từ chối với quyền process riêng. Góp ý lưu đúng revision/deadline nhận tại server. Không chuyển tự động thành hỏi đáp hoặc bài.
5. Lỗi409 nghĩa có người đã xử lý; tải lại rồi xem trước khi thao tác mới. Double click/retry cùng command không nhân audit/effect; quyền vẫn được kiểm tra lại.

`/admin/tuong-tac/tiep-nhan-binh-luan` cần comment.moderate + settings.manage; cờ bài còn cần quyền sửa và phạm vi bài. Thay đổi reception có hiệu lực live, có audit/expected state/rowversion; không thay draft/published prose/category/cover. Tắt nhận mới vẫn giữ bình luận đã duyệt; gỡ bài không cho khách lấy bình luận. Không demo bằng bài nguồn Dev. Danh sách reception hiện50 bài gần nhất, chưa toàn bộ quản lý/paging.

Trạng thái thư xem trong chi tiết. Pending chưa gửi, Accepted chỉ máy chủ sink chấp nhận, Uncertain phải đối soát trước khi gửi lại. Không reset mật khẩu hoặc tự grant các quyền còn thiếu. Cấu hình chính sách dữ liệu/SLA cần PO/pháp chế; không tự đặt thời hạn pháp lý/xóa hồ sơ.
