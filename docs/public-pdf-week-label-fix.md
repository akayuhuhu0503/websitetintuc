# Xem PDF công khai và nhận diện tuần — 17/11/2026

## Thay đổi

- `MediaController`: bỏ điều kiện ép tải riêng vì `managedDocumentNoAv` đối với PDF. `/media/{id}` trả PDF để trình duyệt mở; `?download=true` vẫn trả `attachment`. DOCX/XLSX vẫn luôn tải về. `Portal/Article.cshtml` đã có hai liên kết đúng, được kiểm tra bằng Razor thực tế, không cần đổi view hoặc thêm thư viện.
- PDF vẫn phải được phép phục vụ theo policy hiện hành, có liên kết với revision công khai, proof hợp lệ, đúng byte/hash và không bị cách ly. Giữ kiểm tra quyền công khai hai lần, `no-store`, `nosniff`, `CSP sandbox; default-src 'none'; frame-ancestors 'self'`, `X-Frame-Options: SAMEORIGIN` và Range. `NotScanned` không trở thành `Clean`. Kiểm định cấu trúc lúc upload, chống hành động/JavaScript/biểu mẫu/tệp nhúng của PDF không thay đổi. Đây là kiểm định kỹ thuật, không phải quét malware; trình duyệt vẫn có thể tải về nếu người dùng cấu hình như vậy.
- `WeeklyScheduleArticle`: nhận dạng “Tuần 45. Từ ngày 02/11/2026 đến ngày 08/11/2026”. Khoảng ngày phải hợp lệ, thuộc cùng một tuần ISO và khớp số tuần ghi rõ trong tiêu đề. Metadata `WeeklyYear/WeeklyWeek` hợp lệ của revision công khai luôn được ưu tiên; metadata thiếu một nửa hoặc sai không được tự bù từ tiêu đề.
- `WeeklyScheduleController`: tiền lọc SQL hỗ trợ tuần ISO ở ranh giới năm khi năm lịch trong tiêu đề khác năm ISO. Cả nhãn trong `_ScheduleArticles.cshtml` và kết quả lọc dùng cùng `Period`; không thay partial, nội dung hoặc workflow.
- Tiêu đề mâu thuẫn mà không có metadata hợp lệ vẫn nằm trong danh sách lịch công khai, nhưng không tự gán nhãn/nhóm tuần. Ngày đăng tiếp tục ưu tiên `Metadata.PublicationDate` theo luồng hiện hành; không lấy khoảng ngày của tiêu đề làm ngày đăng.

## Hồ sơ cần rà soát

Đọc cột C của 43 dòng trong sheet `2.4.1_LichCT` của ZIP đã cung cấp, không chỉnh Excel hoặc đọc lại PDF. Có 39 tiêu đề thống nhất và 4 tiêu đề lệch số tuần với khoảng ngày. Đọc 6 trang danh sách công khai `/lich-tuan` thấy 59 bài, trong đó 42 tiêu đề khớp nguyên văn nguồn; STT 2 chưa khớp nguyên văn, không suy luận bài bị thiếu hoặc cần nhập lại. Bốn hồ sơ sau đều có link công khai khớp tiêu đề:

| STT nguồn | Tuần ghi trong tiêu đề | Khoảng ngày trong tiêu đề | Tuần ISO của khoảng ngày | Bài công khai |
|---|---|---|---|---|
| 39 | 41 | 12/10/2026–18/10/2026 | 42/2026 | [Mở bài](https://gdla.gov.vn/bai-viet/4ba54265-6946-407a-ae02-131cde467365) |
| 40 | 42 | 19/10/2026–25/10/2026 | 43/2026 | [Mở bài](https://gdla.gov.vn/bai-viet/5343466c-fbfd-4528-a211-f130092e3028) |
| 41 | 43 | 26/10/2026–01/11/2026 | 44/2026 | [Mở bài](https://gdla.gov.vn/bai-viet/c9ab211d-6788-4bd1-8b62-473b2fe44060) |
| 42 | 44 | 02/11/2026–08/11/2026 | 45/2026 | [Mở bài](https://gdla.gov.vn/bai-viet/7f267250-5d4f-4bba-8ec3-04e1218775f7) |

Bảng trên chỉ nêu mâu thuẫn trong **tiêu đề**; không khẳng định khoảng ngày đúng hơn số tuần. Chưa đọc metadata riêng trên CMS hoặc SQL vận hành. Cán bộ cần đối chiếu nguồn rồi chỉnh qua workflow nếu cần; bản sửa này không thay tiêu đề, ngày đăng, metadata, PDF hay lịch sử công bố.

## Kiểm tra

- Release build Web và project kiểm thử: **PASS, 0 lỗi/cảnh báo**.
- **50/50** kiểm tra thuần nhận diện tuần/link: dạng mới/cũ, metadata ưu tiên, ngày sai, nhiều tuần, tuần 53, ranh giới năm, không suy từ ngày đăng.
- **25/25** kiểm tra HTTP + SQL Server LocalDB/schema18 cô lập: PDF inline/download/HEAD/Range, byte và header, DOCX/XLSX download-only, Razor Article/nhãn tuần thực, lọc SQL và năm ISO, bản nháp/thu hồi/cách ly/policy tắt/proof lỗi/hash sai bị từ chối. Fixture Office chỉ mô phỏng tài liệu đã kiểm định để kiểm tra response, không chạy lại kiểm thử upload Office.
- Hai lỗi của lần chạy harness đầu (thiếu đăng ký DI và đọc `TextContent` ở node document) đã sửa trong test; lần cuối `pdf-week-20261117-03` PASS. Host và LocalDB thử nghiệm được dừng trong `finally`.
- Các assertion tải xuống của suite cũ được đổi sang URL `?download=true`; không chạy lại các suite E2E đó. Không full regression, không SQL vận hành, `.env`, dữ liệu công khai hay IIS.

Chạy lại riêng khi cần từ checkout có SDK phù hợp:

```powershell
dotnet run --project tests/LandPortal.WeeklyScheduleTests -c Release
dotnet build tests/LandPortal.PublicFunctionsTests -c Release
dotnet tests/LandPortal.PublicFunctionsTests/bin/Release/net10.0/LandPortal.PublicFunctionsTests.dll pdf-week . .local/tests/pdf-week-NEW
```

Lệnh cuối tự tạo LocalDB mới với tên ngẫu nhiên và chỉ chấp nhận thư mục kết quả mới trong `.local/tests/pdf-week-*`; không tải cấu hình vận hành. Chưa triển khai hoặc nghiệm thu binary mới trên IIS. Quy trình build/publish hiện hành không đổi; sau khi review/merge mới cập nhật nguyên `web` gồm `.runtime`.
