# Đồng bộ QLDD — SYNC-01A

Trang quản trị: `/admin/source-sync` (**Đồng bộ QLDD**). Chức năng mặc định tắt. Chỉ người có quyền `site.manage` được xem/thay đổi hàng chờ; tạo nháp còn cần quyền tạo nội dung, quản lý media và đúng phạm vi chuyên mục. Không tự cấp quyền.

## Cách dùng

1. Mở **Ánh xạ chuyên mục**, chọn đúng chuyên mục GDLA cho từng chuyên mục nguồn muốn biên tập. Không suy ra theo tên hoặc dùng thẳng ID nguồn.
2. Bấm **Kiểm tra bài mới ngay** hoặc bật kiểm tra định kỳ. Nút trả lời ngay sau khi ghi yêu cầu; việc lấy nguồn diễn ra trong Web ở nền.
3. Mở **Xem trước** để đọc tiêu đề, sapo, nội dung, nguồn/ngày đăng, tài nguyên và cảnh báo. Hình/tệp tạm chưa được đưa ra HTTP.
4. Bấm **Chuyển vào bản nháp biên tập**. Tệp được kiểm định lại qua hệ thống upload hiện có; tệp thiếu/hỏng/chưa được phép dùng bị loại khỏi liên kết và giữ cảnh báo. Nháp thuộc tài khoản đang thao tác.
5. Kiểm tra/sửa nháp rồi gửi duyệt → người có quyền duyệt → người có quyền xuất bản như hiện tại. Không có xuất bản tự động.

Nếu nguồn thay đổi, hàng chờ hiển thị **Nguồn đã cập nhật**, đặt nội dung nguồn cạnh bản CMS được phép đọc. Không sửa revision, khôi phục bài bị gỡ/thùng rác hoặc tải lại media của bài đã có. Khi không tìm được snapshot cũ hợp lệ, giao diện nói rõ chỉ dùng lần quan sát đầu làm mốc; không khẳng định lịch sử nguồn chưa từng đổi.

**Thử lại** chỉ mở khi hết thời gian chờ. Thao tác này hoạt động ngay cả khi đồng bộ định kỳ tắt. **Bỏ qua** không xóa bài/media và không bị lượt phát hiện tiếp theo tự khôi phục.

## Giới hạn và bảo toàn

- Một lượt: trang chủ + hai trang đầu chuyên mục trong danh mục nguồn đã xác minh, luân phiên các chuyên mục; tối đa 20 ID gần đây, 3 chi tiết, 32 request, 12 tài nguyên/bài, 10 MiB/tệp, 40 MiB/bài. Hàng chờ chưa biên tập tối đa 200 mục. Không đi sâu phân trang/lịch sử.
- Chu kỳ 5 phút sau lượt trước, poll SQL 20 giây để nhận yêu cầu thủ công. Thời hạn lượt 100 giây, lease 3 phút. Không bảo đảm chạy lúc IIS nghỉ/dừng. Khi Web hoạt động lại, dùng checkpoint/lease đã lưu; không crawl trong request công khai.
- Lỗi nguồn có backoff 15/30/45 phút và tạm dừng sau 3 lượt liên tiếp; Admin có thể yêu cầu tiếp sau thời gian chờ. Mỗi bài lỗi cũng tối đa 3 lần tự thử, sau đó cần thao tác rõ ràng của Admin.
- Dùng `SafeSourceClient` hiện có: danh sách host cho phép, xác minh DNS/địa chỉ công khai, kiểm tra redirect, giới hạn byte/timeout/tần suất. Kiểm tra robots trước khi đọc các trang nguồn. Không tải video, văn bản pháp luật/dự thảo đặc thù trong SYNC-01A.
- `SourceParser`, `SourceSrcset`, danh mục nguồn dùng chung với công cụ cũ; Web không gọi `SqlImporter.ImportAsync`. Không đổi thuật toán parser cũ.
- Nội dung giữ sapo/tác giả/ngày nguồn có thật; HTML đi qua `HtmlPolicy`. Lưu cả snapshot và checkpoint nguồn; không dựng thông tin còn thiếu. Ảnh lazy/srcset chỉ thay bằng tệp đã tiếp nhận; không giữ hotlink chưa kiểm định.
- Tài nguyên lưu tạm dưới `Storage.SnapshotRoot/source-sync`, kiểm tra SHA trước khi chuyển qua `MediaService` / `EditorialDocumentService`. Các chính sách hiện hành quyết định chấp nhận/quét; không bật ngoại lệ mới, không giả `Clean`. Không dùng tài khoản nền để giả người biên tập.

## Schema và cơ chế đồng thời

