# Tối ưu Admin và upload — 16/11/2026

Baseline: `d98d6898c0619e4660a88064a193c5bbc6832e7a` (`main`, thiết kế số 2).
Nhánh: `perf/editor-upload-responsiveness`. Chưa merge hoặc triển khai IIS.

## Những điểm có bằng chứng và đã sửa

- Editor chờ lưu nháp rồi mới báo đang tải. Với bài đã tồn tại, upload còn tạo hai lần lưu. Nay báo **Chuẩn bị** ngay; upload kiểm tra quyền trên bài hiện có, sau đó lưu một lần để gắn tệp cùng nội dung mới đang nhập. Bài mới vẫn cần tạo nháp trước.
- Tiến độ lấy từ byte XHR; sau khi truyền xong chuyển sang kiểm tra/lưu và đính kèm. Chỉ báo **Đã đính kèm** khi phản hồi lưu thành công xác nhận payload thực sự chứa asset. Không khóa toàn bộ form; chặn thao tác chuyển trạng thái trong lúc đang gắn tài liệu.
- Autosave bỏ request khi các trường lưu không đổi. Không thay DOM trình soạn thảo, cursor hoặc nội dung người dùng; giữ cơ chế revision, phiên đăng nhập và request idempotency hiện có.
- Mất phản hồi upload được đối soát bằng hash tại endpoint chỉ đọc. Chỉ người tải trên đúng bài có quyền sửa mới đọc được kết quả. Không gửi lại file chưa rõ kết quả; không tải lại cả thư viện. Tệp cách ly hoặc chưa đạt chính sách không được tự gắn/công bố.
- Mỗi lần đọc quyền trước đây truy vấn user claims ba lần. Gộp thành một projection mới mỗi lần gọi, giữ phép so sánh theo collation SQL và kiểm tra khóa/vô hiệu hóa/đổi mật khẩu/category scope. **Không cache quyền**.
- Carousel không ép tải ngay mọi ảnh banner dưới màn hình; autoplay dừng khi ngoài viewport. Giữ 5 giây, dots, vuốt, hover và reduced motion; không đổi bố cục thiết kế số 2.

## Số đo trước/sau

### Trình duyệt biên tập

Chromium/Edge thật, Jodit thật; HTTP được kiểm soát với cùng độ trễ 250 ms/lưu và 200 ms/upload. Median 3 lần. Đây là phép đo hành vi client, **không phải tốc độ SQL hay mạng IIS**.

| Chỉ số | Trước | Sau |
|---|---:|---:|
| Chọn tài liệu → feedback DOM | 279,7 ms | 3,8 ms |
| Upload → gắn vào bài đã tồn tại | 762,6 ms | 501,1 ms |
| POST lưu cho thao tác trên | 2 | 1 |
| GET readiness, gồm lần mở trang | 3 | 2 |
| Autosave khi input không thay đổi | 1 request | 0 |

Bài mới vẫn hai lần lưu; median 748 → 767,5 ms, không có bằng chứng cải thiện. Không coi việc DOM được cập nhật là cam kết thời gian toàn bộ mạng/SQL. Fixture editor không có đầy đủ CSS CMS nên không dùng CLS của nó để kết luận giao diện Admin thật.

### HTTP thật và SQL Server cô lập

Cùng máy Developer, .NET 10.0.112 Release, Kestrel loopback, LocalDB riêng từng lượt với dữ liệu giả giống nhau và schema 18. Mỗi cấu hình có database mới; độ nóng cache và tải máy có thể khác. HTTP wall time đo từ middleware; SQL ms là tổng thời gian lệnh EF, **không gồm toàn bộ mở kết nối/Application Role RPC/đọc dữ liệu**, không suy phần còn lại thành CPU. Không ghi SQL text, tham số hoặc secret vào metrics.

Các màn hình/lưu: median 3 lần. Kết quả chắc chắn là giảm số truy vấn; thời gian quan sát dưới đây không phải SLA hay phần trăm tăng tốc trên IIS.

