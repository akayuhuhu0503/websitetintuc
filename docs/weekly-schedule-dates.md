# Ngày đăng từ Excel — cập nhật qua CMS HTTPS

Lỗi của công cụ WEB IMPORT cũ: payload tạo nháp không có `Metadata.PublicationDate`. Vì vậy trang công khai dùng ngày công bố thật làm ngày hiển thị. Bản sửa chỉ thay công cụ Developer, dùng trường và endpoint CMS đã có; không cần cập nhật IIS, SQL hoặc upload lại PDF.

## Đối chiếu nguồn

Đã đọc đủ 43 dòng B7:B49, đếm lại 43 PDF / 120 trang. **24 ngày text rõ; 19 dòng chờ xác nhận**: 17 ô số Excel và hai dòng text 40–41 còn xung đột tuần/PDF. STT 39–42 vẫn cần cán bộ rà soát tiêu đề/PDF trước duyệt. E48 vẫn sửa 2→3 trên bản sao Excel. A/B/C và ZIP nguồn không đổi.

Tất cả 17 ô số dùng number format tích hợp 14, phụ thuộc locale. Báo cáo liệt kê raw serial, định dạng và cả diễn giải vi-VN/en-US; không thể khẳng định máy lập Excel đã hiển thị theo locale nào. Ứng viên đảo ngày/tháng chỉ là đề xuất, **không tự chọn theo ngày bắt đầu tuần**.

| STT | Ngày giải mã B (dd/mm/2026) | Đề xuất cần xác nhận (dd/mm/2026) |
|---|---|---|
| 1 | 01/05 | 05/01 |
| 2 | 01/12 | 12/01 |
| 5 | 02/02 | 02/02 |
| 6 | 02/09 | 09/02 |
| 8 | 03/02 | 02/03 |
| 9 | 03/09 | 09/03 |
| 17 | 05/04 | 04/05 |
| 18 | 05/11 | 11/05 |
| 21 | 06/01 | 01/06 |
| 22 | 06/08 | 08/06 |
| 26 | 07/06 | 06/07 |
| 30 | 08/03 | 03/08 |
| 31 | 08/10 | 10/08 |
| 35 | 09/07 | 07/09 |
| 39 | 10/12 | 12/10 |
| 40 | 19/10 (text) | 19/10; còn lệch tuần PDF |
| 41 | 26/10 (text) | 26/10; còn lệch tuần PDF |
| 42 | 11/02 | 02/11; còn lệch tuần PDF |
| 43 | 11/04 | 04/11; không tự đổi thành 02/11 theo tiêu đề |

## Chạy trên máy Developer

Mở PowerShell tại **thư mục CLI mới**, giữ nguyên thư mục kết quả cũ `%USERPROFILE%\LandPortal-Private\weekly43-web` có `web-plan.json`, `web-journal.json`, `source-backup.zip` và `original.xlsx`. Không chạy lại DryRun/Pilot/ImportAll cho 43 hồ sơ đã Verified.

```powershell
# 1. Offline, không đăng nhập: xuất báo cáo 43 dòng và mẫu xác nhận ngày.
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action DateReport
```

Đọc `DOI-CHIEU-NGAY-*.md` trong thư mục kết quả. Sau xác nhận thực tế của chủ dữ liệu, điền **chỉ các STT đã xác nhận** vào `date-decisions.json`: `ConfirmedDate` dạng `yyyy-MM-dd`, `ConfirmedBy` là người đã xác nhận. Giữ hai hash nguồn. Dòng chưa xác nhận để `null`; công cụ bỏ qua, không ghi ngày mơ hồ. Mẫu được tạo trống, không giả xác nhận. Ngày text hợp lệ không có cảnh báo được đọc trực tiếp từ B, không cần xác nhận lặp.

```powershell
# 2. Chỉ đọc CMS, đối chiếu ContentId/tiêu đề/category76/revision/PDF/hash và metadata.
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action DatePlan
# 3. Khi có quyền thực thi: chỉ lưu ngày đã xác nhận thành bản nháp.
# Có thể giới hạn một hồ sơ bằng -Stt 3 trước khi chạy toàn bộ kế hoạch.
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action SyncDates
# 4. SAU KHI cán bộ tự gửi duyệt/duyệt/xuất bản các bản sửa qua CMS:
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action VerifyDates
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action SyncLinks
```

