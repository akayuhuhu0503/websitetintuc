# Ngày đăng từ Excel — cập nhật qua CMS HTTPS

Lỗi của công cụ WEB IMPORT cũ: payload tạo nháp không có `Metadata.PublicationDate`. Vì vậy trang công khai dùng ngày công bố thật làm ngày hiển thị. Bản sửa chỉ thay công cụ Developer, dùng trường và endpoint CMS đã có; không cần cập nhật IIS, SQL hoặc upload lại PDF.

## Đối chiếu nguồn

**Nguồn duy nhất của ngày đăng là cột B** trong sheet `2.4.1_LichCT`. Đọc đủ 43 dòng B7:B49: **26 ngày text theo dd/mm/yyyy và 17 ô số Excel**. Ngày 17/11/2026 chủ dữ liệu đã xác nhận toàn bộ 17 ngày dự định trong bảng dưới, gồm STT 1 = 05/01/2026 và STT 43 = 04/11/2026. Xác nhận được lưu trong `date-decisions.json` private, gắn đúng hash ZIP/Excel; không tự áp dụng sang nguồn khác.

Không dùng tiêu đề hoặc PDF để chọn ngày, cảnh báo tuần/PDF không làm thay đổi ngày. STT 40–41 lấy thẳng text 19/10/2026 và 26/10/2026 từ B. Cảnh báo nội dung STT 39–42 vẫn giữ riêng cho cán bộ biên tập, không phải điều kiện xác minh ngày.

Tất cả 17 ô số dùng number format tích hợp 14, phụ thuộc locale hiển thị. **Serial lưu một ngày lịch xác định; đổi định dạng hiển thị không sửa được lỗi nhập đảo ngày/tháng.** Công cụ giải mã theo hệ ngày 1900/1904 của workbook, không phụ thuộc locale máy Developer/SQL. Với đúng nguồn này, dùng 17 diễn giải đã được chủ dữ liệu xác nhận; không tự đảo mọi ngày số của nguồn tương lai. Payload CMS dùng ISO `yyyy-MM-dd` và `DateOnly`, không qua ngày/giờ hiện tại hoặc phép đổi múi giờ.

| STT | Ngày lịch lưu ở B (dd/mm/2026) | Ngày dự định đã được xác nhận (dd/mm/2026) |
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
| 42 | 11/02 | 02/11 |
| 43 | 11/04 | 04/11 |

`DateReport` chỉ đọc workbook và hash ZIP, không mở/phân tích lại PDF, không kết nối CMS/SQL. Báo cáo đủ 43 dòng giữ raw B, kiểu, định dạng, ngày lịch lưu, ứng viên đảo và ngày CMS đã chuẩn hóa cùng trạng thái. Không sửa A/B/C. Kết quả kiểm PDF 43 tệp/120 trang và sửa E48 2→3 là kiểm chứng của lượt trước; lượt chuẩn hóa ngày không đếm lại PDF.

## Chạy trên máy Developer

Mở PowerShell tại **thư mục CLI mới**, giữ nguyên thư mục kết quả cũ `%USERPROFILE%\LandPortal-Private\weekly43-web` có `web-plan.json`, `web-journal.json`, `source-backup.zip` và `original.xlsx`. Không chạy lại DryRun/Pilot/ImportAll cho 43 hồ sơ đã Verified.

```powershell
# 1. Offline, không đăng nhập: đọc xác nhận đã lưu và xuất 43 ngày chuẩn hóa.
powershell -NoProfile -File .\NHAP-LICH-WEB.ps1 -Action DateReport
```

Đọc `DOI-CHIEU-NGAY-*.md` trong thư mục kết quả. Với bộ nguồn hiện tại, 17 xác nhận đã được ghi theo câu trả lời của chủ dữ liệu; **không cần xác nhận lại từng STT**. Giữ file private `date-decisions.json` cùng nhật ký cũ. Nếu chuyển sang ZIP/Excel khác, công cụ từ chối dùng xác nhận cũ. Nếu chưa có file, chỉ tạo mẫu trống; không giả xác nhận. Ngày text hợp lệ được đọc trực tiếp từ B, không cần xác nhận lặp. Ngày ngoài hai diễn giải của B hoặc dùng để thay đổi một ngày text rõ ràng bị từ chối.

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

Rà soát cuối bổ sung ràng buộc ngày từng dòng plan với tập ngày đã xác nhận (không chỉ hash của tập ngày). **4/4 kiểm tra offline riêng PASS** cho đúng tập ngày, chèn ngày số chưa xác nhận, sửa ngày text và hash sai. Đây là kết quả trước bổ sung chuẩn hóa độc lập cột B.

Kiểm thử bổ sung chỉ đọc Excel, không mở PDF hoặc dựng HTTP/SQL: `dotnet run --project tests/LandPortal.WeeklyWebTests -c Release -- <source.zip> --date-column-only`. Bao gồm locale en-US/vi-VN/fr-FR, serial 1900/1904, ngày sai/có phần giờ/công thức, không dùng tiêu đề/cached row/PDF, giữ bytes nguồn, ràng buộc ngày với B và báo cáo 43 ngày sau xác nhận. Không chạy lại toàn bộ HTTP/SQL hoặc các bộ E2E cũ.

Kết quả sau đính chính nguồn ngày: **36/36 kiểm tra cột B và báo cáo PASS; 4/4 ràng buộc plan PASS**. Release build 0 lỗi/cảnh báo. Chạy đúng nhánh offline trước phần dựng host; không phân tích PDF, không mở HTTP/SQL. Kết quả 44 HTTP/SQL ở trên là kiểm chứng lịch sử, không chạy lại hoặc cộng gộp vào lượt này. Đã chuẩn hóa đủ 43/43 ngày bằng 17 xác nhận private và 26 ngày text; **chưa sửa hồ sơ thật**.
