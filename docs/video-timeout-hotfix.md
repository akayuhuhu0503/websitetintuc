# Video hết thời gian xử lý — hotfix

## Nguyên nhân và giới hạn

Tải 100% chỉ xác nhận nhận đủ byte. `VideoProcessor.Run` dừng tiến trình khi hết hạn và trả `VIDEO_PROCESSOR_TIMEOUT`; thời hạn chuyển mã trước hotfix là 300 giây, 2 luồng. Chính sách video no-AV luôn yêu cầu chuyển mã H.264/AAC và tạo JPEG mới, kể cả MP4 đã nén bằng HandBrake. Không dùng nén trước làm bằng chứng an toàn.

Video 16 phút 23 giây dài 983 giây: hoàn thành chuyển mã trong 300 giây cần tốc độ khoảng 3,28 lần thời gian thực. Ngân sách 1.800 giây cho phép khoảng 0,55 lần thời gian thực, phù hợp hơn để thử trên CPU giới hạn 2 luồng. Đây là giới hạn, không bảo đảm hoàn tất trên mọi máy. Chưa đo trực tiếp hai tệp 838,7 MiB trên IIS.

Mã timeout cũ dùng chung cho lệnh native, nên chỉ từ mã lỗi không thể khẳng định đã dừng ở chuyển mã hay FFprobe/poster. Hotfix bổ sung `Operation`, `DeadlineSeconds`, `ProcessSeconds`, `Threads` trong audit xử lý để đối chiếu chính xác lần tiếp theo. Thời hạn kiểm tra công cụ 10 giây, FFprobe 30 giây và poster 60 giây không đổi. 1.800 giây chỉ áp dụng cho lệnh chuyển mã MP4; không phải tổng thời gian toàn hàng đợi.

## Publish và sử dụng

Sau khi PR được duyệt/merge, cập nhật theo quy trình hiện có:

```powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Deploy nguyên thư mục `web` gồm `.runtime` như thông thường. Không cần bước cấu hình, tài khoản Windows, Worker hay lệnh SQL bổ sung. Không sửa `.runtime/private/uat.json` hoặc manifest sau Publish.

- File `.env` tối giản hiện có không cần thêm khóa: mẫu đầu vào Publish tự điền `Video.ProcessSeconds=1800` **trước khi tạo hash/manifest**. Web và Worker đọc cùng cấu hình.
- Khóa tùy chọn `Video__ProcessSeconds` vẫn được tôn trọng nếu đã khai báo rõ; giá trị hợp lệ 10–1800. Giá trị ngoài khoảng bị chặn lúc Publish và runtime. Không sửa `.env` private trong hotfix này.
- `/admin/videos` → **Thông tin kỹ thuật dành cho quản trị hệ thống** hiển thị thời hạn thực tế. Cần hiển thị 1800 giây để bật lượt xử lý bổ sung. Nếu còn 300, kiểm tra đầu vào build hoặc việc cập nhật đủ gói, không chỉnh JSON.

## Xử lý hai phiên đã tải

1. Đăng nhập CMS bằng quản trị viên có quyền quản lý cài đặt, video, tệp và sửa bản nháp tương ứng.
2. Mở `/admin/videos`, tìm **Video hết thời gian sau 3 lượt**, đối chiếu liên kết bản nháp/mã tệp/dung lượng.
3. Bấm **Cho phép thêm 1 lượt xử lý (30 phút)** trên từng video cần sử dụng. Nếu hai phiên cùng tệp, hệ thống khử trùng theo MediaAsset và chỉ hiện một tác vụ; không cần xử lý bản sao thứ hai.
4. Chờ trạng thái Ready/Đã xử lý, xem trước rồi dùng quy trình gửi duyệt/xuất bản hiện có. Lượt retry không tự gắn lại tệp đã chủ động gỡ, không tự xuất bản.

Chỉ nhận `Failed + VIDEO_PROCESSOR_TIMEOUT + Attempts=3`, bản gốc còn đúng phiên tải, bản nháp còn sửa được, quyền người tải gốc còn hợp lệ. Phiên tải đã hoàn tất không cần tải lại dù đã qua hạn nhận chunk. Người quản trị có thể khôi phục tệp của cán bộ khác trong phạm vi quyền; không đổi chủ sở hữu.

Không đặt lại Attempts: giữ 3 khi chờ, tăng thành 4 khi worker nhận việc. CSRF, kiểm tra quyền hiện hành, khóa nội dung/rowversion và audit chống bấm trùng. Bản gốc được mở kiểm tra khả năng đọc/kích thước; worker kiểm tra lại SHA256, quota/đĩa trống, chính sách scan/no-AV, FFprobe, chuyển mã, poster và hash đầu ra như trước. Không gán Clean cho NotScanned.

Nếu lượt thứ tư thất bại, dừng; không có lượt thứ năm tự động hoặc nút cấp thêm. Không xử lý qua hotfix này các lỗi khác, tệp nhiễm, bản nháp đã đóng, quyền đã thu hồi hoặc bản gốc thiếu/sai. Giới hạn upload 1 GiB, CPU 2 luồng, tài nguyên native và quyền công bố không đổi.

Web đang nghỉ/dừng thì không xử lý nền. Dừng có trật tự hoàn trả lượt đang chạy và giữ quyền retry; dừng đột ngột có thể cần chờ hết lease (với cấu hình này tối đa khoảng 63 phút). Lượt thứ tư bị mất lease sẽ Failed, không được lặp vô hạn. Không cần tạo dịch vụ hoặc chỉnh IIS mới.

## Rollback

Giữ bản `web` trước cập nhật theo quy trình triển khai hiện có. Nếu cần rollback, thay nguyên bản WebBundle đó và khởi động lại ứng dụng. Không rollback SQL, không sửa Attempts hoặc audit. Mã cũ giới hạn `<3` sẽ không chạy tác vụ retry còn chờ ở Attempts=3. Khi quay lại hotfix có cấu hình 1800, tác vụ được phép còn Pending tiếp tục; không cấp thêm quyền hoặc thêm lượt đã tiêu thụ. Bản gốc/đường dẫn và video Ready giữ nguyên.

## Kiểm chứng trong Developer

- Release Web/Worker: PASS, 0 warning/error.
- 39/39 kiểm tra tập trung PASS trên SQL Server LocalDB riêng, schema18, HTTP Kestrel với MVC/CSRF thật, tài khoản tổng hợp và FFmpeg/FFprobe thật. Lượt cuối `video-timeout-c7fd82e44572`; instance đã dừng. Không gọi SQL/IIS vận hành; không cộng các lượt kiểm thử trước vào số này.
- Bao gồm deadline native thật, cùng workload hoàn tất với ngân sách1800, form quản trị, phân quyền/CSRF, hai phiên cùng hash, bấm đồng thời, expiry phiên đã Completed, audit một lần, no-AV tắt, tệp nhiễm, sai hash, bản nháp đóng, lượt thứ tư, recycle và hết lease, không phát sinh lượt thứ năm/công bố.
- Parser Publish offline: mặc định1800 từ `.env` mẫu, JSON/hash đồng nhất, giữ override300, từ chối1801. Không sửa cấu hình private thật hoặc chạy Publish vận hành.
- Không chạy lại full regression, upload E2E48/video E2E60 hoặc video dài trên IIS. Không khẳng định hai video thật đã được xử lý thành công; cần kiểm tra Ready và phát thử sau cập nhật.

Chạy lại đúng bộ kiểm thử delta khi cần, không dùng cấu hình SQL thật:

```powershell
powershell -NoProfile -File scripts/test-video-timeout.ps1 -DotNet <dotnet.exe> -ToolMetadata <test-video-tools.json>
```

`test-video-tools.json` chỉ chứa đường dẫn/hash FFmpeg/FFprobe đã cấp cho kiểm thử, không chứa credential. Runner tạo một instance LocalDB riêng, dừng instance khi kết thúc, lưu kết quả trong `.local/tests/video-timeout-*`.
