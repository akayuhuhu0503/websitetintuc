# Nhập 43 lịch tuần từ máy Developer qua CMS HTTPS

Phương án hiện hành của PR #17. **Không chuyển công cụ sang IIS, không đọc cấu hình private của website, không kết nối SQL trực tiếp.** CLI dùng các form/API Admin đã có; không cần thay website để thêm API. Các chế độ CLI SQL/IIS cũ vẫn giữ để dùng khi được cấp phép riêng.

**Bổ sung ngày đăng:** ngày chỉ lấy từ cột B. `DateReport` đọc Excel offline, dùng `date-decisions.json` private cho 17 ô số đã được chủ dữ liệu xác nhận; 26 ngày text đọc theo dd/mm/yyyy. Công cụ tạo nháp với `Metadata.PublicationDate` đã xác minh. Với 43 hồ sơ đã nhập, không nhập lại: dùng `DatePlan → SyncDates → cán bộ duyệt/công bố → VerifyDates → SyncLinks`. Xem [hướng dẫn chuẩn hóa 43 ngày](weekly-schedule-dates.md).

## Điều kiện thực tế

- HTTPS `https://gdla.gov.vn/` có chứng chỉ hợp lệ và Admin hoạt động. Không tắt kiểm tra TLS hoặc sử dụng token bỏ qua đăng nhập.
- Người chạy có tài khoản CMS hiện hành: tạo bài, xem/sửa bài của mình, quản lý media, được truy cập chuyên mục **76**. Để đối soát hồ sơ có sẵn của người khác, cần quyền xem toàn bộ phạm vi lịch tuần tương ứng; CLI không tự cấp quyền và không thể phát hiện bản nháp bị CMS che khỏi tài khoản. Không chạy nhiều bộ nhập hoặc nhiều người nhập cùng gói đồng thời.
- Chuyên mục 76 có trong lựa chọn CMS. PDF phải được CMS chấp nhận theo chính sách scanner/no-AV hiện hành và IIS phải ghi/đọc được storage. Tiền kiểm chỉ đọc xác nhận form/quyền; **nhập thử mới chứng minh được lưu và tải lại PDF**. Thiếu quyền/chính sách/disk thì dừng, không bật ngoại lệ hay sửa IIS.
- Mỗi lần chạy nhập tên tài khoản, mật khẩu kín và trả lời CAPTCHA hiện trên console. Mật khẩu/cookie/CSRF không lưu vào file hoặc tham số. Sai đăng nhập, yêu cầu đổi mật khẩu/MFA, hết phiên: dừng; xử lý bằng CMS, không thử liên tục. Không dùng tài khoản cũ đã MISMATCH.

## Chạy trên máy Developer

Nhận nguyên thư mục CLI đã publish và giữ ZIP tại `C:\website_upload\website_upload.zip`. Bản CLI Windows self-contained không cần cài thêm .NET. Mở PowerShell tại thư mục CLI:

```powershell
# 1. Chỉ đọc CMS; không tạo hồ sơ hoặc upload PDF.
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action Preflight
# 2. Chỉ đọc CMS; tạo bản sao nguồn, kiểm kê và kế hoạch trên máy Developer.
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action DryRun
# 3. CHỈ sau khi được cho phép nhập thật: tạo một bản nháp + một PDF.
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action Pilot
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action Reconcile
# 4. Sau khi hồ sơ thử đúng và có quyền thực thi đủ lô:
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action ImportAll
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action Reconcile
# 5. Sau khi cán bộ duyệt/xuất bản qua CMS, chạy nhiều lần nếu cần:
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action SyncLinks
```

Mặc định kết quả tại `%USERPROFILE%\LandPortal-Private\weekly43-web`. Có thể đặt `-SourceZip` và `-OutputDirectory` một lần, **giữ cùng đường dẫn/tài khoản/host/ZIP ở tất cả lệnh sau**. DryRun không ghi đè thư mục cũ. Không xóa nhật ký khi mất kết nối; không đổi gói để tránh kiểm tra.

`Pilot` chọn STT đầu tiên cần tạo trong plan, không nhất thiết STT 1 nếu đã có hồ sơ hợp lệ. `ImportAll` bị chặn khi còn bài cần tạo nhưng chưa có một hồ sơ do chính công cụ tạo, upload và đối soát thành công. Hồ sơ sẵn có chỉ được tái sử dụng khi tên, StaticPage, chuyên mục và byte/SHA256 PDF khớp; xung đột dừng để rà soát.

## Resume và phạm vi ghi