| Thao tác | HTTP trước → sau (ms) | EF trước → sau (ms) | Số lệnh EF trước → sau |
|---|---:|---:|---:|
| Danh sách Admin | 2506,5 → 1955,0 | 115,0 → 122,4 | 25 → 19 |
| Lọc/tìm danh sách | 2372,3 → 1751,7 | 79,5 → 68,7 | 25 → 19 |
| Mở trang tạo bài | 1904,8 → 1611,0 | 73,0 → 56,3 | 20 → 14 |
| Mở editor bài đã có | 2518,6 → 1748,7 | 78,0 → 7,5 | 29 → 23 |
| Lưu bài đã có | 1385,6 → 909,5 | 66,4 → 6,7 | 26 → 22 |

Upload: **một mẫu mỗi định dạng/kích thước**, dùng cùng byte/hash trước/sau cho bộ gần 10 MB. Không đủ mẫu để khẳng định tốc độ ổn định theo kích thước.

| File | Byte | HTTP trước → sau (ms) | EF trước → sau (ms) | Số lệnh EF |
|---|---:|---:|---:|---:|
| JPG giả hợp lệ | ảnh 12×8 | 2443,0 → 1677,5 | 82,4 → 66,0 | 18 → 14 |
| PNG giả hợp lệ | ảnh 12×8 | 2962,2 → 1175,5 | 89,7 → 45,0 | 18 → 14 |
| PDF nhỏ | 657 | 3824,0 → 1730,4 | 117,9 → 60,3 | 22 → 16 |
| DOCX nhỏ | 819 | 2333,4 → 1242,1 | 95,7 → 7,6 | 22 → 16 |
| XLSX nhỏ | 1405 | 1763,8 → 1232,6 | 10,1 → 5,4 | 22 → 16 |
| PDF lớn | 9437851 | 2784,5 → 1475,9 | 108,6 → 70,6 | 22 → 16 |
| DOCX lớn | 9917243 | 1833,8 → 1372,4 | 67,7 → 45,5 | 22 → 16 |
| XLSX lớn | 9917127 | 1714,6 → 1254,3 | 9,2 → 5,3 | 22 → 16 |

Đo riêng thao tác file, median 3 lượt trên cùng byte đã upload: đọc khoảng 1,4–1,9 ms, SHA-256 khoảng 10–10,5 ms, kiểm định PDF 8,6 ms / Office 0,4 ms, ghi bản sao 2,8–2,9 ms. Đây là microbenchmark riêng có ảnh hưởng cache, **không cộng/trừ để phân rã một request upload**. Chưa có bằng chứng cần bỏ bước kiểm định hoặc thay thuật toán hash/ghi file.

Video: một MP4 giả 2 giây, 77226 byte, được xử lý bằng FFmpeg thật và hàng đợi Web/SQL hiện có. Mở phiên 1243 ms, tải chunk 1474 ms, hoàn tất upload 1329 ms; chờ xử lý/polling tới Ready 7287 ms (4 lần đọc trạng thái). MP4/poster có thật; nguồn vẫn `NotScanned`, không có thời điểm quét giả. Không có phép đo video trước tương đương nên không tuyên bố cải thiện tốc độ video. Không chạy lại E2E phát hành video đã PASS.

### Trang công khai

Razor thật qua PublicPresentationHost, dữ liệu giả/SVG nhỏ, không SQL. Ba mẫu trước/sau xen kẽ, cache mới mỗi lần, desktop 1366 và mobile 390 px:

| Chỉ số | Trước | Sau |
|---|---:|---:|
| Request banner mobile trước cuộn | 3 | 0 |
| Tổng request mobile | 24 | 21 |
| Tổng byte mobile của fixture | 284185 | 283225 |
| Tổng request desktop | 31 | 31 |
| Median LCP desktop / mobile | 160 / 160 ms | 148 / 148 ms |
| Median CLS desktop / mobile | 0,00595 / 0,01020 | 0,00595 / 0,01020 |
| Tổng long tasks median desktop / mobile | 143 / 84 ms | 143 / 142 ms |