Các lệnh có kết nối CMS yêu cầu nhập tài khoản/mật khẩu kín/CAPTCHA tại console; không lưu password/cookie. Dùng đúng tài khoản gắn với journal cũ và quyền sửa hồ sơ. Không đọc `.env` hoặc SQL vận hành.

## Phạm vi cập nhật và phục hồi

- Nhật ký cũ `web-journal.json` được giữ nguyên. Kế hoạch ngày và nhật ký ngày tách riêng (`web-date-plan.json`, `web-date-plan.journal.json`), chứa mapping, snapshot, rowversion và command ID, **không chứa credential/CSRF**. Giữ trong thư mục private, không đưa lên Git.
- Chỉ POST `BeginEdit` khi bài đang Published, sau đó `Save` với ngày mới; không Submit/Approve/Publish. Bản công khai, actual `PublishedAtUtc`, PDF và URL giữ nguyên cho đến khi người có quyền công bố bản sửa. Không tạo bài/PDF mới hoặc WeeklyScheduleRelease.
- Đọc metadata đầy đủ từ revision qua chức năng so sánh lịch sử; giữ các trường không xuất hiện trong form. Bản nháp phải khớp snapshot và lịch sử mới nhất. Nếu có tự lưu chưa chốt, lịch có cấu trúc, bài PendingReview/Approved/hẹn giờ, mất quyền, PDF đổi hoặc rowversion đổi: dừng, không đoán và không hủy workflow. Cán bộ xử lý/lưu nháp phù hợp rồi lập kế hoạch mới.
- Mất phản hồi: chạy lại `SyncDates` cùng kế hoạch. Công cụ đối soát command đã ghi trong audit với revision/metadata/rowversion và PDF; không tạo lệnh khác để ghi đè. Lỗi bất kỳ dừng lượt, các hồ sơ đã Verified vẫn có nhật ký. Không tự xóa/rollback dữ liệu CMS.
- Kế hoạch cũ không bị ghi đè. Nếu đã xử lý xung đột hoặc xác nhận thêm ngày, dùng `-DatePlan web-date-plan-2.json` cho **cả DatePlan và SyncDates**. Không xóa nhật ký cũ. Nếu còn lệnh dở, đối soát bằng kế hoạch cũ trước.
- `VerifyDates` đọc revision công khai, kiểm tra PDF đúng hash, ngày tại `/bai-viet/{id}` và `/lich-tuan` (có phân trang), đọc lại published revision. Không coi ngày đã lưu ở draft là PASS công khai. Báo cáo là ảnh chụp trạng thái tại lúc kiểm tra, không bảo đảm thay đổi sau đó.
- SyncLinks tiếp tục ghi URL đúng ContentId vào D7:D49, cập nhật E sai trong bản sao; không sửa A/B/C hoặc các sheet khác. Không nhập lại 43 PDF.

## Kiểm thử tập trung

`powershell -NoProfile -File scripts/test-weekly-web.ps1 -SourceZip C:\website_upload\website_upload.zip -DatesOnly`

Test dùng HTTP/controllers thật và SQL Server LocalDB riêng schema18, một PDF, tài khoản giả. Kiểm tra ngày text/serial/mơ hồ, snapshot metadata, nháp và bài công khai, ngắt phản hồi BeginEdit/Save, sửa đổi đồng thời, idempotency, hiển thị sau duyệt/công bố và Excel/SyncLinks. Không chạy bộ test cũ đầy đủ, không sửa Web/Worker hay dữ liệu vận hành.

Kết quả 17/11/2026: **44/44 kiểm tra tập trung PASS**, gồm các bước dựng host/Identity/SQL cô lập và kiểm tra ngày/revision/Excel (`weekly-web-dates-1794899958423`). Release build PASS, 0 warning/error. Test host/LocalDB đã dừng. Hai trang công khai đã kiểm trên fixture sau workflow thật; **chưa kiểm ngày sau sửa trên gdla.gov.vn vì chưa thực thi sửa hồ sơ vận hành hoặc công bố bản sửa**.

Rà soát cuối bổ sung ràng buộc ngày từng dòng plan với tập ngày đã xác nhận (không chỉ hash của tập ngày). **4/4 kiểm tra offline riêng PASS** cho đúng tập ngày, chèn ngày số chưa xác nhận, sửa ngày text và hash sai; Release build cuối PASS. Không chạy lại phần HTTP/SQL không đổi. Publish self-contained và parser PowerShell PASS.
