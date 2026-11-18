# QHKHSDD 2.4.2 — Hoàn tất kiểm định tệp ngày 18/11/2026

**32/32 hồ sơ đạt kiểm định tệp với bản sửa hiện tại; chưa nhập CMS hoặc triển khai IIS.** Năm hồ sơ còn chặn đã được kiểm tra trực tiếp. Kết quả 27 hồ sơ trước được giữ lại theo đúng SHA, không chạy lại toàn bộ. Bộ nguồn có **33 PDF** vì STT 24 vốn có hai tài liệu riêng; không chia một tài liệu thành nhiều PDF, không bỏ phụ lục hoặc tự ghép chúng.

## Giới hạn có phạm vi

- Chỉ PDF của `EditorialDocument`: **104 MiB (109.051.904 byte), tối đa 2.000 trang**. Đây là mức trần có khoảng dự phòng nhỏ trên tệp lớn nhất 100,16 MiB. DOCX/XLSX vẫn 10 MiB; các luồng Media/public/inquiry và giới hạn PDF 1.000 trang mặc định không được nâng.
- POST `/admin/documents/{id}` có ngân sách multipart 105 MiB; bộ đệm 64 KiB rồi ghi xuống kho temp hiện có. `web.config` được sinh khi Publish chỉ nâng `requestFiltering` tại `admin/documents`; phần còn lại vẫn 20 MiB. Cấu hình, hidden segments và manifest được tạo nhất quán trước khi niêm phong. Không sửa JSON/manifest đã Publish.
- Chỉ một bộ phân tích PDF chạy đồng thời trong mỗi tiến trình; yêu cầu khác nhận 429 thay vì xếp hàng không giới hạn. Ngân sách kiểm định 60 giây với kiểm tra hủy giữa các token/trang. Đây là **hủy hợp tác**, không phải cơ chế cưỡng bức dừng một lệnh PdfPig đang chạy hoặc trần RAM tuyệt đối.
- PdfPig đọc từ stream, không nạp nguyên tệp qua overload đường dẫn. Giữ `%PDF`, EOF, parser strict, giới hạn kích thước trang, 100.000 token/độ sâu 64 và chặn nội dung chủ động của `ManagedPdfPolicy`.
- Giữ quyền/CSRF, hash, cách ly, quy trình công bố và chính sách no-AV hiện hành. Kiểm định định dạng không phải antivirus; tệp vẫn **NotScanned**.

## Kết quả trực tiếp năm hồ sơ

| STT | Byte thực tế | MiB | Trang | Kiểm định | Bộ nhớ đỉnh tiến trình |
|---|---:|---:|---:|---:|---:|
| 1 | 11.516.204 | 10,98 | 52 | 0,443 giây | 67,58 MiB |
| 26 | 100.455.358 | 95,80 | 385 | 0,424 giây | 65,86 MiB |
| 28 | 105.023.366 | 100,16 | 714 | 0,537 giây | 67,70 MiB |
| 29 | 63.458.320 | 60,52 | 174 | 1,161 giây | 75,65 MiB |
| 32 | 10.184.761 | 9,71 | 1.663 | 4,112 giây | 108,41 MiB |

Đo bằng chính bộ kiểm định mới, mỗi tệp trong một tiến trình riêng trên Windows Developer. Số đo không bao gồm mạng/scanner, không phải đo tải đồng thời hoặc cam kết hiệu năng IIS. Ngoài đo riêng, cả năm tệp đã upload/download qua HTTP Kestrel thực, controller/service thật, SQLite bộ nhớ với tài khoản giả; SHA tải lại khớp, chưa công khai. Không kết nối SQL Server trong lượt kiểm thử này.

Byte của năm PDF không đổi so với bản chuẩn hóa đã kiểm tra từng trang ngày 17/11: số trang, hình học, chữ, pixel ảnh gốc và render 96 dpi trong bộ nhớ. Lượt này không nén thêm, giảm độ phân giải hoặc tái raster hóa. Chi tiết giới hạn của chứng cứ hiển thị nguồn trong [báo cáo chuẩn hóa](planning-pdf-remediation.md) vẫn được giữ.

## Chữ ký và nguồn

STT **15, 17, 18, 19, 20, 23, 30** có đối tượng chữ ký trong PDF nguồn. Các PDF chuẩn hóa là bản sao tĩnh, **không còn chữ ký số mật mã nguyên bản**; chưa xác minh hiệu lực chữ ký nguồn. Không trình bày bản chuẩn hóa như bản gốc có chữ ký số hợp lệ. Cảnh báo từng tệp nằm trong manifest và báo cáo importer, đồng thời giữ nguyên PDF/ZIP gốc để đối chiếu.