Chỉ kết luận giảm 3 request banner ngoài màn hình trên mobile và tránh autoplay ngoài viewport. LCP/long tasks dao động; **không có bằng chứng cải thiện đáng tin cậy**. Byte SVG nhỏ không đại diện ảnh vận hành. HTTP/SQL riêng của trang chủ có 13 lệnh trước/sau (1448,4 → 1121,6 ms); bài công khai cũng 13 lệnh (1535,4 → 1222,2 ms): không quy thay đổi thời gian này thành hiệu quả của PR.

## Kiểm chứng và giới hạn

- Release Web và Worker: PASS, 0 lỗi/cảnh báo; TypeScript biên dịch thành JavaScript đã đưa vào source.
- Editor browser: **72 kiểm tra PASS**, gồm nội dung/focus khi đang upload, click trùng, mất phản hồi, đối soát không tìm thấy, quyền lưu bị thu hồi, replay request lưu cũ không chứa attachment, giữ dữ liệu và chặn chuyển trạng thái khi upload.
- HTTP/SQL: **85 assertion PASS**, gồm setup, phép đo lặp, kiểm tra SHA microbenchmark và 8 kiểm tra quyền; không phải 85 kịch bản nghiệp vụ độc lập. Upload đủ năm định dạng, tài liệu nhỏ/gần 10 MB, gắn đúng SQL, phục hồi đúng người/bài, từ chối giả PDF/thiếu quyền; gửi duyệt → duyệt → lập lịch đúng revision → xuất bản và download byte khớp. Kiểm tra này xác nhận tạo lịch, không chờ lịch 30 phút thực thi.
- Video: **19 assertion PASS**, trong đó 15 setup/login và 4 kiểm tra video; không phải 19 E2E video.
- Public browser: **29 kiểm tra PASS**. Không yêu cầu/chụp ảnh hoặc quay video.
- Các thử nghiệm khởi đầu lỗi harness (provider `Disabled` không hợp lệ cho vòng video) được giữ trong `.local`; lượt video cuối dùng provider Clamd hợp lệ nhưng không sẵn sàng trên loopback và no-AV riêng trong DI test. Không sửa guard/chính sách vận hành để chạy thử.
- Đối soát upload tự động cần WebCrypto trên HTTPS/localhost. LAN HTTP không có SubtleCrypto sẽ hướng dẫn xem tài liệu đã tải, không giả hash hoặc tự gửi lại. Trạng thái request chưa rõ được giữ theo từng tệp trong tab hiện tại, không phải hàng đợi lưu bền vững qua đóng tab.
- Chưa đo WAN chậm, máy IIS thật, kho ảnh lớn, DB nhiều nghìn bài hoặc tải đồng thời; không đề xuất index/timeout/connection-policy thay đổi khi chưa có chứng cứ. Không thêm cache quyền/nháp, thư viện UI, dịch vụ hoặc cấu hình triển khai.
- Không thay `.env`, SQL vận hành/LAN, tài khoản thật, 703 media hoặc IIS. Schema chỉ được tạo trên database test mới có allowlist; host và LocalDB test đã dừng.

## Tái hiện và bàn giao

Harness và lệnh: [UploadPerformanceTests](../tests/LandPortal.UploadPerformanceTests/README.md), [PublicPresentationHost](../tests/LandPortal.PublicPresentationHost/README.md), `tests/browser/editor-performance-check.mjs`. Kết quả chi tiết chỉ nằm trong `.local/tests`, không đưa dữ liệu fixture hoặc cấu hình private vào Git.

Các checkpoint đo: baseline `perf-upload-9e18de3ae157`; cùng file gần 10 MB trên binary cũ `perf-upload-15ab6050cdd6`; sau sửa `perf-upload-25266da33cd9`; video `perf-upload-7f876e8c5bbe`; client `editor-performance/{before,after}.json`; public `public-performance/comparison.json`.

Sau khi PR được duyệt, quy trình IIS giữ nguyên: `git pull` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → cập nhật nguyên `web`. Không thêm thao tác cấu hình máy chủ. Lượt này chỉ bàn giao PR, chưa merge/deploy.
