# QHKHSDD 2.4.2 — Xử lý PDF ngoại tuyến

**Cập nhật 18/11/2026:** theo yêu cầu nâng giới hạn riêng cho EditorialDocument, năm hồ sơ còn lại đã đạt kiểm định. Xem [kết quả 32/32 và giới hạn triển khai](planning-pdf-limits-20261118.md). Nội dung bên dưới giữ lại kết quả/giới hạn của ngày 17/11, không phải trạng thái mới nhất.

Kết quả ngày 17/11/2026: **18/23 hồ sơ bị chặn đã đạt kiểm định tệp CMS** sau chuẩn hóa. Cộng 9 hồ sơ đã hợp lệ, có **27/32 hồ sơ, 28 PDF** đạt kiểm định. STT 24 vốn có hai tài liệu riêng, mỗi tài liệu vẫn là một PDF. Chưa upload hoặc thay dữ liệu CMS.

## Phạm vi

Giữ nguyên ZIP/Excel/PDF nguồn và 32 ngày đăng đã xác nhận. Bản xử lý ở thư mục riêng, có SHA-256 nguồn và kết quả. Không chia PDF, sửa giới hạn CMS, SQL, IIS, `.env` hoặc trạng thái quét virus. Không tái xác định ngày từ tiêu đề/PDF.

Các bản xử lý và báo cáo riêng nằm tại `.local/handoff/GDLA-QHKHSDD-PDF-20261117/`, ngoài Git:

- `BAO-CAO-PDF.md`, `DOI-CHIEU-PDF.csv`: kết quả 32 hồ sơ và giới hạn.
- `MANIFEST-PDF.json`, `CHECKPOINT.json`: mapping/hashes và số lượng cuối.
- `ready/`: chỉ 28 PDF đạt kiểm định của 27 hồ sơ.
- `originals/`: 23 bản trích nguyên byte để đối soát.
- Kết quả CMS và chứng cứ so sánh từng trang được giữ riêng, không có ảnh chụp hoặc dữ liệu đăng nhập.

## Phân loại và kết quả

| Phân loại nguồn | STT |
|---|---|
| Chỉ quá dung lượng | 26 |
| Chỉ định dạng/cấu trúc | 2, 3, 4, 5, 6, 12, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24, 25, 30 |
| Cả dung lượng và định dạng/cấu trúc | 1, 28, 29, 32 |

Đã khắc phục: **2, 3, 4, 5, 6, 12, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24, 25, 30**.

| Còn chặn | Bản xử lý | Lý do |
|---|---:|---|
| 1 | 10,98 MiB / 52 trang | Quá 10 MiB |
| 26 | 95,80 MiB / 385 trang | Quá 10 MiB |
| 28 | 100,16 MiB / 714 trang | Quá 10 MiB |
| 29 | 60,52 MiB / 174 trang | Quá 10 MiB |
| 32 | 9,71 MiB / 1.663 trang | Quá 1.000 trang |

Đã thử dọn/nén cấu trúc, nén không mất dữ liệu ảnh, tìm ảnh trùng theo pixel và thử nén mẫu ở nguyên độ phân giải. Chưa có bản dưới 10 MiB đủ cơ sở bảo toàn chất lượng cho bốn tệp quá dung lượng. Không hạ độ phân giải mạnh hoặc bỏ trang để đạt giới hạn. STT 32 không thể đạt guard hiện tại khi giữ một PDF đủ 1.663 trang; cần bản nguồn phù hợp hoặc thay đổi giới hạn được yêu cầu và kiểm thử riêng.

## Công cụ và kiểm chứng

`prepare_planning_pdfs.py` chỉ tạo bản sao ngoại tuyến bằng PyMuPDF/Pillow đã có trên máy. Lượt này dùng PyMuPDF 1.24.10; phần xóa khóa PDF dùng API MuPDF nội bộ, nên cần kiểm chứng lại trước khi đổi phiên bản thư viện. Không đưa công cụ này vào runtime Web/IIS.

`PlanningPdfCheck` bổ sung lệnh:

```powershell
dotnet WeeklyScheduleImport.dll planning-pdf-check files.json new-result.json
```

`files.json` chứa `Stt`, `Path`, `ExpectedSha256`, `ExpectedPages`, `SourceEntry`. Lệnh kiểm tra hash, dung lượng, số trang, gọi đúng `OfficeDocumentPolicy`, `MediaService.ValidateContent` và `ManagedPdfPolicy` hiện có. Chạy các kiểm tra dung lượng và cấu trúc độc lập để không bỏ sót trường hợp có cả hai lỗi. Chỉ chẩn đoán, không khởi tạo kết nối SQL, upload, scan hoặc đổi guard.

Đã kiểm tra từng trang của 23 bản xử lý: số trang, hình học, chữ trích xuất, ảnh giải mã nguyên độ phân giải và hiển thị 96 dpi trong bộ nhớ. Các khác biệt làm tròn cho phép tối đa 2/255 mỗi kênh màu; không bỏ qua chênh lệch chữ hoặc ảnh. STT 23 được rút gọn cấu trúc font tương đương và gom mục lục, giữ thứ tự/tên/đích, rồi chạy lại kiểm định CMS thực: PASS.

Build công cụ Release PASS, không lỗi/cảnh báo. Không chạy lại các bộ kiểm thử nghiệp vụ cũ.

## Giới hạn cần giữ khi nhập sau này

- Tất cả bản xử lý vẫn `NotScanned`; PASS ngoại tuyến không phải antivirus Clean hay bằng chứng đã upload thật.
- STT 15, 17, 18, 19, 20, 23, 30 có đối tượng chữ ký trong nguồn. Bản chuẩn hóa là bản sao tĩnh, giữ phần hiển thị nhưng **không giữ chữ ký số mật mã nguyên bản**; chưa xác minh hiệu lực chữ ký nguồn. Giữ bản gốc làm đối chiếu, không gọi bản xử lý là bản ký số pháp lý.
- Một số tag trợ năng/form đã được loại khỏi bản sao tĩnh; chữ tìm kiếm và hình ảnh vẫn được kiểm tra. Nguồn STT 15/23 có tham chiếu ảnh thiếu sẵn: chỉ chứng minh giữ nội dung thực sự render được, không tái tạo ảnh nguồn không có.
- WEB IMPORT hiện vẫn ràng buộc ZIP gốc. Chưa tích hợp thay PDF theo manifest bản xử lý; không tráo ZIP hoặc sửa journal để vượt hash. Trước lần nhập được phép, cần nối mapping rõ ràng và dry-run theo manifest. Năm hồ sơ bị chặn không được nhập.
