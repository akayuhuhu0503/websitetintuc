# Cập nhật IIS bằng duy nhất thư mục web

## Cập nhật thường lệ

`powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
`

Thay toàn bộ .local\publish\TTDL-IIS\web, gồm .runtime, vào IIS đang trỏ
D:\WWW_GDLA\publish\TTDL-IIS\web, trong thời gian dừng/khởi động lại ứng dụng.
Giữ bản web cũ ngoài webroot để rollback. Không cần chạy script cấu hình bổ sung.

.env vẫn là đầu vào duy nhất, cấu hình một lần. Build tạo snapshot cấu hình và
manifest/hash chung. Không sửa uat.json/manifest sau build hoặc trộn DLL hai bản.
Gói web có secret, chỉ chuyển bằng kênh riêng. IIS và middleware từ chối HTTP tới
.runtime/.env; wwwroot không chứa cấu hình bí mật.

## Dữ liệu và xử lý

- Giữ nguyên TTDL-IIS\private\{media,snapshots,temp,keys} hoặc các đường dẫn
  tùy chỉnh đã cấu hình. Không chuyển/xóa/ghi đè dữ liệu trong lần cập nhật.
- Web thực hiện upload CMS ngay theo chính sách ảnh/tài liệu đã duyệt, giữ kiểm định
  định dạng, cấu trúc, dung lượng, quyền và trạng thái NotScanned trung thực.
- Video chạy trong dịch vụ nền của Web, sử dụng cùng SQL queue/lease, không phụ thuộc
  tài khoản GDLAWorker hay quyền đọc metadata IIS của tài khoản riêng.
- Build chuẩn bị FFmpeg/FFprobe cố định kèm giấy phép trong .runtime/tools/ffmpeg.
  Các tệp được ràng buộc vào manifest; hỏng/thiếu không âm thầm dùng bản khác.
  Bản legacy không có công cụ đóng gói vẫn giữ cơ chế tìm thư mục cũ/PATH.
- Hẹn giờ xuất bản dùng lại phiên bản đã duyệt, dấu kiểm tra nội dung, quyền hiện hành
  và nhật ký; xử lý nền và yêu cầu trang công khai dùng cùng bộ điều phối. Không công
  khai nháp mới hơn hoặc tệp chưa đạt điều kiện. Tác vụ bị từ chối trước đó không được
  tự phục hồi thành bài công khai.
- Quét tệp còn bắt buộc và gửi thư dùng lại các hàng đợi có lease. Scanner chưa sẵn
  sàng không tiêu hết lượt thử; SMTP Disabled không gửi thư. Trạng thái gửi thư không
  chắc chắn vẫn cần đối soát, không tự gửi lại.

## IIS nghỉ, tài nguyên và chuyển đổi

Khi IIS idle/recycle hoặc máy dừng, không có cam kết xử lý nền liên tục. Video đang
xử lý được trả lại hàng đợi khi dừng bình thường; nếu tiến trình bị ngắt đột ngột,
lease hết hạn rồi mới được thử tiếp. Số lần thử vẫn có giới hạn. Chỉ một tác vụ video
được claim tại một thời điểm qua khóa SQL; giới hạn thread, RAM, thời gian và dung
lượng FFmpeg giữ nguyên. Nhiều Web process khi recycle không được xuất bản trùng.

Lịch đến hạn được xử lý trước lần đọc trang công khai với ngân sách hữu hạn; hàng đợi
lớn có thể cần nhiều lượt đọc/vòng nền. Lịch hợp lệ đến hạn khi IIS nghỉ được kiểm tra
lại sau khi Web trở lại, không bị loại chỉ vì đã quá 15 phút. Không bảo đảm mốc 5 phút
khi toàn bộ IIS dừng; đây là giới hạn được chủ hệ thống chấp nhận trong yêu cầu mới.

Launcher/Worker CLI trong WebBundle mới tự báo tác vụ đã được Web tiếp nhận và kết
thúc, sau khi kiểm tra hash gói. Không cần chỉnh Action của task cũ hoặc tạo task mới.
Khóa/lease SQL giữ an toàn trong thời gian chồng lấn. **Một tiến trình Worker của bản
cũ đã chạy từ trước không tự biến thành code mới trong bộ nhớ**: nếu thực tế còn
chạy/khóa DLL thì phải dừng tiến trình đó trong khoảng bảo trì cập nhật. Không cấp
quyền cho Web tự tắt dịch vụ hoặc sửa Task Scheduler. GDLA đã báo Worker cũ chưa khởi
động được; cần phân biệt trường hợp này với một Worker cũ thực sự đang chạy.

Không yêu cầu quyền Windows mới. Pool dùng các quyền đọc ứng dụng/ghi kho hiện có.
Nếu chính sách máy chặn thực thi FFmpeg hoặc không cho đọc/ghi kho, code không được
giả thành công hoặc tự nâng quyền; trang quản trị phải báo lỗi truy cập cụ thể.

## Kiểm tra và rollback

Sau cập nhật: mở /, /tin-tuc, /admin/login, /health, thử upload PNG/tài liệu
CMS và một video hợp lệ; theo dõi xử lý ở /admin/videos. Truy cập
/.runtime/private/uat.json, /.runtime/manifest.json, /.runtime/tools/ffmpeg/ffmpeg.exe
phải bị từ chối và không trả byte tệp.

Nếu lỗi: dừng ứng dụng, trả nguyên web cũ vào vị trí ban đầu rồi khởi động lại.
Không rollback SQL hoặc kho dữ liệu. Cấu hình cũ có thể từ chối tệp no-AV mới;
tệp và lịch sử vẫn được giữ. Bản cũ phụ thuộc Worker riêng thì cơ chế nền của bản cũ
cũng chỉ hoạt động theo cấu hình vận hành đã có, không được hứa hoạt động như bản mới.

Build không kết nối SQL, chạy migration, sửa IIS, bật SMTP hoặc thay quyền Windows.
Kiểm thử cô lập không thay nghiệm thu IIS thật. [Nguồn và phạm vi bộ FFmpeg](video-tools-auto-discovery.md).
