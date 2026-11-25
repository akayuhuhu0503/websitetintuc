# PDF nội bộ: mục lục và hồ sơ riêng dùng cùng tài liệu

## Phạm vi sửa

Các bookmark PDF liên kết bằng `Next` là mục cùng cấp. Bộ kiểm định trước đây tăng độ sâu theo từng mục, nên một mục lục nhiều mục ngang có thể bị từ chối dù không lồng sâu. Duyệt cây mục lục bằng hàng đợi, giữ giới hạn độ sâu thực, tổng số đối tượng, hủy tác vụ, liên kết cha và tham chiếu vòng. Mọi dictionary vẫn đi qua kiểm tra hành động nguy hiểm, tệp nhúng và chữ ký hiện có.

Cây đối tượng PDF cũng có liên kết ngược hợp lệ: chữ ký `/Data` về catalog, `StructElem /P` về cha và `/Pg` về trang. Duyệt rộng thay cho duyệt sâu để không tính các đường vòng này thành độ lồng thật. Không bỏ qua đối tượng; độ lồng trực tiếp, tổng số token và giới hạn của parser strict vẫn được giữ. Thuộc tính danh sách `/O /List /ListNumbering /Decimal` được nhận diện là dữ liệu bố cục; chỉ nhận các tên đánh số liệt kê cụ thể, không mở rộng tùy ý thuộc tính.

Một nhóm PDF có lệnh `gs`/`Do` gọi tài nguyên không khai báo nhưng vẫn có nội dung trang đọc được. Đây là lỗi dựng hình của nguồn, không phải bằng chứng malware. Riêng PDF nội bộ đã có quyền/xác nhận, adapter giữ trạng thái đồ họa hiện tại khi thiếu `gs` và bỏ lệnh vẽ không có đối tượng tương ứng. Toàn bộ đối tượng hiện hữu và phần còn lại của từng trang vẫn được parser xử lý. Trang không còn nội dung đọc được, tài nguyên **đã khai báo nhưng hỏng**, font thiếu hoặc lỗi parser khác vẫn bị chặn. Không sửa/ghi lại PDF, không khẳng định tài liệu hoàn toàn chuẩn hoặc chữ ký mật mã đã được xác thực.

Adapter sử dụng page factory mặc định của PdfPig 0.1.14 đã pin, thông qua constructor nội bộ vì phiên bản này chưa công khai factory đó. Nếu chữ ký constructor thay đổi thì dừng, không fallback sang chế độ bỏ qua lỗi. `UseLenientParsing=false`, `SkipMissingFonts=false`. Không thay parser của upload công khai hoặc PDF thông thường. Tài liệu trong ZIP vẫn theo kiểm định riêng hiện có.

Các cảnh báo `PDF_UNDEFINED_GRAPHICS_STATE:<count>` và `PDF_UNDEFINED_XOBJECT:<count>` được ghi vào audit `document.trusted.confirmed`, cùng SHA, actor, xác nhận nguồn và `NotScanned`; không ghi đường dẫn riêng tư/nội dung tài liệu. Không phát sinh cấu hình vận hành hoặc nâng quyền.

Đối chiếu implementation upstream đã pin: [ResourceStore](https://github.com/UglyToad/PdfPig/blob/v0.1.14/src/UglyToad.PdfPig/Content/ResourceStore.cs), [BaseStreamProcessor](https://github.com/UglyToad/PdfPig/blob/v0.1.14/src/UglyToad.PdfPig/Graphics/BaseStreamProcessor.cs), [PageFactory](https://github.com/UglyToad/PdfPig/blob/v0.1.14/src/UglyToad.PdfPig/Parser/PageFactory.cs).

Hai hồ sơ Excel có thể dùng cùng PDF nhưng vẫn phải có ContentId, metadata, revision và workflow riêng. Luồng dùng lại tài liệu chỉ áp dụng PDF nội bộ đã có kiểm định và audit, không dựa riêng vào việc biết SHA-256.

## Thao tác CMS cho công cụ nhập liệu

`POST /admin/documents/{destinationContentId}/reuse`, phiên CMS và antiforgery hợp lệ. Gửi `sourceContentId`, `assetId`, `sha256`, `contentVersion` (rowversion bản nháp dạng Base64) và `trustedSource` (xác nhận nguồn thực tế của quản trị viên).

Người thực hiện cần quyền quản lý thiết lập và media, quyền đọc hồ sơ nguồn và sửa hồ sơ đích. Chỉ nhận bản nháp/đã trả lại, đúng phiên bản, trong chính sách UAT nội bộ hiện hành. Kiểm tra audit gốc, trạng thái cách ly và byte/hash trước khi ghi audit cho phép dùng lại. Không upload hoặc nhân bản file; không đổi `NotScanned` thành `Clean`.

Sau khi nhận asset, công cụ vẫn lưu bản nháp qua `/admin/save` với đầy đủ metadata/attachment và rowversion hiện hành. Endpoint này không tự gắn, duyệt, công bố hoặc hẹn giờ. `/result`, danh sách tài liệu và liên kết tải riêng tư có ngữ cảnh ContentId đích để đối soát sau gián đoạn. Không được tự gắn asset khác hồ sơ nếu chưa có audit cho phép.

## Kiểm chứng và giới hạn

- Kiểm thử mục lục: nhiều mục ngang, lồng sâu, vòng, sai cha, hành động nguy hiểm ở cuối cây, tệp nhúng, URI, giới hạn tổng đối tượng và hủy tác vụ.
- Kiểm thử dùng lại: quyền, phạm vi hồ sơ, rowversion, hash, audit nguồn/đích, lặp thao tác, tải riêng tư và gắn vào bản nháp riêng. Fixture SQLite/service/controller; không phải kiểm thử HTTP/IIS thật.
- 18 kiểm tra tương thích PDF, 12 kiểm tra mục lục và 25 kiểm tra upload/dùng lại PDF có audit PASS. Các nhóm chạy tập trung; không full regression, không HTTP/IIS thật.
- Offline trên toàn bộ 137 PDF còn lại: 136 đủ điều kiện tiếp nhận, 1 bị chặn (liên kết URI tương đối tới tệp cục bộ). Nhóm 113 tệp từng bị chặn có 112 được giải quyết: 110 lỗi tham chiếu đồ họa (nhận có cảnh báo), 1 thuộc tính danh sách và 1 đường vòng qua cây chữ ký/cấu trúc. 12.273 trang của các tệp đạt đã được parser đọc; SHA trước/sau khớp tất cả 137 file. STT thiếu PDF không được tính là đã kiểm định. PDF/manifest/Excel/checkpoint riêng không được đưa lên GitHub.
- Thuộc tính bố cục chưa hỗ trợ, nội dung thực thi, tệp nhúng hoặc URI ngoài chính sách vẫn bị từ chối. Không chuyển đổi tài liệu, thay byte hoặc sửa chữ ký số.

## Cập nhật và quay lại

Gộp bản sửa trong một lần build bằng `scripts/build-for-iis.ps1`, triển khai nguyên `web` gồm `.runtime` theo quy trình hiện có. Không có migration hoặc cấu hình mới. Chưa triển khai IIS trong lượt sửa này.

Giữ bản Web trước cập nhật để rollback binary. Nếu đã tạo hồ sơ dùng tài liệu chung, bản code cũ không hỗ trợ chỉnh sửa các attachment khác ContentId; tạm dừng thao tác đó sau rollback. Không xóa audit, attachment hoặc dữ liệu để ép chạy phiên bản cũ. Việc triển khai không tự nhập, công bố hoặc đổi lịch hồ sơ.
