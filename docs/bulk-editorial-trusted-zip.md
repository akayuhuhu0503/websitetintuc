# Xuất bản hàng loạt và ZIP nội bộ QHKHSDD STT 21

Baseline: `f7f4ce9cbe05eacf083e65939c56e90fbbc9917b`. Không thay đổi SourceSync, schema, cấu hình private hay dữ liệu vận hành.

## Thao tác nhiều bài tại /admin

- Chọn từng bài hoặc các bài chưa công khai/hẹn giờ/trong thùng rác trên trang hiện tại; tối đa 100 bài mỗi nhóm. Giữ lựa chọn qua phân trang trong cùng tài khoản/tab/bộ lọc. Đổi bộ lọc sẽ bỏ lựa chọn và thông báo rõ.
- Gửi duyệt, Duyệt, Xuất bản bài đã duyệt, Duyệt và xuất bản, Đăng trực tiếp xuất hiện theo quyền. Đăng trực tiếp là lựa chọn riêng, không suy ra từ quyền duyệt.
- Hộp xác nhận kiểm tra từng bài và hiển thị lý do bỏ qua. Mỗi bài gửi một request riêng, tuần tự, có CSRF và command ID; tái sử dụng controller/workflow hiện có, không cập nhật trạng thái bằng SQL riêng.
- Quyền/chuyên mục/revision/RowVersion/tệp đính kèm được kiểm tra lại khi thực hiện. Guard nằm trong khóa giao dịch nội dung hiện có, bảo vệ cả lịch hẹn vừa được tạo sau lúc mở hộp thoại và phiên bản công khai.
- Một lỗi không chặn bài sau. Kết quả giữ tiêu đề, link biên tập, số thành công/bỏ qua/lỗi; chỉ làm mới vùng danh sách và số trạng thái.
- Nếu duyệt thành công nhưng xuất bản lỗi, giữ kết quả duyệt thật và báo lỗi theo workflow hiện có. Không rollback giả hoặc tự thay ngày hồ sơ.
- Khi đóng tab hoặc mất kết nối, việc chưa gửi không chạy nền. Lựa chọn và command ID được giữ trong sessionStorage của tab; phải đối soát trạng thái hiện tại nếu phản hồi không về. Preview sẽ từ chối phiên bản cũ/công khai, không tự chọn lại phiên bản mới để đăng.

## ZIP nội bộ: giới hạn và kiểm định

Chỉ nhánh **Tài liệu nội bộ đã kiểm soát nguồn**, với quyền/xác nhận/audit hiện có, được dùng một tầng ZIP lồng và XLS BIFF8. Tệp giữ `NotScanned`; đây không phải kết quả antivirus Clean. Luồng ZIP thông thường và upload công khai không được mở rộng.

- ZIP tối đa 128 MiB, request 129 MiB; tổng 256 mục (kể cả thư mục), 512 MiB bung nén tính gộp các tầng, tỷ lệ 100:1, thời hạn kiểm định 60 giây và một slot kiểm định tại một thời điểm.
- Kiểm tra header/directory, đường dẫn, symlink, mã hóa, ZIP64, trùng mục, CRC và SHA-256 từng tệp. Không giải nén ra website; ZIP luôn được tải attachment theo chính sách hiện có.
- PDF tiếp tục dùng chính bộ kiểm định nội bộ/đọc PDF hiện có, không sửa bộ kiểm định PDF hoặc chuyển đổi byte.
- XLS tối đa 10 MiB: dùng OpenMcdf 3.4.0 đọc OLE strict, chỉ cho Workbook BIFF8 và metadata; kiểm tra record, sheet, công thức ô nội bộ, vùng in/lọc và đối tượng autofilter không gắn mã. Từ chối VBA/XLM/dialog/nhúng/DDE/liên kết ngoài/mã hóa và cấu trúc chưa thuộc tập hỗ trợ. Không khởi chạy Excel.
- Một số Office trong ZIP thật có trường số trang và công thức. Chỉ trong ZIP nội bộ, cho PAGE/NUMPAGES/SECTIONPAGES, phép tính/hàm thuần theo danh sách và vùng in/lọc nội bộ. Macro/ActiveX/nhúng/kết nối ngoài/hàm chủ động vẫn bị từ chối. Không thay chính sách PDF/Office upload độc lập.
- Các biểu thức tên chưa định nghĩa trong hai XLSX Phú Lâm được giữ nguyên; có thể cho lỗi `#NAME?` khi mở Excel, nhưng không chứa định nghĩa tên tự chạy. Không tự sửa công thức hoặc đánh giá kết quả tính toán.
- OpenMcdf được dùng nguyên bản, MPL-2.0; license và đường dẫn mã nguồn đi cùng Web/Worker trong `licenses/media`. Lockfile các project phụ thuộc chỉ bổ sung đúng thư viện này để publish locked restore không lỗi.

