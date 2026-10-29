# Phase3J — độ đầy đủ nguồn QLDD

Run `phase3j-20261021T092822Z-35b2612b`. Audit cuối `coverage-final-v2`, đối chiếu SQLDev250 và checkpoint `70148329b48f7edab54830f5d6bc54652049874704bd9060f01a2c6c189aadfc`. **Tổng toàn nguồn UNKNOWN; không phải nghiệm thu chuyển đủ website.**

Đã kiểm tra813 blob SHA/length nguồn,584 file media Dev và68 snapshot SQL Dev.216 nội dung đạt đối chiếu cached title/summary/author/date/date precision/category/body text và toàn bộ tài nguyên yêu cầu đúng byte, linked published revision/Clean.34 nội dung Dev còn khoảng trống:30 thuộc ledger lịch sử và4 thêm chỉ khác metadata; trong6 trường hợp khác metadata tổng cộng có2 đồng thời bị chặn media. Không ghi đè public/revision để ép khớp.

| Loại | Bóc tách trong cache | Có đúng danh tính ở Dev | Đủ theo tiêu chí trên | Chưa có đúng danh tính Dev |
|---|---:|---:|---:|---:|
|Article|220|192|171|28|
|Announcement|11|8|6|3|
|StaticPage|5|5|0|0|
|LegalDocument|35|34|31|1|
|DraftDocument|9|9|8|0|
|Video|2|2|0|0|

282 bản ghi theo loại/277 canonical,285 danh tính ứng viên/1.396URL.32 danh tính staging chưa có đúng loại tại Dev;5 canonical đã thuộc bài Dev dưới loại khác, nên **27 canonical đã bóc tách thực sự chưa có ở Dev**.9 trong27 đã nhập và verify Test ở lượt này,1 mới Article3205 bị chặn PDF vượt response budget;17 còn lại thuộc ledger lịch sử.3 danh tính parser còn treo (`Article:2024`, `DraftDocument:2024`, `DraftDocument:3`); không coi là3 bài độc lập. Không còn ứng viên thiếu snapshot trong tập frontier đã kiểm kê, nhưng chưa khảo sát hết phân trang hoặc phát hiện hết nguồn.

**52 dòng lịch sử =52 danh tính theo loại,50 canonical;33 canonical đã có đại diện Dev,17 chưa có.**30 dòng trùng đúng danh tính Dev,22 staging;5 staging có canonical thuộc loại khác. Nhóm nguyên nhân giao nhau:18 file thiếu/không hợp lệ,31 proof/scanner,5 alias,2 video. Chưa đóng tự động dòng nào;0 trong52 được nhập thêm.5 alias đã xác định owner để trình mapping, không tự merge/xóa/chuyển loại. Từng identity/URL/current state/owner/hướng xử lý: `coverage-final-v2/blocked-52.csv` và JSON trong run.

Có140 URL tài nguyên yêu cầu còn chênh lệch với Dev, **không phải140 file vật lý bị mất**: gồm nguồn53NotScanned, byte đã cache nhưng chưa linked vào Dev (kể cả9 bài mớiTest), thiếu byte hoặc format cần review.31 URL chưa có byte; số tham chiếu có lặp và nhóm không cộng thành tổng file.2 video vẫn chỉ metadata/0file. Article3205 chưa tải PDF vì header Content-Length vượt response budget; chưa biết kích thước thật từ artifact, không suy thành404 hoặc tự tăng giới hạn.

## Chuyên mục

Giữ18 category SQL hiện có, gồm toàn bộ14 mục gốc; bài có thể thuộc nhiều mục nên không cộng các hàng thành tổng nội dung. Chưa có category nào được nghiệm thu100% nguồn. Đã quan sát đủ số trang nguồn tự khai của vài mục vẫn không thay đối chứng tổng nội dung và UAT.

| ID / chuyên mục | Có Dev theo cache | Đủ theo cache | Trang đã quan sát | Số trang lớn nhất nguồn tự khai |
|---|---:|---:|---|---:|
|47 — Tin hoạt động địa phương|19|17|1,2|11|
|48 — Quy hoạch, kế hoạch sử dụng đất|24|22|1,2|7|
|49 — Giao đất, cho thuê đất, chuyển mục đích sử dụng đất|20|19|1,2|8|
|50 — Giá đất|10|8|1,2|5|
|51 — Phát triển tài nguyên đất|17|16|1,2|5|
|52 — Thông báo|14|12|1|4|
|53 — Tin tức hoạt động|39|33|1,2|29|
|55 — Điều tra, đánh giá tài nguyên đất|13|12|1,2|2|
|56 — Công tác hoạt động Đảng, Đoàn thể|11|9|1|2|
|57 — Bồi thường, hỗ trợ, tái định cư|12|10|1|4|
|74 — Thanh tra, kiểm tra đất đai|12|11|1|2|
|75 — Xây dựng, phổ biến chính sách, pháp luật|28|27|1,2,3,4|4|
|76 — Lịch tuần|11|11|1|18|
|77 — Quy hoạch sử dụng đất|9|9|1|44|
|79 — Sổ tay điện tử đất đai|1|0|1|1|
|81 — Thống kê, kiểm kê đất đai|0|0|1|0|
|82 — Dữ liệu và Thông tin đất đai|6|6|1|1|
|83 — Đo đạc và Đăng ký đất đai|0|0|1|0|

Hai mục81/83 không bóc tách được hàng từ trang danh sách: UNKNOWN, không phải chuyên mục trống đã chứng minh. Các khoảng trang còn thiếu và kết quả parser nằm trong `category-coverage.json`. Không crawl lại toàn bộ để tạo số tổng giả.

Năm dòng alias cần review mapping, không tạo thêm bài:

| Danh tính staging | Canonical đã thuộc danh tính Dev |
|---|---|
| Article:3610 | Announcement:3610 |
| Article:3618 | Announcement:3618 |
| Announcement:3589 | Article:3589 |
| Announcement:3607 | Article:3607 |
| Announcement:3684 | Article:3684 |

Đây là bằng chứng ownership URL, chưa phải phê duyệt đổi loại/chuyên mục, hợp nhất hoặc đóng ngoại lệ.
