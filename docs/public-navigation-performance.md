# Điều hướng công khai — 16/11/2026

Phạm vi: trang chủ, danh sách tin, chuyên mục, bài viết. Không triển khai IIS,
không kết nối SQL vận hành, không thay schema/cấu hình triển khai hoặc dữ liệu thật.

## Kết quả đo

Baseline `f8fe40cb464917ad0ad11dae5d317a732c95cf22` (nhánh PR #13).
PR #13 đã merge vào main tại `741fd170d97e1a84113c69223a39cde2c477f29e`
sau khi chủ dự án xác nhận. Tree của merge commit trùng baseline đã kiểm thử.
PR #14 đã chuyển base sang main, chỉ còn phần điều hướng; chưa merge/deploy.

Edge headless, viewport 1366×900, Kestrel HTTP loopback, SQL Server LocalDB riêng,
schema 18, application role thật, Pooling=False. Cùng 120 nội dung giả đã xuất bản,
4 chuyên mục và cấu hình trang chủ nhiều khối. Không dùng 703 media hay tài khoản thật.
Mỗi URL: một lần tải đầu và 3 lần bấm liên kết tải trang đầy đủ; không prefetch/SPA.
Lượt sau là **trung vị 3 lần**, đơn vị ms:

| Trang | TTFB đầu trước → sau | TTFB lượt sau trước → sau | Click → FCP trước → sau | Mở SQL/request trước → sau |
|---|---:|---:|---:|---:|
| `/` | 4105 → 1872 | 1493 → 423 | 1528 → 460 | 18 → 2 |
| `/tin-tuc` | 552 → 378 | 597 → 300 | 640 → 337 | 6 → 2 |
| `/chuyen-muc/{slug}` | 856 → 313 | 542 → 313 | 584 → 353 | 7 → 2 |
| `/bai-viet/{id}` | 1981 → 633 | 1618 → 404 | 1664 → 441 | 17 → 2 |

Hai kết nối còn lại: một scope xử lý lịch đăng, một scope trang công khai.
Số lệnh EF ở các lượt sau vẫn lần lượt **24/12/13/25**: giữ các lần đọc lại
con trỏ Published, trạng thái/quyền và dữ liệu CMS. Không đánh đồng giảm kết nối
với giảm số truy vấn nghiệp vụ. RPC kích hoạt role không nằm trong số lệnh EF;
trong fixture này số RPC đó giảm theo số lần mở kết nối.

- Dải tin chạy: bỏ tải toàn bộ HTML `/tin-tuc` ở nền. Endpoint mới chỉ trả tối đa
  10 ID/tiêu đề đang công khai. Lệnh EF mỗi lần tải dải tin: **12 → 2** (bao gồm
  một lần kiểm tra/claim lịch); không chạy COUNT danh sách, Razor hoặc ghi lượt xem.
- Thống kê footer khi hết cache: **4 COUNT → 1 aggregate**, giữ nguyên cache
  thống kê 15 giây, các cửa sổ giờ Việt Nam và bộ đếm bigint; không cache nội dung.
- Phản hồi click: con trỏ chờ và `aria-busy` đo **28,4 ms** đến frame kế tiếp.
  Không có thanh tiến độ giả, không chặn điều hướng gốc; Back khôi phục trạng thái.

## Nguyên nhân và sửa đổi

1. EF tự đóng/mở kết nối giữa các truy vấn. Mỗi lần mở lại kích hoạt Application
   Role; cấu hình không pooling. Baseline trang chủ lượt sau tốn khoảng 1,34–1,45 s
   trong mở kết nối, trong khi lệnh EF chỉ khoảng 12–235 ms. Middleware mới giữ
   kết nối trong **một request nội dung** rồi đóng bằng finally, kể cả khi lỗi.
   Không giữ transaction/snapshot xuyên request, không đổi pooling/TLS/Runtime18.
2. JavaScript tải nguyên trang tin để lấy tiêu đề cho ticker, tạo thêm SQL và
   dựng lại header/footer. Dùng projection công khai nhỏ, không lưu cache qua trang.
3. Bốn thống kê footer đọc tuần tự cùng bảng; gộp bằng SUM(CASE)/COUNT_BIG.

`PublicSiteService` đã dùng snapshot riêng cho từng request; không thêm cache
toàn tiến trình vì cần cập nhật ngay khi CMS công bố/gỡ bài. Giữ COUNT phân trang
và cơ chế đọc lại revision công khai trong `PublicContentQuery`.

`DuePublicationMiddleware` chạy trước khi đọc trang, vẫn dùng scope và giao dịch
riêng. Không bỏ claim/check lịch, không đổi khoảng hẹn giờ, quyền người duyệt,
revision/hash, retry hoặc lease. Chi phí lượt sau khoảng 75–151 ms ở phép đo này;
không đưa tác vụ đó ra sau response chỉ để đạt điểm tốc độ.

## Phân tách và giới hạn

Harness ghi `Server-Timing`, thời gian EF command, mở kết nối (bao gồm role), action,
Razor/result và từng tài nguyên. Các khoảng action/Razor bao gồm SQL nên **không
cộng chúng với SQL/open**. EF duration chưa bao gồm toàn bộ đọc/materialize kết quả.
Razor/result có cả callback thống kê OnStarting; chưa phải riêng CPU Razor.

- Sau sửa, SQL open trung vị khoảng 197–201 ms/request; EF khoảng 94–195 ms
  tùy trang. DNS/TCP kết nối lại ở lượt sau bằng 0 trên loopback; tải HTML ~0,5 ms.
- CSS/JS trang chủ đầu tải khoảng 8–11 ms/tệp, font khoảng 37–40 ms. Không có
  bằng chứng đây là phần gây chờ chính trong fixture. Giữ CSS/design2 và JS defer;
  ảnh chính đã ưu tiên cao, ảnh phụ lazy/async. Fixture không có ảnh nội dung:
  **chưa kết luận về tốc độ 703 ảnh/PDF trên IIS hoặc mạng thực**.
- “Lần đầu” là lần đầu URL trong browser context mới; chỉ `/` là lần đầu của
  tiến trình Web. Không xóa cache SQL/OS giữa hai phiên, không giả lập WAN/TLS,
  không đo tải đồng thời hay p95. Không phải SLA; lần đầu `/` và bài vẫn >500 ms.
- Polling nền chỉ bị tắt bằng DI trong **test host** để quy chiếu chi phí request.
  Middleware lịch đăng thật, RuntimeRoleInterceptor thật và SQL thật vẫn chạy.
  Không có cờ tắt kiểm tra mới trong runtime.

Raw (ngoài Git): `.local/tests/nav-80722f45347a/before-e3005ac1`,
`after-49bd8879`, `feedback-2b3e48e5`. Một thử nghiệm trung gian chưa biên dịch lại
TypeScript đã bị loại khỏi kết quả cuối; các lỗi harness được giữ trong thư mục test.

## Kiểm tra tập trung

- Release Web và Worker: PASS, 0 lỗi/cảnh báo; TypeScript compile PASS.
- Browser: 16 điều hướng trước + 16 sau; HTTP 200 và dữ liệu đo đầy đủ.
- 21 kiểm tra kết nối/role/thống kê/đọc mới PASS: đóng kết nối khi lỗi, không mở
  connection cho Admin, gỡ bài có hiệu lực ở detail/ticker, đổi live config có
  hiệu lực request sau. Fixture dùng revision cấu hình mới, không sửa lịch sử.
- 16 kiểm tra lịch đăng HTTP/SQL PASS cùng middleware mới: đúng revision đã duyệt,
  bài tương lai còn ẩn, quyền duyệt bị thu hồi chặn đăng, hủy/sửa lịch, concurrency
  chỉ có một command/attempt/audit. Không chạy lại upload/video E2E.
- Feedback PASS: dưới 100 ms, Back/bfcache khôi phục, không đánh dấu thao tác
  tải file, mở tab khác hoặc anchor nội trang là chuyển trang chờ.

Harness `tests/LandPortal.PublicNavigationTests/run.ps1` nhận `DotnetPath`,
`PlaywrightModule`, `Phase` và `FixtureRoot`. Phase chỉ là nhãn: phép đo trước phải
chạy baseline cùng tài nguyên JS baseline, phép đo sau chạy source mới trên cùng
fixture. Không dùng nhãn `before` để giả lập baseline bằng source mới.
Harness chỉ cho phép LPNav_* LocalDB, bind đúng MDF trong `.local/tests/nav-*`,
schema18 và SMTP Disabled; mỗi lần đo đưa riêng lượt xem giả về cùng trạng thái
ban đầu. Không nạp `.env` hoặc cấu hình SQL vận hành.

## Triển khai sau khi duyệt

Giữ nguyên: `git pull` → `powershell -NoProfile -File scripts/build-for-iis.ps1`
→ thay nguyên `web` gồm `.runtime`. Không thêm cấu hình IIS, không migration.
Chưa merge/deploy. Cần đo lại trên IIS sau triển khai được phép để biết độ trễ
mạng/SQL thực và tải đồng thời; không suy diễn số đo LocalDB thành kết quả gdla.gov.vn.