## ZIP STT 21 thực tế

- Tên: `21_2025_NQ_H_ND.zip`; 124.514.760 byte.
- SHA-256 trước/sau: `8df1b096400344996af1c84841e1c56d5638b1573967436e90eea512151f5eaf`.
- Ngoài: 116 PDF + 1 DOCX + 1 ZIP. Trong: 36 PDF + 5 DOCX + 6 XLSX + 1 XLS.
- 166 tệp/container, 10 thư mục, tổng 176 mục; tổng bung nén tính cả container lồng: 152.677.936 byte.
- PASS bộ kiểm định thật trong 9,5 giây trên máy Developer. Đối soát độc lập đủ tên/kích thước/hash từng mục ở cả hai tầng, CRC hợp lệ; byte gốc không đổi. Không đánh giá chữ ký số là hợp lệ chỉ từ việc giữ hash.
- ContentId đích: `fbb093da-f241-4783-a0b2-e3521a01c5c8`; chuyên mục 77; ngày hồ sơ 09/07/2026. Chưa upload/gắn ZIP hoặc sửa bài trên CMS trong lượt này.

## Kiểm thử tập trung

- Release Web và Worker, locked restore: PASS, 0 cảnh báo/0 lỗi.
- HTTP thật + SQL Server LocalDB riêng/schema 18: 42/42 kiểm tra (18 thiết lập/xác thực + 24 kiểm tra hàng loạt). Bao gồm thay quyền chuyên mục sau preview, sai RowVersion, CSRF, command replay, tệp lỗi, xử lý bài sau và giữ nguyên lịch hẹn. Host/LocalDB test đã dừng.
- Trình duyệt: 12/12, chọn qua trang, xác nhận, lỗi độc lập, cập nhật danh sách, thay bộ lọc, desktop 1366/mobile 390 và không lỗi JavaScript. Dùng HTML Razor thật từ host test; phản hồi batch được mô phỏng cho UI, còn workflow được kiểm chứng bằng HTTP/SQL ở trên.
- ZIP lồng/XLS/công thức: 27/27 kiểm tra tập trung.
- ZIP cũ/PDF/attachment/quyền/hash: 43/43 kiểm tra SQLite/service/controller; không giả scanner fixture là quét antivirus thật.
- ZIP STT 21 thật: PASS riêng, không cộng vào tổng fixture.
- Không chạy full regression; chưa kiểm chứng trên IIS thật.

## Sau khi PR được duyệt và IIS được cập nhật

Giữ quy trình `git pull --ff-only` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → thay nguyên `web` gồm `.runtime`. Không sửa JSON hoặc manifest sau build; không cần migration/cấu hình mới.

Sau khi xác minh phiên bản IIS mới, mở đúng bản biên tập STT 21, dùng chính ZIP gốc với lựa chọn tài liệu nội bộ, gắn thêm bên cạnh PDF rồi duyệt/xuất bản bản sửa theo quyền được cấp. Giữ tiêu đề, ngày, chuyên mục và URL. Kiểm tra tải ZIP là attachment sau khi công bố. Mục tiêu dữ liệu vẫn 32 hồ sơ, 33 PDF, thêm 1 ZIP.
