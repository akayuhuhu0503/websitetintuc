# Video CMS không antivirus — bản sửa để review

Hướng dẫn phát hành chung PR #1 + #2: [managed-upload-video-release.md](managed-upload-video-release.md). Dùng quy trình `.env` → Publish trong tài liệu này; launcher `uat-runtime.ps1` xóa override môi trường bên ngoài nên các cờ phải có trong cấu hình private đã Publish.

Baseline: `429cb93bb2ea72cabfc86815cdea36aa4db62784`. Không migration; Runtime18, TLS, Application Role và 703 media nguồn giữ nguyên. Chưa triển khai IIS.

## Chính sách

- `Video__AllowManagedVideoWithoutAv=false` mặc định, độc lập hoàn toàn với hai cờ ảnh/tài liệu. Chỉ nhận giá trị `true` khi `Video__Enabled=true` và profile UAT/Staging qua validator hiện hành. Không áp dụng Production hoặc Development thông thường.
- Chỉ video tải mới qua CMS có quyền quản lý video/media được ghi `VideoNoAvPending`. Worker không tự chuyển video cũ sang chính sách này. Khi thiếu scanner, Worker chỉ chọn các job đã đăng ký chính sách mới, không tiêu thụ lượt thử của video cũ.
- Bản gốc giữ cách ly, không phục vụ trực tiếp. FFprobe kiểm tra container/stream/codec, tối đa 1 giờ, 4K/60fps và các giới hạn hiện hành. Worker luôn chuyển mã H.264/yuv420p/AAC và tạo JPEG mới trong nhánh này, kể cả nguồn đã tương thích trình duyệt. MP4/poster và bản gốc có byte/SHA được đối soát. Cache kết quả chuyển mã có phiên bản chính sách riêng, không tái dùng kết quả remux cũ.
- `Ready` nghĩa là xử lý kỹ thuật thành công, không phải quét sạch. Media giữ `NotScanned`, kiểm định `VideoTranscodedNoAv`; các trường bằng chứng scanner để null. Audit ghi chính sách, hash đầu vào/đầu ra, probe và thời gian xử lý. Phụ đề, tệp công dân, upload công khai và các luồng ảnh/tài liệu không đổi chính sách.
- Xuất bản vẫn cần revision được duyệt và quyền Publisher. Preview/public player kiểm tra quyền và môi trường thật; chỉ cấp MP4/poster đã kiểm định. Khi tắt cờ, video NotScanned không thể được xuất bản/phát hoặc chọn cho khối VIDEO; không sửa trạng thái SQL của chúng.
- FFmpeg không chạy qua shell, không yêu cầu nâng quyền và không nhận biến môi trường chứa credential SQL/runtime. Giữ giới hạn thread, timeout, 512 MiB working set, max_alloc, kích thước đầu ra, protocol file/pipe. Đây không phải sandbox chống mọi lỗi parser: vận hành phải dùng tài khoản Worker riêng không thuộc Administrators, chỉ có quyền đọc binary và đọc/ghi kho cần thiết. Không chạy Worker bằng sa hoặc tài khoản quản trị Windows.

## Kiểm thử cô lập

Kết quả cuối ngày 11/11/2026: **60/60 kiểm tra PASS** trong run `video-http-bd2625bd2433` (gồm setup/guard và các bước E2E), Release Web/Worker 0 lỗi/cảnh báo. SQL Server LocalDB 17.0.1000.7, compatibility 160, schema 18. MP4 được FFmpeg giải mã thực tế; HTTP Range 206 khớp byte. Host và instance đã dừng. Không chạy full regression.

Lần đầu dừng do harness gọi scanner factory với provider Disabled ngoài UAT; đã sửa harness dùng implementation UnavailableInquiryScanner giống nhánh UAT thiếu scanner. Run trung gian 55 PASS được giữ riêng, không cộng vào 60 kiểm tra cuối. Bản cuối bổ sung guard cấu hình UAT và giữ job cách ly cũ nguyên trạng khi thiếu scanner. Đây là kiểm chứng HTTP/SQL/native processor, chưa phải phát video bằng trình duyệt trên IIS thật.