Tất cả 32 ngày đã xác nhận từ cột B giữ nguyên. Không chuyển đổi lại ngày, không lấy ngày từ tiêu đề/PDF. Tệp xác nhận ngày, ZIP và workbook được ràng buộc SHA; chỉ sao chép nguyên byte vào gói CLI mới. ZIP phụ lục của STT 21 tiếp tục bị loại đúng phạm vi đã xác nhận.

## WEB IMPORT và bàn giao

Thư mục riêng ngoài Git: `.local/handoff/GDLA-QHKHSDD-PDF-20261118/`.

- `ready/`: 33 PDF của 32 hồ sơ, SHA trùng các bản đã kiểm tra.
- `MANIFEST-PDF-32.json`: toàn bộ mapping nguồn/kết quả, giới hạn và cảnh báo.
- `pdf-overrides.json`: chọn tường minh 23 PDF chuẩn hóa, gắn STT, entry nguồn, SHA nguồn/kết quả, byte và số trang. SHA manifest: `ecabb90f27b15438be956f6fcd8534c92ce7f9b6d8abb610c6833b0f94cf8d6c`.
- `cli/`: công cụ độc lập; `date-decisions.json` và `source-scope.json` giữ nguyên byte đã xác nhận. Không có credential/runtime config mới.

`-PdfOverrides` phải dùng nhất quán trong Inspect/Preflight/DryRun/Import/Reconcile/SyncLinks. Hash manifest đi vào scope của plan; không tráo tệp/nguồn hoặc dùng lại plan khác byte. Không tạo PDF giả, không thay ZIP gốc. Ví dụ **chỉ kiểm kê ngoại tuyến** sau khi nhận công cụ:

```powershell
# Tại thư mục GDLA-QHKHSDD-PDF-20261118\cli; Output phải là thư mục mới.
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action Inspect -Zip C:\website_upload\website_upload.zip -Output ..\qhkhsdd32-run -PdfOverrides ..\pdf-overrides.json
```

Khi có quyền thực thi nhập và IIS đã được cập nhật bản giới hạn mới: dùng cùng tham số ZIP/Output/PdfOverrides với `Preflight`, `DryRun`, `Import -Stt 1`, `Reconcile`, rồi `Import -All`. Công cụ chỉ tạo nháp; cán bộ duyệt/công bố qua CMS. `SyncLinks` chỉ ghi URL đã đối soát ngày, revision và toàn bộ PDF công khai. Nếu gián đoạn giữ nguyên nhật ký, đối soát trước khi tiếp tục.

## Kiểm tra và giới hạn triển khai

- Release Web/Worker/CLI: PASS, không lỗi/cảnh báo.
- HTTP giới hạn và năm tệp: **37/37 PASS**; gồm 2.000 trang được nhận, 2.001 trang bị chặn, Office/luồng khác giữ giới hạn, fake PDF/JavaScript/CSRF/quyền bị từ chối, byte/hash/NotScanned giữ đúng.
- Manifest/ghép PDF: **20/20 PASS**, fixture nhỏ tách biệt; kiểm tra nguồn/entry/STT/traversal/hash/số trang/notice và mutation, giữ workbook/ngày, không bỏ tài liệu thứ hai STT24.
- Publish/WebBundle: **7/7 PASS**, chạy converter thật trên gói cấu hình giả cô lập; giữ hash/private/hidden segments và phạm vi giới hạn IIS.
- Đọc lại tệp lớn của importer: **6/6 PASS**, HTTP loopback tải đúng tệp 100,16 MiB/hash; chế độ 2.4.1 vẫn chặn trên 10 MiB, sai tên/chính sách bị từ chối. Không chạy các bộ E2E cũ.

**IIS chưa được cập nhật hoặc kiểm thử.** Máy IIS đang chạy bản giới hạn cũ vẫn có thể từ chối tệp lớn. Cần đưa bản sửa qua quy trình duyệt/build/deploy nguyên `web` gồm `.runtime` sau này; không chỉnh private JSON hoặc manifest thủ công. Kiểm tra XML/publish không thay thế kiểm chứng hiệu lực `requestFiltering` trên IIS thật (ví dụ khi section bị khóa ở cấp máy). Chưa upload hồ sơ thật, chưa sửa SQL hoặc dữ liệu công khai.
