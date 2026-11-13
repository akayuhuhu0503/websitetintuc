# Rà soát CMS ngày 13/11/2026

Phạm vi: đối chiếu toàn bộ 26 mục trong `Ra_soat_CMS_GDLA_2026-11-13.md` do chủ dự án cung cấp, trên nền `main` sau PR #10. Đây là thay đổi ứng dụng và kiểm thử cô lập; chưa nghiệm thu trên IIS vận hành.

## Kết quả theo báo cáo

| Mục | Thay đổi |
|---|---|
| CMS-01, 03 | PDF/Word/Excel tải qua dịch vụ tài liệu, tự lưu nháp và gắn tệp ngay trong editor. Hiện tên, loại, dung lượng và xem/tải/gỡ cả sau khi mở lại bài. Thư viện riêng của bài giúp dùng lại tệp khi mất kết nối; thử lại không tạo bản sao và không lấy quyền tệp của bài khác. |
| CMS-02 | Banner dùng bộ chọn ảnh và tải ảnh ngay tại form; không điều hướng người dùng tới JSON thư viện. |
| CMS-04 | Tải, tạm dừng, tiếp tục và theo dõi video tại editor; video đủ kiểm định tự gắn vào đúng bản nháp, không tự gửi duyệt hay công bố. |
| CMS-05, 08, 25 | Tách trạng thái bài, xử lý tệp và công bố. Nhãn trung thực về chưa quét antivirus; phần chẩn đoán kỹ thuật thu gọn, chỉ người có quyền cấu hình xem được. |
| CMS-06 | Hiển thị thông tin còn thiếu từ chính quy tắc máy chủ. Nháp chưa hoàn chỉnh vẫn lưu được; thông báo chỉ có PDF hợp lệ không bị ép thêm ảnh/nội dung giả. Video dùng trạng thái kiểm định đầu ra, không coi bản gốc cách ly là lỗi. |
| CMS-07 | Tên video cho phép tiếng Việt, khoảng trắng và nhiều dấu chấm; vẫn kiểm tra đuôi cuối, MIME, ký tự nguy hiểm và đường dẫn. |
| CMS-09 | Có nút “Duyệt và xuất bản” khi có cả hai quyền; giữ hai lệnh nghiệp vụ, hai audit và cơ chế chống lặp. Nếu chỉ duyệt thành công, báo rõ và mở lại đúng phiên bản để xử lý tiếp. |
| CMS-10 | Hẹn giờ ngay trong editor theo giờ Việt Nam, gắn đúng phiên bản; thử lại cùng lịch không tạo trùng, không âm thầm thay lịch khác. Phân biệt ngày hiển thị với thời điểm hẹn đăng. |
| CMS-11, 12 | Tiêu đề/sapo/nội dung lên trước; thông tin theo mẫu và thao tác ít dùng thu gọn. Tách lưu, gửi duyệt, đăng và thao tác nguy hiểm. |
| CMS-13 | Autosave đầu tiên giữ nguyên trang/con trỏ; xem trước lưu thành công rồi mới mở đúng bản, không mở bản cũ nếu lưu lỗi. |
| CMS-14 | Ảnh vừa tải được chọn ngay theo thao tác ảnh đại diện/chèn nội dung. |
| CMS-15 | Menu ba nhóm Nội dung/Tương tác/Quản trị theo quyền, trạng thái đang mở rõ; có lịch tuần và đổi mật khẩu. |
| CMS-16 | Một nút tạo nội dung; tab nháp/chờ duyệt/hẹn giờ/đang công khai/thùng rác, số lượng theo đúng phạm vi người dùng; bộ lọc chi tiết giữ đầy đủ trạng thái. Liên kết quay lại giữ bộ lọc với URL nội bộ được kiểm tra; không lưu tìm kiếm nhạy cảm vào browser storage. |
| CMS-17 | Chọn ngày để xác định tuần và khoảng ngày; thêm/sao chép buổi; nhập Word ngay tại editor, xem trước rồi rà soát. Danh sách hiển thị tuần, khoảng thứ Hai–Chủ nhật và phiên bản đã phát hành thực tế. Giữ dòng nháp chưa đủ trường và giới hạn 100 buổi hiện có. |
| CMS-18 | Menu chỉ hiển thị trường đích tương ứng; giá trị không hoạt động không trở thành liên kết được lưu. |
| CMS-19 | Tìm/phân trang ảnh và nội dung bằng hộp chọn, giữ các trường đang nhập, ngày hiển thị và liên kết. |
| CMS-20 | Nêu rõ lưu nháp và công bố cấu hình toàn website; không giả công bố riêng một banner khi workflow áp dụng cả phiên bản cấu hình. |
| CMS-21 | Hộp thư chọn bài bằng tiêu đề trong phạm vi được phép; thông tin cá nhân được ghi rõ; thanh xử lý nhiều chỉ hiện khi có lựa chọn và theo quyền. |
| CMS-22 | Bàn giao mật khẩu tạm một lần trong CMS qua HTTPS, xác thực lại + CAPTCHA, thời hạn 10 phút, mã hóa và ràng buộc người quản trị/tài khoản/security stamp. Không lưu plaintext trong luồng CMS. |
| CMS-23 | Gợi ý nhóm quyền Người viết/Người duyệt/Quản lý nội dung chỉ chọn checkbox; người quản trị vẫn kiểm tra và lưu, không tự cấp thêm quyền. |
| CMS-24, 26 | Menu thu gọn trên điện thoại, bảng cuộn trong khung, bộ lọc và form responsive; đồng bộ màu, khoảng cách, nhãn, focus và thông báo. Lối đi nhanh tới soạn nội dung/tệp/hoàn tất bài có điều hướng bàn phím, không tạo nút gửi trùng. |

