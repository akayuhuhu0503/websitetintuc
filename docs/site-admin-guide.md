# Bổ sung chính thức SRS v1.2 — 23/10/2026

- Tên Anh: **Department of Land Administration**, cấu hình trong Nhận diện; fallback chính thức tập trung tại `SitePresentation`. Không sửa tên trong bài lịch sử. Logo chưa cần; bố cục có dấu nhận diện dự phòng.
- `/admin/site?section=menus`: mọi menu đều thêm/sửa/xóa được, kể cả mục nguồn; danh mục/nội dung nguồn vẫn được bảo vệ riêng. Chọn loại đích: tuyến hệ thống, chuyên mục, bài công khai, URL nội bộ, HTTP(S) ngoài hoặc nhóm. Cấu hình cha/con, vị trí, thứ tự, Hiển thị và Mở tab mới. Tắt nhóm sẽ ẩn cả nhánh; URL/tên được kiểm tra server. Không HTML/script, javascript/data, URL có credential hoặc tuyến admin/private. Không thêm Dịch vụ công đến khi có URL thật. Thư điện tử theo SRS: https://mail.mae.gov.vn; URL vẫn sửa/xóa trong SQL qua workflow.
- Lưu luôn tạo revision nháp. Xem trước rồi Áp dụng đúng revision/rowversion; cần `site.manage` và `site.publish` tương ứng, kiểm tra quyền mới từ SQL. Khôi phục tạo bản nháp mới. Helper Công khai thông tin tạo đúng ba mục FR17; xác nhận catalog bằng tài khoản có quyền trước khi cần công bố nội dung, không tự đăng hồ sơ.

## Ba tài khoản giả lập — chỉ Development Dev/Test

Tên: `dev-srs12-writer`, `dev-srs12-reviewer`, `dev-srs12-admin`. Hai tài khoản đầu có phạm vi chuyên mục được chỉ định lúc tạo; Người viết không đăng, Người duyệt có duyệt/đăng bản được duyệt/gỡ và không quản trị tài khoản/menu. Quản trị có quyền nghiệp vụ hiện hành, chỉ trên local Dev/Test. Hết hạn14ngày; lần đầu đổi mật khẩu. Giữ CAPTCHA/lockout/MFA; không bật/tắt cơ chế xác thực để tiện thử.

Lệnh bootstrap tường minh, idempotent, cần backup và đúng target. Chuẩn bị biến môi trường private trước, không ghi secret vào lệnh/tài liệu:

```powershell
rtk proxy powershell -NoProfile -File scripts/bootstrap-dev-demo.ps1 -Cli <MigrationCli.dll> -Database LandPortal_Dev -ConfirmTarget '<physical-instance>/LandPortal_Dev' -BackupReceipt <verified-receipt.json> -CategoryId <assigned-category>
```

Mật khẩu ngẫu nhiên nằm tệp `.local/credentials/*.dpapi`, mã hóa Windows CurrentUser và ACL riêng; không log/clipboard/Git. Chỉ người vận hành cùng Windows user mở qua `scripts/show-dev-demo-credentials.ps1 -Path <absolute-private-file>`: hộp thoại riêng, không output console. Nếu có tệp `.active`, đó là mật khẩu sau đổi lần đầu, file ban đầu không còn đăng nhập được. Không gửi mật khẩu qua chat.

Rerun không đổi mật khẩu/role/quyền/expiry hoặc mở khóa tài khoản đã có; trùng tên với tài khoản không mang marker sẽ dừng. Vô hiệu hóa trước vận hành: chạy cùng lệnh có `-Disable` (không cần category), hoặc khóa đúng3tài khoản qua admin. Không tự xóa tài khoản có audit. Chứng thực demo bị chặn ở Staging/Production ngay cả nếu cấu hình nhầm/copy DB; vẫn phải loại tài khoản khỏi dữ liệu bàn giao vận hành.

## Tra cứu hỏi đáp

Khách mở `/tra-cuu-ho-so`, nhập đủ mã tiếp nhận + đúng email + CAPTCHA. Sai mã/email cùng thông báo, CAPTCHA hết hạn/sai/dùng lại bị từ chối; không OTP/không cần SMTP để đọc kết quả. Không trả email/điện thoại/nội dung gốc/tệp/ghi chú nội bộ; kết quả POST không cache, câu trả lời hiển thị dạng chữ. Mã tiếp nhận phải giữ bí mật. Mục này thay quyết định khóa tra cứu ở tài liệu phiên bản cũ; workflow gửi, phân công và duyệt công khai giữ nguyên.

---

> Cập nhật BA/SRS v1.1: máy chủ vận hành chính thức thuộc TTDL, không phải máy chủ của Bộ. Giữ IIS + SQL Server + Web/Worker; hạ tầng đích chưa được cấp. Video mới tải trực tiếp lên hệ thống, không nhúng YouTube.

# Quản trị cấu hình cổng thông tin — Giai đoạn 2B

