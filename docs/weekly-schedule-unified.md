# Một trang Lịch tuần cho nội dung cũ và mới

`/lich-tuan` giữ bộ lọc năm/tuần và lịch có phiên bản, đồng thời hiển thị danh sách
lịch đã công bố (10 mục/trang, mới nhất trước). Menu không cần đổi. Các bài thuộc
chuyên mục Lịch tuần (chính hoặc phụ, ID 76) và các bản lịch được phát hành bằng
CMS đều xuất hiện. Không tạo bản ghi phát hành từ bài cũ, không nhập lại PDF.

Khi chọn tuần 42/2026, bài có tiêu đề “Lịch công tác tuần 42 năm 2026...” được liệt
kê ngay cả khi không có WeeklyScheduleRelease. Liên kết mở bài công khai cũ, giữ
nguyên PDF/Word đính kèm và các điều kiện truy cập tệp hiện hành. Trang không còn
báo “chưa có lịch được duyệt” khi đã tìm thấy bài lịch công khai đúng tuần.

Nguồn đọc là PublicContentQuery.Published() và WeeklySchedulePublication.Releases().
Chỉ tiêu đề/metadata của revision công khai được dùng; không lấy draft, bài đã gỡ,
bài trong thùng rác hoặc nhóm công khai chưa được phép. Không có SQL write,
migration, sửa .env, media, phân quyền, menu CMS hay dữ liệu phát hành.

Tuần/năm lấy từ metadata đã công bố hoặc cặp rõ ràng trong tiêu đề (ví dụ “tuần
thứ 42 năm 2026”, “tuần 42/2026”). Không lấy tuần của ngày đăng: ngày đăng có thể
khác tuần được nói đến. Tiêu đề mơ hồ/không đủ năm vẫn nằm trong danh sách chung,
nhưng không được tự gán vào một tuần. Tham số version không hợp lệ vẫn trả 404;
lịch sử phát hành và quyền tải Word của các bản có cấu trúc không thay đổi.
`/lich-tuan/nguon` vẫn hoạt động để bảo toàn liên kết cũ.

## Kiểm tra tập trung

Không cần SQL để chạy kiểm tra parser/liên kết:

```powershell
dotnet run --project tests/LandPortal.WeeklyScheduleTests -c Release
```

Trước bàn giao chạy build Web/Razor thông thường và kiểm tra trên môi trường riêng:
- Tuần 42/2026 chỉ có bài cũ + PDF: thấy bài, mở đúng trang/attachment hiện hữu.
- Tuần có release mới và bài cũ: cả hai hiện, release/history không bị thay thế.
- Tuần không có nội dung: thông báo đúng tuần, danh sách đã công bố vẫn truy cập được.
- Bài gỡ/nháp/thùng rác không hiện; chuyên mục phụ 76 được tính; không nhân đôi bài.
- Trang danh sách có hơn 10 mục: phân trang giữ bộ lọc, không tải toàn bộ body/tệp.
- Mobile 390px và desktop: bộ lọc, tiêu đề dài và phân trang không tràn ngang.

Phiên sửa trực tiếp chỉ kiểm tra source và logic/HTML fixture; chưa chạy .NET/Razor
build hoặc SQL/IIS do môi trường hiện tại không có .NET SDK. Các ca C# ở trên được
bổ sung để chạy trên máy build, không tuyên bố đã PASS tại IIS.

Sau build thành công, triển khai nguyên thư mục web (gồm .runtime) theo quy trình
WebBundle hiện hành; giữ các kho dữ liệu và tools/ffmpeg bên ngoài. Không sửa .env.
