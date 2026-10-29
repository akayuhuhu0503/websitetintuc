# Biên tập V05-Finish

Tài liệu cho bản chạy local/Test; chưa là hướng dẫn vận hành Production. Không nhập dữ liệu người dân vào scanner Defender Test. Giữ tra cứu hồ sơ riêng tư khóa; CAPTCHA không xác minh chủ hồ sơ.

## Đăng nhập

Mở `/admin/login`, nhập tài khoản được cấp, mật khẩu và đáp án câu hỏi. Câu hỏi gắn với trình duyệt, địa chỉ mạng và mục đích đăng nhập; dùng một lần, tối đa5 phút. Sai/hết hạn/dùng lại cần trả lời câu mới. Mật khẩu không được điền lại khi báo lỗi. Hộp xác thực lại phiên editor có câu hỏi mục đích riêng, giữ nội dung chưa lưu trong tab; endpoint reauthenticate không còn là đường đăng nhập bỏ qua CAPTCHA. Giới hạn SQL CAPTCHA và rate limit HTTP cùng hoạt động; khóa tài khoản5 lần sai/15 phút và cơ chế phiên Identity giữ nguyên. CAPTCHA chưa thay MFA; MFA Production còn gate riêng. Không có mật khẩu mặc định hoặc tài khoản công khai.

## Menu Công khai thông tin

Người có `site.manage` mở `/admin/site?section=menus`, bấm **Thêm nhóm Công khai thông tin vào nháp**. Thao tác thêm nhóm, trang tổng quan và3 đích FR-17, chống thêm trùng; giữ toàn bộ chuyên mục/menu nguồn. Xem trước, đối chiếu rồi người có quyền áp dụng cấu hình xuất bản đúng revision.

**Cập nhật SRS1.0/Phase3D ngày19/10/2026:** tên “Người nước ngoài định cư ở Việt Nam” đã chốt, quản trị không còn ghi chú chờ duyệt tên viết cứng. Quyết định trạng thái có audit được áp dụng Test (`NameApproved`), Dev vẫn `NeedsNameApproval` chờ thay đổi dữ liệu có thẩm quyền; không auto-publish menu hoặc bài. Bộ dựng public vẫn lọc theo trạng thái database. Không tự cấp quyền hoặc bỏ gate để mở Dev. Xem [runbook3D](phase-3d-runbook.md).

## PDF, Excel và Word

Lưu nháp trước, mở **Tải / quét tài liệu PDF, Excel hoặc Word lịch tuần** tại `/admin/documents/{contentId}`. Nhận một tệp/lần, tối đa10MiB. PDF; Excel `.xlsx` dạng giá trị thuần; Word `.docx` chỉ cho mẫu lịch tuần. Chuyển DOC/XLS cũ bằng phần mềm Office ngoài hệ thống trước khi tải; không nhận macro, formula Excel, liên kết ngoài/nhúng nguy hiểm. Kiểm tra định dạng khác với quét mã độc.

Upload tạo MediaAsset mới và hàng đợi SQL riêng, lưu ngoài `wwwroot` trong `editorial-private/`, **NotScanned/cách ly**. Màn hình ghi trạng thái thật. Chạy worker Test theo runbook, làm mới để đọc kết quả. Chỉ **Clean + engine/hash/thời gian đúng byte + định dạng hợp lệ** mới cho tải riêng tư hoặc chọn vào preview. Infected/ScanFailed/NotScanned không có quyền tải, kể cả người biên tập. Worker không tự công khai hoặc sửa bản nháp.

Với PDF/XLSX: chọn **Đọc / thêm vào preview biên tập**, rà soát nhãn, lưu nháp. FR-17 cần đủ tỉnh/mã tỉnh/năm/nguồn/căn cứ/xác nhận thẩm quyền và PII, gửi duyệt rồi xuất bản đúng revision bằng quyền riêng; không đăng trực tiếp. Sửa sau duyệt phải duyệt lại. URL `/media/{id}` chỉ tải khi có tham chiếu trong revision hiện đang được công khai và file đủ điều kiện. Thu hồi bài/quyền hoặc hash sai đóng quyền ngay, không dựa vào cache cũ.

