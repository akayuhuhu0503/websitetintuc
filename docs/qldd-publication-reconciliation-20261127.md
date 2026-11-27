# Đồng bộ QLDD: điều kiện xuất bản và đối soát lịch

Phạm vi: một bản sửa trên main sau PR #30 (`c7412ba`). Không ghi CMS/SQL vận hành, không đổi `.env`, migration hoặc triển khai IIS. Giữ nguyên 103 bài công khai. Các số dưới đây là kết quả kiểm tra bản sao nguồn và môi trường Test, không phải số bài mới đã đăng.

## Điều kiện xuất bản

- Bài tin có SourceRecord và checkpoint QLDD khớp định danh/hash được thiếu tác giả hoặc ảnh đại diện **khi nguồn thực sự không cung cấp trường đó**. Dán URL vào ô nguồn dẫn không đủ để hưởng ngoại lệ. Không gán tên quản trị viên, không bịa sapo.
- Nguồn có tác giả/ảnh vẫn phải bổ sung thông tin tương ứng. Giữ sapo, nội dung, ngày nguồn, phân quyền và workflow. Giao diện ẩn tác giả rỗng; dùng các thẻ bài không ảnh hiện có. Chưa có ảnh mặc định chuyên mục được xác nhận nên không tự chọn ảnh minh họa.
- Thông báo chỉ có ảnh được tiếp nhận khi tất cả ảnh trong nội dung là media nội bộ đã kiểm định, không cách ly, chính sách cho phép phục vụ, byte/length/SHA khớp và giải mã được. Không tạo PDF hay nội dung chữ giả; không đổi `NotScanned` thành `Clean`.

Đối chiếu 20 nháp đã lưu: 19 nháp hết rào cản trường bắt buộc nêu trên (12 thiếu ảnh, 5 thiếu tác giả, 1 thiếu cả hai, 1 thông báo có ba ảnh). Bài Hà Nội nguồn **3699** còn phải gắn ảnh chính thức vào ContentId `0563a7ad-a1f7-48fc-bbb0-34684b85fe3c`. Đã tải và giải mã đủ **5/5 ảnh** chính thức, giữ SHA riêng. Có phương án cho đủ 20 bài; chưa tuyên bố 20 bài đã vượt tiền kiểm CMS thực tế hoặc đã công bố.

## 120 mục cần kiểm tra

Đã phát lại offline 90 mục không phải lịch, sử dụng byte nguồn thu thập qua chính SafeSourceClient, robots, giới hạn byte và decoder hiện hành. **65/90 mục qua kiểm định tài nguyên sau sửa**; trong đó hai mục còn cần ánh xạ chuyên mục nguồn 80, tức **63 mục hết chặn kỹ thuật mà không cần ánh xạ mới**.

| Nhóm ban đầu | Số mục | Kết quả |
|---|---:|---|
| MIME JPEG bị khai báo PNG | 5 | 5 qua giải mã JPEG thật; chỉ dung nạp cặp MIME đã xác minh trên hai media host QLDD/MAE |
| URL ngoài phạm vi | 19 | 9 dùng được bản sao first-party trong `src`; 10 chưa có bản sao được phép |
| Cấu trúc/định danh/tệp chung | 57 | 49 PDF qua kiểm định; 2 trang nhận diện đúng category 80, chờ ánh xạ; 6 mục còn ngoại lệ |
| Vượt dung lượng | 6 | Giữ nguyên giới hạn; chưa tiếp nhận các tài nguyên vượt 10 MiB/tệp |
| Tài nguyên khác | 3 | Đã sửa tiếp tục tải qua nhiều lượt cho 3130, nhưng PDF cuối sai định dạng; 1779 và 3623 vẫn chưa đạt |
| Lịch có ứng viên CMS | 27 | 25 cặp cùng đơn vị/năm/tuần theo tiêu đề đã đối chiếu; 2 cặp mâu thuẫn. Không kết luận các PDF khác hash là bản trùng |
| Lịch thiếu năm trong tiêu đề | 3 | Xác minh PDF gốc tuần 36–38/2025; cho phép dùng bằng chứng PDF thay vì ép sửa tiêu đề |

### Những mục còn cần xử lý

- **URL ngoài phạm vi (10):** 1786, 1787, 3117, 3295, 2578, 2585, 3282, 3291, 3280, 3283. Cần bản sao hợp lệ do nguồn cung cấp hoặc biên tập viên tiếp nhận qua CMS; không mở rộng whitelist, bỏ DNS/SSRF hay theo redirect ra ngoài.
- **Dung lượng (6):** 3735, 3747, 3239, 3167, 3174, 3718. 3747 đồng thời có PDF vượt ngân sách phân tích. Không tải thêm toàn bộ tệp quá giới hạn để lách giới hạn nguồn.
- **Ngân sách phân tích PDF:** 3707, 3714, 3802 (và 3747 thuộc nhóm dung lượng). Các PDF có nhiều đối tượng, vượt 100.000 token của luồng managed. Không kết luận là PDF hỏng, không tự tăng ngân sách.
- **PDF 3261:** có `OpenAction` điều hướng tự động; chính sách hiện hành vẫn từ chối. Không sửa byte hay làm mất chữ ký để né kiểm tra.
- **3136:** hai URL mang đuôi PDF nhưng byte không có định dạng PDF hợp lệ.
- **3130:** đã kiểm tra thêm ba JPEG cuối thành công; tệp `41-2024-QĐ-UBND.pdf` trả 9.532.812 byte, SHA `a62017f2ee2921c3996f94f82b9055846577e0e3c7d9503001fe63dcd3c28a25`, không có định dạng PDF. Không đánh dấu mục này đã xử lý xong chỉ vì đã sửa hàng đợi.
- **1779:** tài nguyên `.html` không thuộc loại tài liệu tiếp nhận. **3623:** byte ảnh không khớp định dạng. **2024:** URL không cung cấp tiêu đề bài được xác minh; không tạo bài giả.
- **3781, 3788:** breadcrumb chính thức dùng category **80**, không phải 83. Code bổ sung nhận diện riêng; Admin chọn ánh xạ trong Đồng bộ QLDD. Không tự thay cấu hình hoặc nội dung.
- **Lịch 3696 → `c9ab211d-6788-4bd1-8b62-473b2fe44060`:** CMS ghi tuần 43 nhưng khoảng 26/10–01/11/2026 thuộc tuần 44.
- **Lịch 3716 → `7f267250-5d4f-4bba-8ec3-04e1218775f7`:** CMS ghi tuần 44 nhưng khoảng 02–08/11/2026 thuộc tuần 45. Hai trường hợp này chờ quyết định nội dung, không tự sửa tiêu đề hoặc nối dựa trên tên gần giống.