**Không cần migration mới:** đã chạy bằng schema 18 trên SQL Server LocalDB cô lập với Application Role thật. Không thêm quyền SQL. Tái sử dụng:

| Thành phần | Dữ liệu |
| --- | --- |
| `SiteSettings` | Khóa `SourceSync.QLDD`: bật/tắt, ánh xạ, cursor, lần kiểm tra/lỗi |
| `Jobs` | Type riêng `QlddSyncSweep` / `QlddSyncEntry`, unique idempotency key, lease/fence và backoff |
| `CrawlRun`, `CrawlItem` | Hàng chờ, JSON checkpoint theo bài, trạng thái; không lẫn với crawl/import cũ |
| `SourceSnapshot`, `ImportIssue` | Raw HTML/hash và lỗi thu thập |
| `SourceRecord`, `SourceUrlAlias` | Nhận diện nguồn hiện có và quan hệ nháp mới |
| `ContentCommands`, audit/revision CMS | Tạo nháp, chống gửi lặp và lưu actor thực trong transaction CMS |

Article/Announcement dùng chung namespace ID tin QLDD (canonical `chi-tiet-tin-tuc.aspx?ItemID=…`); không va ID văn bản/video. Đối chiếu cả SourceRecord và URL alias; ID/alias trỏ nhiều bài thì dừng. Không so tiêu đề để nhập trùng. Khóa SQL theo lượt/bài cùng unique constraint và kiểm tra lại nguồn trước commit bảo vệ nhiều tiến trình Web/recycle. Một command CMS commit đồng thời nháp, revision, quan hệ nguồn/alias và trạng thái hàng chờ.

`JSON_VALUE` chỉ dùng cho truy vấn tìm tiêu đề/phân trang; không thay cấu trúc bảng. Không khởi tạo cấu hình CMS, import dữ liệu hay migration khi website khởi động.

## Kiểm tra và giới hạn nghiệm thu

Kiểm thử tập trung ở `tests/LandPortal.SourceSyncTests`, dùng LocalDB mới có schema 18, Application Role, tài khoản giả và HTTP Kestrel thật. Fixture truyền tải dùng snapshot QLDD đã lưu + nội dung/tệp thử riêng; không dùng credential vận hành. Bao gồm nháp/duyệt/xuất bản, source thay đổi, CSRF/quyền bị thu hồi, kiểm định/giả định dạng/tamper SHA, lease/restart/chống trùng, lỗi/backoff, bỏ qua, ánh xạ thiếu và homepage phản hồi trong khi fetch nguồn bị giữ chờ. Đây không phải benchmark tải hoặc chứng nhận hiệu năng IIS.

Kết quả 16/11/2026: **51 kiểm tra HTTP/SQL PASS** trên mã cuối; **10 kiểm tra trình duyệt PASS** tại 1366/768/390/320px (không tràn trang, ánh xạ dùng được, lọc không reload toàn trang, không lỗi JavaScript). Release Web/Worker và CLI parser dùng chung: PASS, 0 cảnh báo/lỗi. Không cộng các lượt chạy lỗi trước khi sửa vào số PASS; instance test đã dừng. Không chạy full regression.

```powershell
powershell -NoProfile -File scripts/test-source-sync.ps1 -SavedQlddSnapshot <duong-dan-snapshot-tin-3681.html>
```

Có thể chỉ định `-DotnetPath` tới SDK của dự án. Script chỉ tạo instance `LPSync_<random>` và database `LandPortal_Test` mới, xác minh máy LocalDB trước DDL; dừng đúng instance khi xong, giữ file kết quả để đối soát. Không đọc `.env`/SQL thật. Browser test riêng tại 1366/768/390/320px không tạo ảnh/video bằng chứng.

Khảo sát nguồn trực tiếp ngày 16/11/2026 giới hạn đúng **3 request** bằng `SafeSourceClient`: robots 404; homepage 200, phát hiện 5 ID; một bài ID 3767 trả 200, category 53, title/body/canonical hợp lệ. Không tải media và không ghi SQL trong khảo sát này. Đây là xác nhận HTML hiện hành; chưa nghiệm thu mạng/ACL/chính sách upload trên IIS thật hoặc mọi chuyên mục/loại tệp nguồn.

Chưa merge/deploy. Sau khi chủ dự án nghiệm thu, quy trình IIS vẫn `git pull` → `scripts/build-for-iis.ps1` → deploy nguyên `web` gồm `.runtime`. Không sửa `.env`, manifest hay JSON sau Publish. Admin chủ động chọn ánh xạ và bật đồng bộ; khi tắt chức năng, dữ liệu đã nhận vẫn được giữ. Có thể rollback binary; các bảng nghiệp vụ và schema không đổi.
