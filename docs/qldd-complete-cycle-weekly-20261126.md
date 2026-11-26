# Đồng bộ QLDD: vòng kiểm tra đầy đủ và lịch tuần

## Phạm vi và nguyên nhân

Bản này chỉ thay công cụ Đồng bộ QLDD. Không migration, không thay cấu hình IIS/private, không thay bộ kiểm định ảnh/PDF, không tự tạo hoặc công bố nội dung khi khởi động.

- Bộ lọc ID trước đây chỉ nhận URL gốc `/ten-bai-3791.htm`; các URL thực tế dạng `/tin-tuc-hoat-dong/ten-bai-3791.htm` bị loại trước khi đưa vào hàng chờ. Bổ sung dạng đường dẫn này, giữ kiểm tra host, giao thức, cổng và canonical ID.
- Lượt cũ chỉ đọc hai chuyên mục và ba chi tiết; bài đã phát hiện có thể không được chọn tiếp. Vòng mới lưu danh sách chuyên mục, trang đang đọc, bài đã thấy và bài còn chờ vào checkpoint hiện có.
- Category 76 bị loại hoàn toàn; nay có luồng riêng cho lịch PDF của Lãnh đạo Cục. Category 77/79 tiếp tục bị loại.
- Danh sách cũ trộn bài đã chuyển CMS với bài cần xử lý; nay tách Chờ xử lý, Cần kiểm tra, Lịch sử, kèm số lượng và liên kết bản biên tập.
- Sapo trống được lấy từ đoạn mở đầu có thật; sapo đã có trên bản biên tập được giữ nguyên.

## Đối soát chỉ đọc ngày 26/11/2026

Phạm vi lấy mẫu: trang đầu của 14 chuyên mục tin tức, Thông báo và Lịch tuần; kiểm tra riêng năm ID được yêu cầu và ba lịch mới nhất. Tổng 25 GET nguồn, không tải hàng trăm bài/PDF. Đọc danh sách CMS và metadata nguồn của các bài liên quan, gồm cả nháp. Không bấm kiểm tra nguồn, chuyển CMS hoặc sửa cấu hình thật.

| Kết quả định danh | Số bài |
| --- | ---: |
| URL/SourceId trong metadata CMS khớp trực tiếp | 5 |
| Manifest nhập cũ có SourceId/canonical/ContentId, ContentId hiện còn trong CMS | 57 |
| Chưa tìm thấy định danh trong phạm vi đọc | 70 |
| Tổng SourceId duy nhất đã đối chiếu | 132 |

Năm ID **3791, 3779, 3777, 3772, 3769** đều chưa tìm thấy trong phạm vi đọc. Các tên gần giống không được coi là bằng chứng trùng. Bài 3761 đã có bản nháp nên không được nhập lại chỉ vì chưa có trang công khai.

**70 là số chưa nối được định danh, không phải xác nhận chắc chắn 70 bài mới.** Trong đó có 6 lịch tuần: ba lịch 46–48 chưa thấy trên CMS; ba lịch 43–45 cần đối chiếu với hồ sơ Excel đã nhập nhưng thiếu SourceId. Hệ thống giữ các ứng viên trùng tuần/tiêu đề để rà soát, không tự tạo hoặc nối sai bài.

| Nguồn Lãnh đạo Cục | SourceId | Liên kết PDF trong HTML |
| --- | --- | ---: |
| Tuần 46/2026 | 3799 | 11 |
| Tuần 47/2026 | 3800 | 10 |
| Tuần 48/2026 | 3801 | 4 |
| Tổng ba lịch cần tiếp nhận | | 25 |

GDLA có lịch **Cục đến tuần 45/2026**; lịch tuần 46 cập nhật lần 02 đang có là của **Lãnh đạo Trung tâm**, không phải lịch Cục. Dòng tuần hiện tại trên trang `/lich-tuan` không chứng minh đã có bài tuần đó. 25 ở bảng là liên kết nguồn cần tiếp nhận/kiểm định, chưa phải 25 PDF đã tải hoặc nhập thành công.

Không tuyên bố đã kiểm kê toàn bộ lịch sử nguồn hoặc mọi bài từ 01/11. Bằng chứng chi tiết được giữ ngoài Git tại `GDLA-QLDD-AUDIT-20261126` (audit, reconciliation, metadata CMS chọn lọc); không chứa cookie/mật khẩu.

## Cơ chế mới và giới hạn