Phạm vi local, chưa production ready. Đọc `account-admin-guide.md` để quản trị tài khoản/nhóm và `scheduler-runbook.md` để vận hành lịch đăng. Không tự gán các quyền mới cho tài khoản vận hành.

## Quyền và màn hình

SRS v1.1/FR-17: tại `/admin/site?section=menus`, người có **cả quyền Biên tập cấu hình website và Áp dụng cấu hình website** xác nhận ba tên danh mục theo SRS. Sau đó thêm nhóm Công khai thông tin vào nháp, xem trước và áp dụng đúng phiên bản. Tên “Người nước ngoài định cư ở Việt Nam” đã chốt; không hỏi lại tên. Thao tác xác nhận không xuất bản bài, không tự áp dụng menu và từ chối nếu phát hiện hồ sơ đang công khai cần rà soát. Dev hiện chưa có tài khoản hoạt động đủ hai quyền; cần quản trị có thẩm quyền xử lý, không dùng SQL hoặc migration để lách. Nhãn tiếng Việt chỉ phục vụ hiển thị, các mã quyền dưới đây giữ nguyên.

`/admin/site` cần một trong `site.view`, `site.manage`, `site.publish`. `site.manage` cho lưu/khôi phục cấu hình nháp; `site.publish` cho áp dụng đúng phiên bản đang là nháp. Hai quyền không suy ra lẫn nhau và không suy ra từ `settings.manage`. Upload còn cần `media.manage` và policy môi trường. Các endpoint kiểm tra quyền hiện thời; ẩn nút không thay kiểm tra server.

Cấu hình SQL được khởi tạo **một lần** bằng lệnh CLI `site-initialize` sau migration có kiểm chứng target/backup. Web không seed lúc startup. Lệnh chạy lại trả `initialized=False`, giữ live/draft hiện có. Backfill lấy menu/hạng mục, footer và thông tin liên hệ đã có; không sửa payload bài nguồn, không tự tạo logo hay nhập lại corpus. Tệp nhận diện ngoài 53 source assets chưa được tự hợp thức hóa bằng cách gắn metadata vào SQL.

## Lưu nháp, xem trước, áp dụng và khôi phục