Luồng tài liệu mới hiện **chỉ mở trên Development + LandPortal_Test + kho Test**. Dev/Production không tự mở upload khi chưa có hạ tầng/policy scanner được duyệt. Không dùng `-AllowUnscannedMedia` để vượt luồng này;53 tài nguyên nguồn không tham gia hàng đợi mới, vẫn NotScanned.

PDF đã upload qua luồng2A cũ cũng bị chặn công khai nếu chưa có proof quét thật, kể cả khi bật ngoại lệ Development. Sau khi gắn vào nháp và lưu, mở trang tài liệu và chọn **Đưa PDF này vào hàng đợi quét**. Byte, tên và revision không đổi; chỉ chuyển sang kho logic cách ly/hàng đợi, chờ engine. Không tự tiếp nhận tài nguyên Source hoặc reset kết luận Infected/Failed. PDF upload cũ dùng chung nhiều hồ sơ cần đối soát trước, không tự đổi phạm vi. Ngoại lệ ảnh local của2A giữ nguyên;53 nguồn không bị tự ẩn hoặc đổi trạng thái.

## Lịch tuần Word

1. Tạo mẫu lịch tuần bằng `/admin/create?kind=StaticPage&template=weekly`; chọn đúng năm/tuần ISO, nhập ít nhất một dòng để lưu hồ sơ nháp. Có thể tiếp tục nhập tay nếu không có Word.
2. Tải DOCX tại trang tài liệu, chờ worker quét thật. Tệp gốc và SHA được giữ riêng tư, không chuyển thành HTML công khai trực tiếp.
3. Chọn đọc vào preview. Mẫu kỹ thuật có7 cột: Ngày, Buổi, Giờ, Nội dung, Chủ trì, Thành phần, Địa điểm; tùy chọn cột8 Ghi chú. Ô gộp dọc ngày/buổi được kế thừa; gộp ngang mơ hồ có cảnh báo và cần nhập tay. Ngày phải thuộc tuần chọn. Không tự tải liên kết hoặc chạy Word.
4. Đối chiếu từng dòng, sửa trong trường preview. Lỗi chỉ rõ bảng/dòng khi xác định được; tệp sai mẫu không đổi lịch đã lưu/công khai. Có thể giữ bảng cũ rồi sửa tay, vẫn giữ nguồn Word đang chọn. Tệp có tracked changes/chữ ẩn phải được rà soát hoặc nhập tay, không tự nhận là đã đọc đúng.
5. Rà soát tệp gốc, bao gồm metadata/comments và thẩm quyền công khai; ghi xác nhận nội bộ. Bấm **Lưu bản nháp**, gửi duyệt/duyệt/xuất bản theo quyền. Preview GET chưa phải một revision được lưu. Bản live chỉ đổi khi xuất bản.
6. Trang `/lich-tuan` hiện phiên bản mới nhất và link Word gốc đúng revision. Bản Word cũ được tải trong quản trị qua lịch sử/tài liệu; không mở public history khi PO chưa duyệt. Khôi phục trong lịch sử bài tạo revision mới, duyệt/đăng sẽ tạo phiên bản tiếp theo, giữ Word gốc của revision đã chọn.

**AC-16.1 vẫn BLOCKED:** chưa có ba Word nghiệp vụ thật thuộc ba tuần khác nhau; fixture QA tự tạo chỉ chứng minh kỹ thuật, không tạo lịch tuần chính thức trên Dev.

## Tìm kiếm chung

`/tim-kiem` tìm bài đã xuất bản và hỏi đáp đã duyệt công khai; bộ lọc loại có **Hỏi đáp đã công khai**. Có ngày, chuyên mục, thứ tự và phân trang SQL10 kết quả/trang. Khi lọc chuyên mục tin, hỏi đáp không được gán chuyên mục giả để xuất hiện. Kết quả hỏi đáp dùng nội dung/đáp án đã duyệt, không dùng email/số điện thoại/tệp hoặc bản gốc riêng tư. Bỏ đồng ý công khai, từ chối hoặc đổi snapshot sẽ thu hồi kết quả cũ.
