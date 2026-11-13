# Kiểm tra giao diện công khai cô lập

Host này render **Razor thật của Web**, CSS/JavaScript/font thật qua Kestrel trên loopback. Model trang chủ, cấu hình menu/banner và kết quả các component dữ liệu là fixture; không kết nối SQL, không đọc `.env`, không dùng tài khoản hoặc nội dung vận hành. Chỉ controller của fixture được map. Bộ đếm truy cập bị vô hiệu hóa bằng cache test và user-agent test; Data Protection dùng khóa trong bộ nhớ.

```powershell
dotnet build tests/LandPortal.PublicPresentationHost/LandPortal.PublicPresentationHost.csproj -c Release --artifacts-path .local/tests/public-design2/build
dotnet .local/tests/public-design2/build/bin/LandPortal.PublicPresentationHost/release/LandPortal.PublicPresentationHost.dll "$PWD" 5407 "$PWD\.local\tests\public-presentation\data"
```

Chạy browser từ một terminal khác, với Playwright đã có trong môi trường kiểm thử (không thêm vào ứng dụng):

```powershell
$env:CMS_PLAYWRIGHT_MODULE='C:\duong-dan-test\node_modules\playwright\index.mjs'
node tests/browser/public-reference-check.mjs http://127.0.0.1:5407
```

Các tham số fixture: `fallback=true`, `videos=0|1|3`, `banners=0|1|3`, `noImages=true`, `noNews=true`, `noCategories=true`, `hideFeatured=true`, `hideDocuments=true`, `hideNotices=true`, `hideVideo=true`, `sidebar=true`, `statistics=true`. Không dùng các tham số này trong ứng dụng thật. Có thể đặt các ảnh kiểm tra hợp lệ `photo-1.jpg`, `photo-2.jpg`… trong thư mục fixture riêng để đối chiếu thị giác; không commit ảnh.

Để kiểm tra HTML5/Range, tạo duy nhất `data/sample.mp4` bằng FFmpeg hợp lệ hiện có của môi trường test, ví dụ 1 giây test pattern H.264 320×180; không lấy video vận hành, không thêm binary FFmpeg vào Git. Nếu thiếu tệp, kiểm tra phát video phải FAIL, không giả PASS.

```powershell
New-Item -ItemType Directory -Force .local/tests/public-presentation/data | Out-Null
ffmpeg -hide_banner -loglevel error -f lavfi -i testsrc2=size=320x180:rate=24 -t 1 -c:v libx264 -pix_fmt yuv420p -movflags +faststart .local/tests/public-presentation/data/sample.mp4
```

Browser kiểm tra bố cục 1536/1366/768/390/320px, Configured/fallback, tin chính + bốn tin phụ + danh sách nổi bật; dịch vụ/tiện ích cạnh video; bốn khối thông tin đều nhau. Bao gồm banner/video 0/1/nhiều, trường hợp thiếu nội dung, tiêu đề dài, bài không ảnh, menu bàn phím, hai ô tìm kiếm, liên kết và phát MP4/Range fixture. Endpoint bài chi tiết/tìm kiếm của host là fixture để kiểm tra điều hướng; kết quả này không thay thế kiểm tra phân quyền, truy vấn SQL, FFmpeg/AV hoặc workflow xuất bản đã có. Không chạy SQL migration, CMS E2E hoặc full regression.

Kết quả lưu tại `.local/tests/public-design2/browser-result.json`. Ảnh render nội bộ nếu cần chỉ nằm trong `.local`; không đưa vào Git. Dừng đúng tiến trình host sau khi kiểm tra.