1. Mở vùng cần sửa: liên hệ & nhận diện, chuyên mục, menu & liên kết, banner, trang chủ hoặc danh mục văn bản.
2. Chọn **Sửa**/**Thêm mục vào bản nháp**, nhập trường theo mẫu và **Lưu phiên bản nháp**. Mỗi lần lưu tạo một revision bất biến mới, gồm toàn bộ cấu hình; public vẫn lấy live trước đó.
3. Chọn **Xem trước nháp**. Trang dùng thiết kế public với header/menu/footer của revision được chọn. Preview cần phiên đăng nhập có quyền và không cấp grant media công khai. Nội dung bài trong khối vẫn lấy published revision của bài.
4. Người có `site.publish` chọn **Áp dụng bản nháp**. Server kiểm tra exact revision, rowversion, cấu trúc, đích public, bytes media và scan policy trước khi chuyển live trong một transaction. Header/menu/footer cùng lấy một revision cho mỗi request.
5. Ở **Phiên bản**, chọn **Khôi phục thành nháp** rồi kiểm tra và áp dụng riêng. Lịch sử không bị sửa. ID danh mục phụ bổ sung sau phiên bản cũ được giữ ở trạng thái ngừng chọn để không mất tham chiếu.

Xung đột trả HTTP 409, giữ live và bản đang lưu hợp lệ. Tải lại phiên bản mới trước khi tiếp tục; hệ thống không âm thầm ghi đè. Không xem thông báo lưu nháp như đã áp dụng công khai.

## Chuyên mục và điều hướng

14 chuyên mục tin và các hạng mục nguồn luôn được giữ ID, tên gốc, slug, cấu trúc nguồn và đường tới. Mục nguồn có dấu **Mục nguồn**, không xóa/đổi/gộp. Mục đã có nội dung/lịch sử không hard-delete. Có thể sửa mô tả/thứ tự phù hợp. Chuyên mục bổ sung được lưu ở cấu hình nháp; stable Category chỉ được tạo khi áp dụng cấu hình. Slug đã dùng giữ nguyên trong 2B, tránh mất route/alias. Self-parent, chu trình, cây quá ba cấp và slug trùng bị chặn.

Menu có vị trí `main`, `footer`, `links`, `supplementary`; nhóm cha cùng vị trí. Dùng picker **Mục hệ thống/Chuyên mục/Nội dung công khai**, hoặc nhập liên kết HTTP(S) có nhãn. Đích admin/preview, URL script/data/protocol-relative/credential/backslash không được nhận. Server không fetch URL ngoài, không làm proxy hoặc open redirect. Mục nguồn chỉ đổi thứ tự hợp lệ; nút ↑/↓ dùng được bằng bàn phím. Nội dung đích bị gỡ sẽ tự biến mất khỏi menu; nhãn/ảnh draft không được lấy thay thế.

Với chuyên mục bổ sung, **Hiển thị trong điều hướng** điều khiển các menu trỏ trực tiếp tới chuyên mục sau khi áp dụng; tắt trong nháp không đổi menu live. Tuyến chuyên mục và nội dung không bị xóa. 14 mục nguồn luôn bật, không dùng cờ này để làm mất hạng mục.

## Trang chủ

Các mẫu khối có schema rõ: tin nổi bật, tin mới, thông báo, chuyên mục, văn bản pháp luật, dự thảo, video và liên kết/sổ tay. Chọn thứ tự, bật/tắt khối và số lượng 1–12. Khối Chuyên mục cần category; pin dùng content ID, lấy published revision lúc render, đúng loại của khối. Không tạo bản sao bài hoặc số lượt truy cập giả. Pin bị gỡ không hiển thị; query SQL lấy bản hợp lệ khác, không tải toàn kho về browser.

Picker nội dung/ảnh giới hạn 60 kết quả SQL; dùng từ khóa **trước khi nhập sửa**, vì tìm lại picker qua GET tải form từ bản nháp đã lưu. Nội dung đã chọn còn hợp lệ được giữ trong danh sách. UI video vẫn ghi hai metadata nguồn chưa có tệp, không tạo player hoặc gắn YouTube.

## Banner, logo và liên hệ

Chỉ nhập thông tin công khai đã được xác nhận: tên cơ quan, địa chỉ, điện thoại, đường dây nóng, email và ghi chú footer thuần văn bản. Không có ô SQL/SMTP/scanner bypass/filesystem/secret. Logo chọn từ kho riêng; không tự tạo biểu trưng cơ quan.

Banner gồm ảnh, nhãn, alt, vị trí, đích, thứ tự và mốc tùy chọn. Nhập giờ Việt Nam UTC+7; hệ thống lưu instant UTC. Mốc cuối là exclusive. Hết hạn được kiểm tra mỗi request, không cần worker chạy. Bỏ một banner không phá ảnh vẫn dùng bởi logo/bài/banner live khác; khi hết tham chiếu công khai, original không còn grant public, lịch sử private vẫn xem được theo quyền. Không hứa thu hồi bytes khách đã tải trước đó.

Upload ảnh từ máy đi qua pipeline ảnh/PDF 2A, decode thật và kho Test/Dev riêng. `-AllowUnscannedMedia` chỉ ngoại lệ Development local tường minh; tệp vẫn `NotScanned`. Production/Staging và worker không biến flag thành scan thành công. Máy quét thật chưa triển khai vẫn là blocker trước production.

## Danh mục văn bản

Tạo/sửa cơ quan ban hành, loại văn bản và lĩnh vực qua mẫu, lưu nháp rồi áp dụng. Không hard-delete ID/đổi loại danh mục đã công khai; ngừng cho chọn vẫn giữ lịch sử. Metadata bài/văn bản là snapshot thuộc revision riêng, đổi tên cấu hình không viết lại raw source hoặc bản biên tập đã xuất bản. Giá trị nguồn chưa ánh xạ giữ text gốc và trạng thái cần rà soát.

## Phạm vi còn lại

2C: tiếp nhận/kiểm duyệt câu hỏi, liên hệ, bình luận dưới bài và góp ý dự thảo; không có nút gửi thành công giả ở 2B. 2D: video upload trực tiếp từ máy và xử lý tệp. Chuyển toàn nguồn, scanner, MFA đặc quyền, restore toàn hệ, UAT và production thuộc các checkpoint sau. `RESTORE VERIFYONLY` không phải diễn tập restore toàn hệ.


SRS1.1 — nhận diện và mẫu bài

Tại /admin/site/edit?section=contact, nhập tên tiếng Anh, cơ quan chủ quản, người chịu trách nhiệm, số giấy phép và mô tả SEO từ dữ liệu Văn phòng Cục. Để trống khi chưa xác nhận; lưu nháp → preview → áp dụng đúng revision. Cần quyền site.manage/site.publish; Dev hiện không có tài khoản hoạt động đủ cả hai, không tự grant hoặc sửa SQL. Hướng dẫn menu3mục FR17 bên trên vẫn áp dụng.

Bài/thông báo mới có thể lưu nháp thiếu trường; trước gửi duyệt/đăng phải có tiêu đề, chuyên mục, sapo, ảnh đại diện, nội dung và tên tác giả. Chú thích ảnh, từ khóa và cờ tin nổi bật thuộc revision; sửa nháp không đổi public, approval cũ không dùng cho sửa mới. Ngoại lệ thiếu trường chỉ giữ đúng phần đã thiếu của revision nguồn có SourceRecord; không áp cho clone/bài mới hoặc xóa trường nguồn đã có.

Banner home từ cấu hình live được hiển thị thành slider có nút, không tự phát chuyển động; ảnh/đích/lịch hiệu lực vẫn qua ACL và workflow cũ. Thống kê là phiên ngẫu nhiên20phút, active5phút, ngày/tháng Việt Nam, SQL bền vững; không phải số người duy nhất. Không ghi IP/tài khoản/email/URL; số ban đầu tính từ thời điểm bắt đầu ghi, không nhập số giả.
