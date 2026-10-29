# Phase 3A — kiểm kê nguồn, 16/10/2026

**PARTIAL: công cụ và mẫu giới hạn đã chạy thật; quét diện rộng NEEDS_APPROVAL, tổng toàn nguồn UNKNOWN.** Nguồn chính là [QLDD](https://qldd.mae.gov.vn/); không lấy nội dung gdla.gov.vn vào CMS. [Báo cáo triển khai](phase-3a-crawl-report.md), [vận hành](phase-3a-runbook.md).

Session SQL Test: `9d95c26f-85a5-4a8a-a2df-61def03cf360`. Báo cáo JSON/CSV/HTML: `.local/evidence/phase3a/inventory-20261015T183702Z-7be2669b`. Các bộ đếm dưới đây thuộc session này, gồm cache nguồn thật đã có và HTTP mới; không bao gồm fixture regression.

| Chỉ số | Kết quả thực |
|---|---:|
| URL phát hiện | 512 |
| Danh tính ứng viên theo loại + mã nguồn | 270 |
| Danh tính đã kiểm chứng và bóc tách | 40 (37 baseline + 3 thêm) |
| URL đã stage riêng | 45, có alias |
| URL alias vượt một URL/danh tính đã xác minh | 45 |
| Trang có snapshot / trang chưa lấy | 89 / 312 |
| Capture / hàng snapshot SQL / hàng URL SQL | 164 / 160 / 512 |
| Collector GET đã dành ngân sách / phản hồi hoàn tất | 39 / 39 |
| Body phản hồi hoàn tất | 5.359.120 byte |
| Capture tái sử dụng | 125 = 70 capture nguồn + 55 bản sao tệp cache |
| File URL phát hiện / file hợp lệ trong kho riêng | 67 / 62 |
| Ảnh / PDF hợp lệ trong kho riêng | 50 / 12 |
| File mới tải và quét engine thật Clean | 7 ảnh JPEG |
| Bản sao cache chưa quét | 55, gồm 53 tài nguyên nội dung và 2 branding |
| Video metadata / tệp video | 2 / 0 |
| Nội dung mới nhập vào CMS Dev bởi Phase3A | 0 |

`CandidateIdentities` không phải số bài toàn nguồn. Capture không đồng nghĩa hàng SQL: snapshot chống trùng URL + SHA. 45 URL staging không phải 45 bài. Kho riêng chưa phải dữ liệu được phép xuất bản. Bộ đếm byte chỉ là body hoàn tất, không đo mọi byte trên đường truyền khi lỗi.

## Loại URL và nội dung

| Loại URL | Số |
|---|---:|
| Robots | 1 |
| Home | 1 |
| Feed | 2 |
| List | 55 |
| Detail | 339 |
| Other | 3 |
| File | 67 |
| External | 44 |

| Loại nội dung | URL ứng viên | Danh tính bóc tách |
|---|---:|---:|
| Article | 272 | 29 |
| Announcement | 11 | 2 |
| StaticPage | 5 | 2 |
| LegalDocument | 35 | 3 |
| DraftDocument | 14 | 2 |
| Video | 2 | 2 |

## Danh mục và độ bao phủ

Giữ 14 chuyên mục cũ và các mục nguồn bổ sung, không gộp để giảm việc. Bảng dưới là quan sát từ snapshot nhiều thời điểm; không cộng số ID của các mục thành tổng nguồn vì bài có thể thuộc nhiều mục. `Trang khai báo` do HTML nguồn cung cấp; không suy tổng từ maxID. `Trang có snapshot` đếm URL, có thể có alias của cùng trang.

| ID | Chuyên mục | Trang có snapshot | ID quan sát | Danh tính stage | Trang khai báo |
|---:|---|---:|---:|---:|---|
| 75 | Xây dựng, phổ biến chính sách, pháp luật | 5 | 37 | 5 | 4 |
| 83 | Đo đạc và Đăng ký đất đai | 2 | 0 | 0 | 0 |
| 82 | Dữ liệu và Thông tin đất đai | 2 | 6 | 2 | 1 |
| 81 | Thống kê, kiểm kê đất đai | 2 | 0 | 0 | 0 |
| 55 | Điều tra, đánh giá tài nguyên đất | 2 | 15 | 1 | 2 |
| 49 | Giao đất, cho thuê đất, chuyển mục đích sử dụng đất | 2 | 20 | 2 | 8 |
| 48 | Quy hoạch, kế hoạch sử dụng đất | 2 | 20 | 2 | 7 |
| 53 | Tin tức hoạt động | 2 | 16 | 6 | 29 |
| 51 | Phát triển tài nguyên đất | 3 | 20 | 2 | 5 |
| 47 | Tin hoạt động địa phương | 2 | 18 | 2 | 11 |
| 50 | Giá đất | 1 | 10 | 2 | 5 |
| 57 | Bồi thường, hỗ trợ, tái định cư | 1 | 10 | 2 | 4 |
| 74 | Thanh tra, kiểm tra đất đai | 1 | 9 | 2 | 2 |
| 56 | Công tác hoạt động Đảng, Đoàn thể | 1 | 10 | 2 | 2 |
| 52 | Thông báo | 1 | 10 | 2 | 4 |
| 79 | Sổ tay điện tử đất đai | 1 | 1 | 1 | 1 |
| 77 | Quy hoạch sử dụng đất | 1 | 10 | 0 | 44 |
| 76 | Lịch tuần | 1 | 10 | 0 | 18 |

## Bằng chứng phân trang và giới hạn

- Chuyên mục75: bốn trang mới nhận về các tập ID khác nhau, kết thúc ở trang4/4; quan sát 37 ID trong cửa sổ này. Trang1 có hai alias; không cộng thành năm trang nghiệp vụ. Trang1 bắt đầu3692/3688, trang2 là3642/3643, trang3 là3173/3301, trang4 là3021/3025. JSON lưu URL, danh sách ID và hash raw.
- Danh sách pháp luật: snapshot cũ khai báo136 bản ghi; phản hồi mới khai báo138,10 trang. Mới quan sát trang1/2/10, chưa lấy trang3–9; không báo đã chuyển138 văn bản.
- Dự thảo: cache khai báo10 bản ghi/1 trang; video2 metadata/1 trang. Đây là số do trang đã lưu cung cấp, không chứng minh nguồn hiện tại không còn mục khác.
- Chuyên mục83/81: HTML không có hàng truy cập được. Browser chỉ dùng ở phần này: `.local/evidence/phase3a/browser-20261015T180204746Z-87eaf5bd`. 20 GET giới hạn, 48 request bị chặn; trang83 HTTP200 nhưng có lỗi plugin phân trang trong phiên giới hạn tài nguyên; chưa biết lỗi gốc ở nguồn hay do giới hạn khảo sát. Trang81 bị giới hạn ngân sách. Kết quả **PARTIAL**, không kết luận hai mục có0 bài. Attempt DNS đầu thất bại có transcript công cụ nhưng chưa tạo raw; không dựng lại raw giả.
- robots.txt, sitemap.xml, rss.xml đã probe và trả404. Đây không chứng minh mọi sitemap/RSS đều không tồn tại, cũng không phải quyền quét không giới hạn. Menu cơ cấu có `#`; trang lãnh đạo/giới thiệu đã có snapshot riêng, các trang chỉ đạo/adapter khác còn cần nguồn hoặc export được phép.
- Collector39 GET, browser20 GET và một lần khảo sát bằng web tool được ghi riêng; số HTTP nội bộ web tool UNKNOWN. Không gộp chúng thành39 request tổng.

## Tài nguyên và ngoại lệ

File URL:47 JPG,13 PDF,5 PNG,1 MP4,1 DOC. Kho hợp lệ:46 JPG +4 PNG +12 PDF. Bảy JPEG mới có exact-byte proof Microsoft Defender thật; 55 bản sao cache vẫn NotScanned. Năm URL ValidationFailed gồm tệp sai định dạng và trang chưa có adapter; raw giữ private để điều tra. DOC cũ chưa được reader hỗ trợ; không gọi là chuyển thành công. MP4 trỏ gdla.gov.vn ngoài allowlist nên không tải; video còn lại không có tệp dùng được.

8 ngoại lệ cũ được giữ với trạng thái nguyên:33 lịch sử /10 entry mở /8 vấn đề distinct. Session có8 URL hiện lỗi riêng; hai tập có thể chồng nhau nên không cộng thành16. Lịch sử lỗi parser prototype vẫn lưu, kể cả khi adapter sau đó được sửa. `LegacyExceptions`, `NewIssueHistory`, `CurrentErrorUrls` trong JSON cho phép đối soát từng URL. Không tự đóng ngoại lệ cũ khi replay mới thành công.

Nguồn chưa bao phủ gồm phần lớn các trang chưa duyệt của18 mục,7 trang pháp luật, ứng viên chưa fetch, mục83/81, cơ cấu/chỉ đạo và tài nguyên ngoài host được phép. Cần PO/source-owner duyệt crawl rộng và cung cấp tổng/export đối chứng. Không có cơ sở tính100% toàn nguồn.
