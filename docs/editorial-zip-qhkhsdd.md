# ZIP tài liệu quản trị và QHKHSDD STT 21

## Phạm vi và trạng thái

Bổ sung ZIP cho luồng tài liệu CMS đã xác thực, không mở upload công khai và không dùng ngoại lệ không antivirus của PDF/Office. Không đổi SQL/schema, dữ liệu hoặc cấu hình private. Không tự duyệt/xuất bản.

ZIP được lưu nguyên byte, hash và tên; không giải nén thành cây tệp trên website. Chỉ tiếp nhận ZIP tối đa 128 MiB, ZIP32 một phần, tối đa 256 mục, tổng bung nén tối đa 512 MiB, tỷ lệ tối đa 100:1 (bỏ qua chênh lệch dưới 1 KiB). Chỉ cho phép PDF/DOCX/XLSX; bản này **không hỗ trợ ZIP lồng, DOC/XLS cũ, macro hoặc tệp thực thi**. Đây là giới hạn tiếp nhận, không phải kết luận các tệp bị chặn có virus.

Kiểm tra local/central headers, phạm vi byte liên tục không chồng lấn/nội dung ẩn, mã hóa, symlink, tên trùng/đường dẫn nguy hiểm, CRC, kích thước thực và SHA từng tệp. Nội dung từng PDF chạy parser thật; Office chạy bộ kiểm định gói/XML hiện hành. Một bộ kiểm định ZIP cùng lúc, ngân sách 60 giây. PDF vẫn tối đa 104 MiB/2.000 trang; Office vẫn 10 MiB. Không giảm kiểm định PDF đang hoạt động.

Scanner thật phải sẵn sàng để upload ZIP. Tệp mới vẫn `NotScanned`, cách ly, `PendingScan`. Tác vụ nền hiện có trong Web quét container **và từng tài liệu**; chỉ ghi Clean khi tất cả kết quả thật Clean, cùng engine/provider/definitions và hash nguyên tệp không đổi. Tổng ngân sách ZIP 180 giây, lease riêng 4 phút; timeout vẫn chặn, không tạo kết luận Clean. Không cần Worker Windows mới. Upload, đọc tệp, gắn nháp và công bố tiếp tục theo quyền/workflow hiện hành.

Tệp có proof Clean khớp byte và quyền công khai mới được tải qua `/media/{id}`. ZIP luôn `Content-Disposition: attachment`, `nosniff`, CSP sandbox; nút **Tải tệp ZIP về**, không mở trình PDF. Thu hồi bài/cách ly/hash sai sẽ chặn lại ngay.

## Tệp STT 21 đã đối soát

- ContentId: `fbb093da-f241-4783-a0b2-e3521a01c5c8`.
- Chuyên mục 77; ngày 09/07/2026; PDF đang có được giữ nguyên.
- Tệp: `21_2025_NQ_H_ND.zip`, **124.514.760 byte** (118,75 MiB).
- SHA256: `8df1b096400344996af1c84841e1c56d5638b1573967436e90eea512151f5eaf`.
- Hash bản trên đĩa và entry trong `website_upload.zip` giống nhau. Đọc toàn bộ CRC thành công.
- Tầng ngoài: 116 PDF, 1 DOCX, 1 ZIP lồng. ZIP lồng có 36 PDF, 5 DOCX, 6 XLSX và 1 XLS cũ.
- **BLOCKED**: ZIP lồng/XLS không thuộc chính sách tiếp nhận hiện tại; chưa có quét antivirus thật. Không tự bỏ/chuyển đổi tài liệu bên trong, không tạo ZIP thay thế và không upload CMS.

Không thể gắn/công khai nguyên ZIP này chỉ bằng bật cờ PDF/Office no-AV. Cần xử lý phạm vi ZIP lồng/XLS bằng chính sách kiểm định được xem xét riêng và có scanner thật. Bản sửa này chưa giải quyết hai điều kiện đó; không hứa cập nhật IIS là đủ để công khai ZIP gốc.

## Công cụ bổ sung, giữ nhật ký cũ

CLI mới có lệnh chỉ đọc, không đăng nhập CMS hoặc đọc cấu hình SQL:

```powershell
dotnet .\WeeklyScheduleImport.dll planning-zip-check --file "C:\website_upload\2.4_Lich tuan\QH, KH SDĐ\21_2025_NQ_H_ND.zip" --journal "C:\Users\ADMIN\LandPortal-Private\qhkhsdd32-web-20261118\planning-web-journal.json" --out "C:\website_upload\zip-review-new"
```

`--out` phải là thư mục mới. Lệnh kiểm tra đúng hash nguồn, binding plan/journal, STT/ContentId và xuất `zip-supplement-review.json`. Báo cáo ghi yêu cầu bổ sung của chủ dự án, **không phải receipt antivirus hoặc phê duyệt công bố**. Không sửa/xóa exclusion cũ, PDF overrides, manifest, journal hoặc Excel. Mục tiêu vẫn **32 hồ sơ / 33 PDF + 1 ZIP bổ sung**.

Reconcile nhận diện riêng đúng ZIP đã chấp thuận cho STT 21 nếu sau này nó được gắn qua CMS hợp lệ: bắt buộc Clean, tải riêng tư qua endpoint kiểm định và hash đúng; không thêm ZIP vào mapping/count PDF. Các tệp dư khác vẫn chặn. SyncLinks tiếp tục trả URL bài viết cho cột D; không yêu cầu ZIP phải có để xuất link PDF hiện có. Chưa bổ sung lệnh tự upload ZIP nguồn bị chặn hoặc tự mở phiên chỉnh sửa bài Published.

Sau khi đủ chính sách và scanner: đối soát bài STT 21 → mở bản chỉnh sửa hợp lệ → tải ZIP qua CMS → chờ quét/kiểm định → gắn cạnh PDF → lưu nháp → duyệt/công bố theo quyền. Không dùng Import PDF để nhập lại hồ sơ; không thay ngày/tiêu đề/URL.

## Kiểm tra và cập nhật

- Release Web/Worker và CLI PASS, 0 lỗi/cảnh báo; TypeScript PASS.
- `dotnet run --project tests/LandPortal.ManagedUploadTests -c Release -- --editorial-zip`: kiểm định ZIP, từ chối nguy cơ, giả lập kết quả scanner để kiểm tra logic, SQLite riêng + service/controller/validator gắn nháp, giữ PDF/ngày, tải attachment, thu hồi và hash sai. **Không phải quét antivirus thật hay HTTP/SQL Server E2E.**
- `powershell -NoProfile -File scripts/test-editorial-pdf-publish.ps1`: kiểm tra template WebBundle, request limit 129 MiB riêng `/admin/documents`, nguyên giới hạn chung 20 MiB, hash/hidden `.runtime` và cấu hình private fixture.
- Kết quả tập trung: 43/43 kiểm tra ZIP; 9/9 kiểm tra đối soát bổ sung qua HTTP localhost giả lập CMS; 7/7 kiểm tra đóng gói. Các kiểm tra HTTP không ghi SQL và không phải upload thật trên IIS.
- Không chạy lại full regression, không ghi CMS/SQL vận hành, không sửa Excel/nguồn ZIP.

Chỉ sau khi PR được duyệt/merge, dùng quy trình hiện có:

```powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Triển khai nguyên `web` gồm `.runtime`; không sửa JSON/manifest sau Publish. Không cần migration. Chưa triển khai từ máy Developer.