## Nối định danh và PDF lịch tuần

Trang xem trước có thao tác **Nối với hồ sơ này**. Máy chủ kiểm tra lại quyền hiện tại/phạm vi chuyên mục, rowversion, source hash, asset hash, cùng đơn vị/năm/tuần, category 76, bản ghi nguồn/alias chưa bị chiếm. Ghi audit và command receipt trong cùng transaction; lặp command không tạo thêm định danh. Một lượt tải đang chạy không được ghi đè checkpoint vừa nối.

Nối chỉ bổ sung định danh. Nếu hash PDF chưa có, bài chuyển sang xử lý cập nhật, rồi dùng thao tác CMS hiện có tạo **bản sửa nháp**. Giữ nguyên bản công khai, nội dung cán bộ đã sửa, ngày và PDF cũ. Bài đang có bản sửa/lịch hẹn không bị ghi đè.

27 cặp có tổng 308 URL PDF nguồn; đã đối chiếu PDF nền của mỗi cặp với attachment CMS và **không có hash nền trùng**. 308 là tổng URL, **không phải số PDF còn thiếu**. Chưa tải toàn bộ các phiên bản cập nhật để kết luận số tệp cần bổ sung. Sau triển khai, runner tải có giới hạn, so hash và bổ sung qua nháp; không tự công bố.

Ba lịch nguồn 3361/3362/3363 dùng header PDF đã kiểm định, SHA/length khớp để xác minh tuần 36/37/38 năm 2025. Không lấy năm từ ngày đăng hoặc đoán theo tên file. Giữ nguyên tiêu đề và ngày nguồn. Đây vẫn là bài lịch PDF `StaticPage`, không tạo `WeeklyScheduleRelease`. Giới hạn còn lại: tiêu đề không có năm vẫn xuất hiện trong kho lịch công khai sau công bố, nhưng bộ lọc tuần công khai hiện dựa vào tiêu đề/metadata lịch cấu trúc; không tự đổi tiêu đề hoặc biến bài PDF thành lịch cấu trúc để có nhãn tuần.

## An toàn và kiểm thử

Managed PDF dùng lại bộ kiểm tra cấu trúc thụ động: chữ ký, thuộc tính bố cục, liên kết HTTP(S) và điều hướng nội bộ khi bấm không bị đánh đồng với mã thực thi. Giữ strict parser, 100.000 token, độ sâu 64, giới hạn trang/dung lượng; không mượn quyền TrustedInternal hoặc chế độ phục hồi trang. Chỉ cho phép `NeedAppearances=true` khi form không có trường. Script, hành động tự chạy, Launch, XFA, tệp nhúng và form tương tác vẫn bị từ chối. Không thay byte tài liệu, không chứng nhận tính hợp lệ mật mã của chữ ký.

- Release Web/Worker: PASS, 0 lỗi/cảnh báo.
- HTTP/SQL LocalDB riêng, schema 18: **42/42 PASS**; kiểm tra quyền/CSRF, trường nguồn, ảnh thông báo, nối lịch, idempotency, bản sửa không đổi bản công khai, lịch thiếu năm dùng PDF, tiếp tục hàng đợi và xung đột checkpoint.
- 29 kiểm tra chính sách tài nguyên PASS; 22 kiểm tra cấu trúc PDF PASS; 11 kiểm tra parser bằng trang nguồn đã lưu PASS.
- Không full regression, không thử lại upload/import của các đợt khác. Không sửa 103 bài công khai, không upload ảnh Hà Nội hoặc nối lịch vào CMS vận hành trong lượt này.

## Sau khi duyệt và triển khai một lần

Giữ quy trình `git pull --ff-only` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → triển khai nguyên `web` gồm `.runtime`. Không sửa JSON/manifest sau Publish.

Sau khi xác nhận bản mới trên IIS: bổ sung 5 ảnh Hà Nội vào bài có sẵn; rà tiền kiểm 20 nháp; ánh xạ nguồn 80 nếu áp dụng; thử lại những mục đã hết lỗi bằng CMS; nối các cặp lịch đủ chứng cứ và tạo bản sửa PDF. Các thao tác công bố vẫn qua quyền và workflow hiện hành. Không chạy lại importer hoặc tạo lại bài đã có.