## Kiểm chứng

- Release Web và các runner tập trung: không lỗi/cảnh báo.
- `LandPortal.CmsAccountTests`: 30/30 trên HTTPS thật và LocalDB mới, schema 18; kiểm tra mã hóa, hết hạn, quyền, CSRF, xác thực lại, stamp, chỉ dùng một lần và thử đồng thời.
- `LandPortal.CmsEditorTests`: HTTP Kestrel thật, Identity/CAPTCHA và SQL Server LocalDB mới, schema 18. Nhóm upload/workflow **35 kiểm tra PASS**; nhóm video/controller **28 PASS**; màn hình/readiness/liên kết **15 PASS**; truy vấn lịch tuần thực **1 PASS**. Mỗi lần chạy có thêm 18 kiểm tra dựng host/Identity, không cộng lặp vào các số này. Các nhóm được chạy riêng sau khi sửa fixture, không tuyên bố là một lần chạy toàn bộ liên tục.
- Browser cô lập dùng HTML Razor thực, Jodit/CSS/JavaScript thực và endpoint giả lập: editor/lịch tuần **36/36 PASS**; shell/picker **20/20 PASS**; video **12/12 PASS**. Các nhóm browser không thay thế kiểm tra SQL/quyền/file thật ở trên. Responsive 320/390/768/1366px; thêm kiểm tra bảng lịch thực tại 320px và mô phỏng CSS zoom 200%, không chụp ảnh/quay video.
- Không chạy lại các bộ E2E 48/60 không liên quan. Không dùng tài khoản thật, không sửa SQL vận hành, LAN, `.env`, 703 media hoặc IIS.

Lỗi tìm được trong quá trình kiểm tra đã xử lý: sự kiện chọn file làm autosave đánh dấu nháp bẩn khiến upload không bắt đầu; callback tài liệu hoàn tất trong lúc gắn video làm mất cờ chưa lưu; thử lại duyệt/xuất bản thành công dùng nhầm rowversion hiện tại; bản gốc video cách ly bị hiểu sai; video chưa chọn tệp chưa có thông báo sớm; metadata công khai ẩn vẫn bị gửi; dòng lịch chưa đủ trường bị ẩn; menu tablet tràn ngang; TempData chuyển chuỗi GUID thành kiểu GUID làm mất khu vực bàn giao mật khẩu.