- WEB mode không nhận `--config`, `--iis-web`, mật khẩu hay cookie qua tham số. Không gọi workflow gửi duyệt, duyệt, xuất bản; nhập mới dùng `/admin/save?format=json` với `intent=Save` và `/admin/documents/{id}?format=json`. Đồng bộ ngày được phép gọi `BeginEdit` cho hồ sơ Published trước khi lưu bản sửa, không upload lại PDF.
- `web-plan.json` gắn ZIP/Excel SHA256, origin, tài khoản và STT. `web-journal.json` ghi ý định **trước** từng POST, ContentId/MediaId, trạng thái và phiên bản. Lệnh tạo/gắn dùng RequestId ổn định của CMS; cùng RequestId không thể được dùng bởi người khác hoặc payload khác.
- Mất phản hồi tạo bài/gắn PDF: lần chạy sau gửi lại đúng lệnh cũ, gồm row version cũ. Mất phản hồi upload: truy vấn `/admin/documents/{id}/result?sha256=...` trước khi gửi file. Không tự retry mạng bên trong một lần chạy. Nếu bản nháp đã đổi hoặc PDF bị cán bộ gỡ, dừng; không gắn lại.
- `Reconcile` chỉ GET các hồ sơ đã Verified, tải PDF và so SHA256. Các thao tác Creating/Uploading/Attaching còn dở được báo riêng, không giả PASS; dùng `Pilot`/`ImportAll` cùng nhật ký để tiếp tục. Khóa file ngăn hai tiến trình cùng sử dụng một thư mục kết quả.
- Không tạo backup SQL bằng WEB mode; lưu bản sao ZIP/Excel và journal để đối soát, giữ audit/revision CMS. Không sửa hồ sơ sẵn có, không rollback/xóa dữ liệu tự động.
- PDF giữ nguyên byte, tên gốc, hash; trạng thái an toàn do CMS quyết định, không giả `Clean`. Không đặt ngày nghi vấn thành ngày công bố, không tạo `WeeklyScheduleRelease` hoặc thay lịch tuần đang phát hành.

## Excel và rà soát

Nguồn 43 PDF: **120 trang**; **STT 42 / E48: 2 → 3**. Giữ các ngày số Excel (17 ô), toàn bộ A/B/C, công thức, định dạng, các sheet khác. Chỉ sửa số trang sai E7:E49 và D7:D49 trong **bản sao**.

STT **39–42** có lệch tuần giữa Excel/PDF; giữ nguyên tiêu đề cột C, chỉ nhập nháp, cảnh báo nằm trong `inventory.json`. Cán bộ rà soát trước khi duyệt. Công cụ không tự sửa nguồn hay gửi duyệt.

SyncLinks đọc lịch sử CMS để xác định đúng revision có nhãn Công khai, xem trước chính revision đó, rồi dùng HTTP **không cookie đăng nhập** xác minh tiêu đề, liên kết PDF và byte/hash PDF. Đọc lại trạng thái trước khi ghi Excel. Bài nháp/thu hồi, PDF lỗi hoặc sai revision: ô D để trống. Tệp kết quả mới `LichCT-links-<timestamp>.xlsx` có hyperlink thật, không ghi đè workbook nguồn. Đây là xác minh tại thời điểm chạy, không phải cam kết URL không bao giờ thay đổi.

## Build / kiểm tra dành cho Developer

```powershell
dotnet publish tools/WeeklyScheduleImport/WeeklyScheduleImport.csproj -c Release -r win-x64 --self-contained true -o .local/handoff/WeeklyScheduleImport-Web
powershell -NoProfile -File scripts/test-weekly-web.ps1 -SourceZip C:\website_upload\website_upload.zip
```

Test dùng một PDF nguồn, tài khoản giả, HTTP Kestrel/controllers thật và instance SQL LocalDB mới schema 18; không tải gói lên gdla.gov.vn. Tự giải CAPTCHA chỉ nằm trong fixture loopback, CLI thực yêu cầu con người trả lời. Hạ tầng bị dừng sau test. Không chạy lại bộ 43-import SQL/IIS/full E2E cũ.

Kết quả 17/11/2026: **Release build/publish Windows x64 PASS; 32 kiểm tra HTTP/SQL tập trung PASS** (run `weekly-web-20261117-final`). Một hồ sơ được tạo qua HTTP; mô phỏng mất phản hồi sau commit tạo nháp, upload và gắn PDF đều phục hồi không trùng. Kiểm tra thu hồi, thùng rác, tệp bị gỡ, sai hash, thiếu quyền và giới hạn HTTPS đều đạt. Fixture duyệt/công bố chỉ dùng tài khoản giả để kiểm SyncLinks, không nằm trong CLI nhập.

Đối chiếu XML workbook độc lập PASS: A/B/C của đủ 43 dòng giữ nguyên, E48=3, D7 có hyperlink sau khi fixture xuất bản; các phần workbook khác giữ nguyên byte. CLI self-contained chạy được và từ chối tham số cấu hình SQL/private trong WEB mode. **Chưa đăng nhập hoặc nhập dữ liệu thật trên gdla.gov.vn; chưa chứng minh upload của tài khoản vận hành trên IIS thật.** Khi được phép, chạy Pilot trước ImportAll; không coi kết quả cô lập là PASS vận hành.