`powershell -NoProfile -File tests/LandPortal.ManagedVideoHttpTests/run.ps1`

Dùng FFmpeg/FFprobe đã được ghi path/hash trong `.local/video-tools.json`, không tải hoặc đưa binary vào Git. Tạo instance SQL Server LocalDB riêng và database mới schema 18, bốn tài khoản giả, Kestrel HTTP thật, Identity/CAPTCHA/CSRF và runtime role. Test host inject chính sách video trong DI dưới guard LocalDB/loopback/kho test; không đổi profile vận hành. Scanner dùng implementation `UnavailableInquiryScanner`, luôn trả chưa sẵn sàng/ScanFailed, không có Clean giả. Một MP4 tổng hợp ngắn duy nhất; thêm byte giả/lỗi để kiểm tra từ chối.

Kiểm tra upload chunk, Worker chuyển mã/poster, workflow, player HTTP/Range, khối VIDEO, không đủ quyền, CSRF, tệp giả/lỗi, policy tắt và job cũ. Kết quả riêng từng run tại `.local/tests/video-http-*/result.json`; không cộng các lần thử. Dừng host/instance sở hữu sau test; giữ evidence riêng, không DROP SQL khác. Chưa phải nghiệm thu trên IIS thật.

## Triển khai một lần sau review/merge và phê duyệt vận hành

1. Pull đúng commit được bàn giao vào checkout source riêng. Giữ cấu hình SQL/TLS/role/private, không chạy migration. Chuẩn bị FFmpeg/FFprobe hợp lệ trên máy IIS: đường dẫn tuyệt đối, SHA thực tế, xác nhận sử dụng/giấy phép; không sao chép đường dẫn máy Developer sang IIS.
2. Sau phê duyệt, đặt `Video__Enabled=true` và `Video__AllowManagedVideoWithoutAv=true` trong đầu vào private được quản lý. Giữ các khóa `Video__FfmpegPath`, `Video__FfprobePath`, `Video__FfmpegSha256`, `Video__FfprobeSha256`, `Video__ProcessorApproved`, `Video__LicenseReference` đúng thực tế. Web, Worker video và Worker hẹn giờ xuất bản phải dùng cùng chính sách; không sửa JSON đã bị manifest ràng buộc hash bằng tay. Dùng đầu vào .env trước Publish để wrapper Worker nhận cùng cấu hình; không dựa vào override môi trường ngoài bị launcher xóa.
3. Build gói đầy đủ bằng `powershell -NoProfile -File scripts/build-for-iis.ps1 -OutputName TTDL-IIS-VideoFix`. Không build đè thư mục đang phục vụ. Sao lưu bộ binary/config/manifest hiện tại trước cửa sổ bảo trì; cập nhật Web và toàn bộ Worker tương thích, giữ private storage và 703 file. Không chỉ thay DLL lẻ.
4. Chạy Worker video ở chế độ `--video` bằng tài khoản dịch vụ tối thiểu, dùng chung media/temp/keys với Web. FFmpeg được chạy trên máy Worker. Không tự bật Mail hoặc các Worker khác đang tắt. Health report phải ghi scanner chưa sẵn sàng nhưng video no-AV đã bật; phụ đề vẫn cần scanner.
5. Tạo nội dung Video → tải MP4 → đợi Ready → gắn vào nháp → gửi duyệt → duyệt → xuất bản. Kiểm tra MP4 phát/seek được, poster tải đúng, bài chi tiết và VIDEO cuối trang. Không có video đủ điều kiện thì khối ẩn; một video hiển thị một thẻ lớn.
6. Nếu lỗi: dừng đúng pool/Worker, khôi phục bộ binary/config/manifest và giá trị cờ trước cập nhật; khởi động lại. Không rollback SQL hoặc xóa file mới. Tắt cờ làm video NotScanned ngừng phục vụ, không biến chúng thành Clean.

Không bật ngoại lệ trên IIS trong lượt phát triển này; không đưa video thử lên hệ thống thật.