Fixture cũng đã sửa: đọc đúng envelope JSON của thư viện, giữ cùng byte PDF khi thử trùng, gửi header AJAX để kiểm tra 403, không đặt Tags/Featured cho loại Video, dùng đúng quyền quản lý video và đúng tên bộ lọc categoryId. Những lần dừng vì fixture không được tính là toàn bộ test PASS. Cách chạy và phạm vi: [README của bộ kiểm tra](../tests/LandPortal.CmsEditorTests/README.md).

## Giới hạn cần hiểu đúng

- Word/Excel thông thường là tài liệu đính kèm; không tự chuyển toàn bộ bố cục Word thành HTML. Riêng lịch tuần dùng bộ đọc bảng Word hiện có, vẫn yêu cầu cán bộ rà soát nội dung và quyền công khai bản gốc.
- Video gắn tự động khi editor mở hoặc mở lại. Hàng đợi xử lý vẫn theo cơ chế Web đã có; IIS dừng thì không hứa xử lý liên tục. Các kiểm tra mới không thay thế kiểm định FFmpeg hoặc cho video đang cách ly thành Ready.
- Bộ kiểm tra SQL video mới dùng byte/hash giả lập riêng để thử ràng buộc attach, phiên bản và quyền; không tuyên bố đó là lần chạy FFmpeg thực mới. Pipeline chuyển mã không được sửa trong đợt này.
- Poster vẫn do bộ xử lý hiện có tạo; chưa bổ sung chọn khung hình hoặc thay poster thủ công. Đề xuất này nằm ngoài các lỗi gắn tệp/quy trình đã sửa và sẽ cần kiểm định đầu ra riêng nếu triển khai.
- Người chỉ có quyền duyệt không có nút xuất bản. “Duyệt và xuất bản” có thể duyệt thành công nhưng bị chặn ở bước phát hành nếu điều kiện thay đổi; màn hình phản ánh chính xác phần đã thực hiện.
- Mật khẩu tạm chỉ được xem một lần. Nếu phản hồi HTTPS bị mất sau khi đã tiêu thụ, không đọc lại; quản trị viên có thể chủ động reset theo quy trình hiện có. Bàn giao trước khi đổi nhóm quyền/security stamp. Chi tiết: [cms-account-handoff.md](cms-account-handoff.md).
- Chưa kiểm thử với cán bộ mới hoặc nghiệm thu trên IIS thật; không tuyên bố tất cả thao tác không cần đào tạo. Sau khi PR được duyệt chỉ cần quy trình cập nhật WebBundle hiện có, không thêm cấu hình máy chủ.
- Kiểm tra bàn phím có phạm vi menu/hộp chọn/lối đi nhanh; chưa đánh giá toàn bộ với trình đọc màn hình, zoom gốc của trình duyệt hay chứng nhận WCAG.

## Thao tác cho cán bộ

1. **Bài viết → Tạo nội dung**: nhập tiêu đề, chuyên mục và nội dung; nháp được tự lưu. Chọn ảnh hoặc tài liệu ngay tại bài.
2. Nếu có video, chọn tệp trong **Video của bài viết**, chờ xử lý và gắn xong. Mở xem trước, bổ sung các mục còn thiếu, bấm **Gửi duyệt**.
3. Người duyệt mở **Chờ duyệt**, xem trước rồi **Chỉ duyệt**, **Duyệt và xuất bản** nếu được cấp quyền, hoặc **Trả bài kèm lý do**. Hẹn giờ chọn thời điểm ngay tại bài; sửa/hủy lịch ở Lịch xuất bản.

## Cập nhật sau khi duyệt PR

Giữ nguyên `.env` và kho dữ liệu. Dùng `git pull` rồi `powershell -NoProfile -File scripts/build-for-iis.ps1`, cập nhật nguyên thư mục `web` theo quy trình hiện có. Không cần migration hay cấu hình Windows mới. Chưa thực hiện các bước này trên IIS trong đợt sửa.
