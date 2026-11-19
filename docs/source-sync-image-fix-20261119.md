# Đồng bộ ảnh QLDD: JPEG bị nguồn khai báo là PNG

## Nguyên nhân và bằng chứng

Ngày 19/11/2026, HTTP client `SafeSourceClient` hiện hành trên Developer đọc robots.txt, đúng hai bài 3758/3767 và hai ảnh (5 request/lượt; một lượt trước sửa, một lượt sau sửa). Không cookie, Referer, proxy hoặc thay đổi User-Agent/TLS. DNS pinning và host allowlist hiện hành đều được áp dụng. Không quét danh sách bài khác.

| Bài | Tệp | HTTP / Content-Type nguồn | Byte thực tế | Kích thước | Kết quả giải mã |
|---|---|---|---:|---|---|
| 3758 | `…8e1f7709f7.jpg` | 200 / image/png | 148.102 | 1881 × 1059 | JPEG PASS |
| 3767 | `…e3e459c2a9.jpg` | 200 / image/png | 262.804 | 1506 × 819 | JPEG PASS |

Cả hai URL nằm trên `media-qldd.mae.gov.vn`, URL cuối không đổi. Parser lấy `src` duy nhất, không có `data-src`/`srcset`. Tên tiếng Việt/khoảng trắng ở bài 3758 được `Uri` encode đúng; không cần sửa parser. Cả hai có magic `FFD8FFE000104A4649460001` và cùng SHA trước/sau:

- 3758: `817cb245aba6d0c6fbb552dbd8250a8f7373ed8d8be16e0d0c7ec850b78351a9`
- 3767: `dd5bd15ec31e8731588a75a2a5ff1208caead0b488313304d62da6e10d1a10fb`

`SourceSyncRunner.DownloadAsync` cũ suy MIME `image/jpeg` từ đuôi `.jpg`, rồi từ chối `image/png` trước khi gọi bộ giải mã. CMS thật đang có đúng hai cảnh báo tương ứng, ở trạng thái Review. Kiểm tra trực tiếp trên Developer tái hiện được điều kiện từ chối; chưa chạy binary mới trên IIS.

## Thay đổi tối thiểu

- `SourceSyncResourcePolicy`: chỉ cho phép trường hợp đã quan sát: host cuối `media-qldd.mae.gov.vn`, tệp dự kiến JPEG nhưng HTTP khai báo PNG. Tệp vẫn phải qua **toàn bộ** `MediaService.ValidateContent` dưới MIME JPEG trước khi đưa vào checkpoint. HTML giả ảnh, PNG thực mang đuôi JPG, ảnh cắt dở và ảnh quá giới hạn vẫn bị từ chối. Không mở ngoại lệ cho các host hoặc cặp MIME khác.
- `SourceSyncRunner`: giữ byte/hash snapshot và MIME JPEG thực; phân biệt HTTP, Content-Type, giải mã/giới hạn ảnh, kiểm định tệp, ngân sách request. Hết ngân sách trong một lượt tải để lại cảnh báo bắt buộc, không chuyển Ready thiếu tài nguyên.
- `SafeSourceClient`: thêm mã lỗi an toàn cho URL/chuyển hướng, giới hạn byte, timeout. Không đổi allowlist, DNS pinning, chứng chỉ TLS, redirect budget, byte budget, proxy hoặc khoảng nghỉ giữa request.
- Không đổi `SourceParser`, `SourceSrcset`, `SourceSyncMedia`, cấu trúc SQL, chính sách antivirus hoặc quyền công bố.

## Kiểm chứng

- Release Web và Worker: PASS, 0 warning/error.
- 28 kiểm tra tệp/mạng: ảnh JPEG/PNG/WebP hợp lệ; MIME sai đã xác minh; HTML giả ảnh, ảnh hỏng, lệch định dạng thực, pixel limit; redirect/allowlist/private IP; request budget; mã lỗi không lộ exception riêng tư.
- 27 kiểm tra HTTP/SQL LocalDB cô lập, schema 18: phát lại đúng HTML/byte/MIME đã lấy từ hai bài thật. Review → retry qua CMS → Ready trên cùng checkpoint; SHA snapshot khớp; phát hiện lặp không tạo trùng; chuyển sang nháp gắn đúng ảnh trong nội dung và cover; `NotScanned`/`ImageDecodedNoAv`, không tự xuất bản. Instance thử đã dừng.
- Không chạy full regression, không ghi SQL vận hành, không sửa hàng chờ thật. **Số bài phục hồi trên IIS trong lượt này: 0**; trên Test: 2/2.

Lệnh kiểm tra tập trung từ gốc checkout (dùng SDK trong `global.json`):

```powershell
dotnet build tests/LandPortal.SourceSyncTests -c Release
dotnet tests/LandPortal.SourceSyncTests/bin/Release/net10.0/LandPortal.SourceSyncTests.dll resource-tests .local/source-resource-tests-new
# Tùy chọn lấy bằng chứng trực tiếp; thư mục output phải chưa tồn tại.
dotnet tests/LandPortal.SourceSyncTests/bin/Release/net10.0/LandPortal.SourceSyncTests.dll image-probe .local/source-image-probe-new
powershell -NoProfile -File scripts/test-source-sync.ps1 -ImagesOnly -SavedQlddSnapshot .local/source-image-probe-new
```

Không commit HTML/ảnh nguồn, dữ liệu thử hoặc cấu hình private.

## Sau khi PR được duyệt và triển khai

Chưa merge/deploy trong lượt sửa này. Không sửa JSON/manifest hoặc cấu hình IIS. Dùng quy trình hiện hành:

```powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Triển khai nguyên thư mục `web` gồm `.runtime`. Trong Đồng bộ QLDD, chọn **Thử lại** cho bài 3758 và 3767 khi hết thời gian chờ. Giữ nguyên ánh xạ, SourceId và checkpoint; không tạo lại hàng chờ, không bật crawl lịch sử. Chỉ xác nhận phục hồi thực tế khi hai bài đã chuyển sang Chờ biên tập và không còn cảnh báo tài nguyên. Chuyển sang nháp/duyệt/xuất bản vẫn là các thao tác riêng; tệp không được tự coi là Clean.
