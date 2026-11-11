# Phát hành chung upload ảnh, tài liệu và video — IIS UAT

PR #1 đã có trong main; PR #2 bổ sung video. Dùng SHA main đã được Developer xác nhận sau merge. Không migration: giữ schema 18, Runtime18, hai kết nối SQL, TLS, Application Role, tài khoản và 703 media. Không chạy lại các bộ E2E 48/60 đã PASS khi source không đổi. Nghiệm thu IIS vẫn cần thực hiện sau cập nhật.

## Cấu hình cần vận hành duyệt trước khi Publish

Dùng bản `.env` private hiện hành đã đối chiếu với cấu hình IIS đang chạy; chỉ bổ sung những giá trị được duyệt dưới đây. Không đưa file vào Git, không thay mật khẩu/secret/SQL hoặc SMTP. Giữ `DOTNET_ENVIRONMENT=Staging`, `ASPNETCORE_ENVIRONMENT=Staging`, `Deployment__Profile=UAT`; không đổi Production thành UAT để vượt guard.

| Khóa | Giá trị sau khi được vận hành duyệt |
| --- | --- |
| `Media__AllowManagedImageWithoutAv` | `true` cho JPG/PNG CMS |
| `Media__AllowManagedDocumentsWithoutAv` | `true` cho PDF/DOCX/XLSX CMS |
| `Video__Enabled` | `true` để mở chức năng video |
| `Video__AllowManagedVideoWithoutAv` | `true`, ngoại lệ video riêng |
| `Video__FfmpegPath`, `Video__FfprobePath` | Đường dẫn tuyệt đối đến hai binary thật trên máy Worker |
| `Video__FfmpegSha256`, `Video__FfprobeSha256` | SHA256 thực tế, 64 ký tự hex chữ thường của đúng hai binary |
| `Video__ProcessorApproved` | `true` sau khi duyệt bộ xử lý |
| `Video__LicenseReference` | Tham chiếu giấy phép/xác nhận sử dụng có thật, không điền mã giả |

FFmpeg cần bộ mã hóa `libx264`, AAC và JPEG. Không lấy đường dẫn/hash của máy Developer nếu binary đích khác. Giữ các giới hạn file/chunk/quota/thời gian/thread hiện có. Web và Worker phải nhìn thấy cùng binary và kho media/temp/keys; nếu Worker ở máy khác, phải chuẩn bị đường dẫn/quyền tương ứng trước, không tự đoán.

Đặt `Public__BaseUrl=https://gdla.gov.vn/`, `Deployment__ExpectedHost=gdla.gov.vn` trong đầu vào nếu đang để trống: Worker chạy ngoài IIS không có request/binding để tự suy ra host. Không đổi HTTPS binding hay TLS SQL. Không tự đổi provider scanner để né lỗi; ngoại lệ chỉ dành cho các upload CMS trên, phụ đề và upload công dân vẫn cần scanner theo chính sách cũ.

Ba cờ no-AV mặc định tắt. File vẫn `NotScanned`; video có kiểm định kỹ thuật `VideoTranscodedNoAv`, không phải Clean. **Đưa các giá trị đã duyệt vào `.env` trước Publish** để Web và cả năm chế độ Worker dùng chung `private/uat.json` được sinh và ràng buộc manifest. Không sửa JSON sau Publish. `ops/uat-runtime.ps1` xóa các override `Video__*`/`Media__*` trong môi trường tiến trình, nên không dùng cách đặt biến bên ngoài rồi gọi wrapper này để bật chính sách.

## Pull và Publish một lần

Chạy tại checkout source riêng, ngoài thư mục IIS đang phục vụ. Nếu có thay đổi source chưa commit thì dừng và giữ lại; không reset/clean/force.

```powershell
git status --short
git fetch origin
git switch main
git pull --ff-only origin main
git rev-parse HEAD
# Đối chiếu HEAD với SHA main bàn giao; nếu khác, dừng trước Publish.
powershell -NoProfile -File scripts/build-for-iis.ps1 -OutputName TTDL-IIS-UploadsVideo
```

Đặt `.env` private đã chuẩn bị ở gốc source trước lệnh Publish. Cần SDK theo `global.json`, Node/npm và các điều kiện build hiện có. Output: `.local/publish/TTDL-IIS-UploadsVideo`, gồm đầy đủ Web, Worker dùng cho 5 chế độ, tools, ops và manifest. Build không ghi SQL/khởi động Worker. Nếu output đã tồn tại, chọn tên mới, không dùng `-Replace` với bản đang chạy. Gói chứa secret trong private, chỉ chuyển bằng kênh riêng.

## Cập nhật cùng một cửa sổ bảo trì

