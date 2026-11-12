# Bộ xử lý video trong gói Web IIS

Với `Video__Enabled=true`, lệnh `powershell -NoProfile -File scripts/build-for-iis.ps1` tự chuẩn bị FFmpeg/FFprobe và đưa vào `web/.runtime/tools/ffmpeg`. Người vận hành không cần cài công cụ, nhập đường dẫn/SHA256 hoặc sửa PATH trên IIS. Cấu hình bốn khóa đường dẫn/hash cũ vẫn được tôn trọng nếu đang có; không tự bỏ pin đã cấu hình.

## Nguồn công cụ và kiểm tra khi Build

`scripts/dependencies/ffmpeg-win64.json` ghim bản Gyan Essentials 9.0.2 Windows x64, URL phát hành cố định, dung lượng và SHA256 do nhà phát hành công bố. [FFmpeg chính thức](https://www.ffmpeg.org/download.html#build-windows) liên kết tới Gyan; [checksum của Gyan](https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip.sha256) trùng digest của tài sản phát hành GitHub. Đây là giá trị đã rà soát trong source, không lấy checksum mới từ mạng để tin ngay tại mỗi lần build.

Lần build đầu cần HTTPS tới GitHub; các lần sau dùng cache `.local/dependencies/ffmpeg`, luôn kiểm tra lại byte/hash. Build thất bại nếu cache sai, mạng không truy cập được khi chưa có cache, archive thiếu giấy phép hoặc đường dẫn không an toàn. Không tự tải tại runtime, không cài phần mềm lên Windows, không sửa ACL/PATH, không thực thi native tool khi Publish. Binary không được đưa vào Git.

Chỉ hai executable cùng bản nguyên gốc `LICENSE`, `README.txt` được giải nén, kèm `package-origin.json`. Manifest của toàn gói xác minh các file này. Ứng dụng ưu tiên công cụ đóng gói; gói bị thiếu/sai hash bị từ chối, không âm thầm chuyển sang binary khác. Gói cũ chưa có công cụ vẫn hỗ trợ vị trí `TTDL-IIS/tools/ffmpeg` và PATH tuyệt đối như trước.

## Phạm vi giấy phép

Gyan Essentials dùng GPLv3 và có libx264. Gói này phục vụ triển khai nội bộ TTDL, giữ nguyên giấy phép/thông tin nguồn của nhà phát hành. [GNU phân biệt việc sao chép trong cùng một tổ chức với việc phân phối ra ngoài](https://www.gnu.org/licenses/gpl-faq.html#InternalDistribution). Không đưa gói binary lên GitHub hoặc URL tải công khai. Nếu phát hành cho đơn vị khác, cần cung cấp nguồn tương ứng, thông tin build và giấy phép các thư viện theo nghĩa vụ phân phối thực tế; chỉ một đường dẫn nguồn FFmpeg không chứng minh đã đủ toàn bộ nguồn phụ thuộc. Đây không phải mã phê duyệt nội bộ hay cờ cấu hình mới.

## Kiểm tra trong ứng dụng

Ứng dụng kiểm tra `-version`, encoder `libx264`, `aac`, `mjpeg`, quyền thực thi thực tế dưới tài khoản Web và giới hạn tiến trình hiện có. SHA256 tự tính dùng kiểm tra toàn vẹn/audit, không thay cho nguồn phát hành. File handle Windows bảo vệ byte trong lúc thực thi. Runtime không nâng quyền và không tải phần mềm.

Video được xử lý ngay trong tiến trình Web bằng hàng đợi bền vững hiện có, không cần Task Scheduler hoặc tài khoản dịch vụ riêng. Khi IIS dừng/nghỉ, xử lý cũng dừng; hàng đợi tiếp tục sau khi ứng dụng được đánh thức. Không cam kết xử lý liên tục 24/7 khi Application Pool có thể nghỉ.

Các cờ no-AV vẫn mặc định tắt và chỉ hợp lệ trong Staging/UAT. Dữ liệu chưa quét vẫn là `NotScanned`; kiểm định định dạng, chuyển mã MP4/poster, quota, chunk, thời gian xử lý, SHA256, phân quyền và workflow xuất bản giữ nguyên. Không sửa SQL, migration hoặc media hiện có.

## Kiểm tra và rollback

Kiểm tra acquisition/cache/hash/ZIP/giấy phép: `powershell -NoProfile -File scripts/test-iis-video-tools.ps1`. Kiểm tra xử lý video và vòng đời Web dùng bộ kiểm thử tập trung tương ứng, không chạy lại toàn bộ E2E.

Khi cập nhật, thay nguyên `web` gồm `.runtime`, giữ kho dữ liệu ngoài ứng dụng. Rollback nguyên thư mục `web` cũ để khôi phục binary/config/manifest và bộ công cụ đồng bộ; không rollback SQL hoặc xóa media. Nếu còn tiến trình video riêng của bản cũ, không chạy song song hai cơ chế; bản Worker mới từ chối chế độ Video trên WebBundle để tránh xử lý trùng.