- Một lần **Kiểm tra bài mới ngay** tạo/tiếp tục vòng kiểm tra tất cả chuyên mục đã ánh xạ. Mỗi lát đọc một trang danh sách, tối đa ba bài mới và một bài lịch sử. Lát tiếp theo tự chạy sau khoảng 20 giây khi Web còn hoạt động; không cần Worker Windows riêng.
- Giữ tối đa 32 request/lát, thời hạn 100 giây, robots, DNS/SSRF, redirect, TLS và lease. Tối đa 5 trang gần nhất/chuyên mục; giao diện cảnh báo khi nguồn còn trang lịch sử ngoài phạm vi này.
- Checkpoint lưu SQL qua service hiện có, không cần schema mới; tạo scope mới sau restart vẫn tiếp tục. Khi IIS nghỉ/dừng, tiến trình nền không chạy liên tục; tiếp tục khi Web hoạt động trở lại.
- Hàng chờ hoạt động tối đa 200 bài. Khi đầy, giữ trang chưa xử lý, tiếp tục xử lý backlog và chờ 5 phút; cán bộ cần chuyển/bỏ qua các mục để giải phóng sức chứa. Không lặng lẽ bỏ bài vượt giới hạn.
- Nguồn lỗi giữ chờ 15/30/45 phút, tạm dừng sau ba lần. Không tự xóa lỗi bằng SQL. Lỗi từng tài nguyên vẫn ở Cần kiểm tra.
- Lịch PDF được đọc tối đa 12 tài nguyên/lát rồi tiếp tục; không dừng ở PDF thứ 12. Giữ giới hạn hiện có 10 MiB/tệp và 40 MiB/bài; quá giới hạn sẽ báo kiểm tra, không bỏ kiểm định.
- Mỗi vòng đọc lại PDF lịch để phát hiện cùng URL nhưng SHA khác. Tệp cùng hash trong bản hiện có được tái sử dụng; PDF phiên bản mới được bổ sung, giữ các PDF trước đó.
- Lịch dùng `StaticPage`/chuyên mục 76 như hồ sơ Excel, ngày nguồn vào `Metadata.PublicationDate`. Tuần/năm đã kiểm tra lưu trong checkpoint; nhãn/lọc công khai dùng tiêu đề lịch hiện có. Không tự tạo `WeeklyScheduleRelease` hoặc giả các dòng lịch bảng.
- Với bài đã công khai, nút **Tạo bản sửa nháp để bổ sung PDF** kiểm tra lại quyền, rowversion, revision và hash. Không ghi đè bản đang biên tập hoặc đang hẹn; không thay bản công khai. PDF chỉ công khai qua workflow duyệt/xuất bản bình thường.

## Kiểm thử tập trung

Chạy trên LocalDB mới, tài khoản giả, HTTP/CSRF thật; không SQL vận hành. Fixture replay ba HTML lịch nguồn đã lưu, tài nguyên PDF kiểm thử được tạo riêng, không tuyên bố đã quét/tải 25 PDF nguồn thật.

```powershell
powershell -NoProfile -File scripts/test-source-sync.ps1 -CycleOnly -SavedQlddSnapshot <thu-muc-HTML-da-luu> -DotnetPath <dotnet.exe>
```

Thư mục fixture có `detail-3799.html`, `detail-3800.html`, `detail-3801.html`. Lượt cuối `source-sync-LPSync_8f1e8d3a8044`: **58/58 PASS**, gồm kiểm tra bài đã nhập từ trước không chiếm ba suất đọc bài mới; Release Web/Worker **0 lỗi, 0 cảnh báo**. Đã dừng LocalDB test. Không cộng các lần chạy chẩn đoán vào tổng PASS. Không chạy full regression/upload/OCR/import nguồn.

## Sau khi review và cập nhật IIS

Quy trình cũ: `git pull --ff-only` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → triển khai nguyên `web` gồm `.runtime`; không sửa JSON/manifest sau Publish.

Trong CMS, kiểm tra ánh xạ các chuyên mục cần đồng bộ, ánh xạ **nguồn 76 → Lịch tuần GDLA 76**, rồi bấm kiểm tra một lần. Không ánh xạ 76 vào Tin tức hoặc Tài liệu. Nếu nguồn đang tạm dừng do lỗi mạng, chờ hết thời gian retry và kiểm tra kết nối trước khi tiếp tục.

Theo dõi tiến độ và ba tab. Các lịch cũ thiếu SourceId hoặc lệch tuần giữ Cần kiểm tra; không tự nhập lại. Chuyển lịch mới/bản cập nhật vào nháp, kiểm tra PDF rồi duyệt/xuất bản theo quyền. Bản này chưa deploy và chưa thay dữ liệu thật.
