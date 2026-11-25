# PDF nội bộ: mục lục và hồ sơ riêng dùng cùng tài liệu

## Phạm vi sửa

Các bookmark PDF liên kết bằng `Next` là mục cùng cấp. Bộ kiểm định trước đây tăng độ sâu theo từng mục, nên một mục lục nhiều mục ngang có thể bị từ chối dù không lồng sâu. Duyệt cây mục lục bằng hàng đợi, giữ giới hạn độ sâu thực, tổng số đối tượng, hủy tác vụ, liên kết cha và tham chiếu vòng. Mọi dictionary vẫn đi qua kiểm tra hành động nguy hiểm, tệp nhúng và chữ ký hiện có.

Hai hồ sơ Excel có thể dùng cùng PDF nhưng vẫn phải có ContentId, metadata, revision và workflow riêng. Luồng dùng lại tài liệu chỉ áp dụng PDF nội bộ đã có kiểm định và audit, không dựa riêng vào việc biết SHA-256.

## Thao tác CMS cho công cụ nhập liệu

`POST /admin/documents/{destinationContentId}/reuse`, phiên CMS và antiforgery hợp lệ. Gửi `sourceContentId`, `assetId`, `sha256`, `contentVersion` (rowversion bản nháp dạng Base64) và `trustedSource` (xác nhận nguồn thực tế của quản trị viên).

Người thực hiện cần quyền quản lý thiết lập và media, quyền đọc hồ sơ nguồn và sửa hồ sơ đích. Chỉ nhận bản nháp/đã trả lại, đúng phiên bản, trong chính sách UAT nội bộ hiện hành. Kiểm tra audit gốc, trạng thái cách ly và byte/hash trước khi ghi audit cho phép dùng lại. Không upload hoặc nhân bản file; không đổi `NotScanned` thành `Clean`.

Sau khi nhận asset, công cụ vẫn lưu bản nháp qua `/admin/save` với đầy đủ metadata/attachment và rowversion hiện hành. Endpoint này không tự gắn, duyệt, công bố hoặc hẹn giờ. `/result`, danh sách tài liệu và liên kết tải riêng tư có ngữ cảnh ContentId đích để đối soát sau gián đoạn. Không được tự gắn asset khác hồ sơ nếu chưa có audit cho phép.

## Kiểm chứng và giới hạn

- Kiểm thử mục lục: nhiều mục ngang, lồng sâu, vòng, sai cha, hành động nguy hiểm ở cuối cây, tệp nhúng, URI, giới hạn tổng đối tượng và hủy tác vụ.
- Kiểm thử dùng lại: quyền, phạm vi hồ sơ, rowversion, hash, audit nguồn/đích, lặp thao tác, tải riêng tư và gắn vào bản nháp riêng. Fixture SQLite/service/controller; không phải kiểm thử HTTP/IIS thật.
- PDF có tham chiếu đồ họa không khai báo, thuộc tính bố cục chưa được hỗ trợ hoặc liên kết ngoài chính sách vẫn bị từ chối. Không chuyển đổi tài liệu, thay byte hoặc sửa chữ ký số.

## Cập nhật và quay lại

Gộp bản sửa trong một lần build bằng `scripts/build-for-iis.ps1`, triển khai nguyên `web` gồm `.runtime` theo quy trình hiện có. Không có migration hoặc cấu hình mới. Chưa triển khai IIS trong lượt sửa này.

Giữ bản Web trước cập nhật để rollback binary. Nếu đã tạo hồ sơ dùng tài liệu chung, bản code cũ không hỗ trợ chỉnh sửa các attachment khác ContentId; tạm dừng thao tác đó sau rollback. Không xóa audit, attachment hoặc dữ liệu để ép chạy phiên bản cũ. Việc triển khai không tự nhập, công bố hoặc đổi lịch hồ sơ.