1. Đối chiếu đầu vào private với bản đang chạy: SQL/TLS/role/secret, storage, IIS/appsettings môi trường và SMTP phải giữ nguyên; chỉ các cấu hình upload/video đã duyệt được thay đổi. Giữ đường dẫn `D:\WWW_GDLA\publish\TTDL-IIS`, IIS vẫn trỏ `web`. Nếu có tùy chỉnh môi trường không có trong gói mới, dừng đối chiếu trước khi thay file; không ghi đè mù quáng.
2. Dừng đúng pool `WWW_GDLA` và các dịch vụ Worker đang chạy. Sao lưu bộ `web`, `worker`, `tools`, `ops`, các manifest/root config và cấu hình private hiện hành vào thư mục private riêng ngoài webroot, ghi hash và danh sách dịch vụ để rollback. Đây là backup binary/config, không backup SQL và không copy lại 703 media.
3. Cập nhật **đồng bộ bộ ứng dụng đầy đủ** từ output mới vào đúng các thư mục ứng dụng; dùng cùng manifest và `private/uat.json` do Publish sinh từ đầu vào đã đối chiếu. Giữ nguyên thư mục dữ liệu private/media, temp, snapshots, keys và ACL hiện có. Không mirror/xóa toàn bộ package, không thay bằng DLL hotfix Runtime18, không chép các thư mục dữ liệu rỗng đè lên dữ liệu thật. Giữ cấu hình IIS site/pool/binding; mọi appsettings/web.config tùy chỉnh phải được bảo toàn trong gói đã kiểm tra trước khi cập nhật, không sửa tùy tiện file sau khi được hash.
4. Pool/Worker dùng tài khoản dịch vụ không thuộc Administrators/SYSTEM. Cần Read cấu hình private, Read/Execute binary FFmpeg/FFprobe, Read/Write/Modify kho media/temp/keys theo nhu cầu hiện hành. Không mở quyền rộng; không đổi SQL login/quyền. FFmpeg chạy với token Worker, không phải sandbox hệ điều hành.
5. Khởi động lại pool và đúng các Worker vốn đang bật. Với Video mới, cấu hình bộ quản lý dịch vụ hiện có chạy lệnh dưới đây bằng tài khoản Worker, đúng một instance. Không tự bật Mail hoặc scanner Worker khác.

Kiểm tra Video Worker trước khi chạy liên tục (trên IIS, sau cập nhật):

```powershell
powershell -NoProfile -File D:\WWW_GDLA\publish\TTDL-IIS\ops\uat-runtime.ps1 -PublishRoot D:\WWW_GDLA\publish\TTDL-IIS -Mode Video -Health
```

Khởi động Video Worker dưới trình quản lý dịch vụ hiện có:

```powershell
powershell -NoProfile -File D:\WWW_GDLA\publish\TTDL-IIS\ops\uat-runtime.ps1 -PublishRoot D:\WWW_GDLA\publish\TTDL-IIS -Mode Video
```

Wrapper truyền `--video` tới `worker/Worker.dll`, dùng cùng cấu hình portable; chạy foreground, không tự cài service. Không thêm `-Once` cho dịch vụ liên tục. Health cần processor Ready và `managedVideoWithoutAv=true` nếu scanner không khả dụng. Không biến scanner unavailable thành Clean. Worker Scheduler cũng phải được cập nhật và dùng chung chính sách để xuất bản hẹn giờ.

## Kiểm tra sau cập nhật và rollback

- Xác nhận `/`, `/tin-tuc`, `/admin/login`, `/health` trả 200, không 500/503.
- Bằng tài khoản được cấp quyền, kiểm tra JPG/PNG và PDF/DOCX/XLSX; tài liệu tải dạng attachment/nosniff. Thực hiện một Video hợp lệ: tạo nháp, upload, Worker Ready/poster, gửi duyệt, duyệt, xuất bản; kiểm tra player phát/seek (Range 206), poster, chi tiết và VIDEO cuối trang chủ. Chỉ dùng nội dung được phép công bố; không tự đưa fixture lên website thật.
- Nếu lỗi: dừng đúng pool/Worker, khôi phục **cả bộ binary + manifest + cấu hình** đã backup và giá trị cờ/service trước triển khai; khởi động lại đúng các tiến trình trước đó, kiểm tra lại bốn URL. Không rollback SQL, không xóa media mới. Tắt cờ no-AV khiến tệp NotScanned thuộc chính sách đó ngừng được phục vụ; không đổi chúng thành Clean.

Developer chỉ chuẩn bị source/hướng dẫn. Chưa xác minh cấu hình/FFmpeg/Worker hoặc phát video trên IIS thật; các bước này là điều kiện nghiệm thu tại máy đích.
