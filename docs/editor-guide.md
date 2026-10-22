# Hướng dẫn biên tập CMS — 2A, 2B và V04

## Bổ sung V04 — 14/10/2026

Xem V04 ở `http://127.0.0.1:5198/admin/login`, chạy `rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -IsolatedBuild -NoBuild -Port 5198`. Không dừng web5188 có trước lượt. Khi thay code, bỏ NoBuild. Credential/quyền cũ giữ nguyên.

Mục chính và **mọi mục phụ** phải trong phạm vi được giao; endpoint/list/history/media/preview đều kiểm tra. Người chưa có scope claims giữ phạm vi legacy, migration không tự gán scope/quyền. Thay scope cần người đủ quyền, stamp bị thu hồi; không tự cấp để vượt403.

Idle30min; pollingGET không renew. Nếu401, dialog reauth giữ prose **trong tab đang mở**, không localStorage. Dùng đúng tài khoản đã mở bản soạn; mật khẩu được xóa khỏi ô sau yêu cầu. Sau xác thực bấm Lưu để kiểm tra quyền/rowversion mới, không tự đăng.409 vẫn phải đối chiếu người khác đã sửa; không đóng/reload khi chưa lưu.

Legal until/boundary UTC+7 chỉ nhập khi có căn cứ/evidence và rule xác nhận. Chưa rõ biên thì để trống; không tự đoán ngày/status pháp lý. Metadata sửa tạo revision mới, phải duyệt lại, không đổi public trước publish. Không sửa hàng loạt văn bản nguồn thiếu căn cứ.

Thông báo chỉPDF không bắt ảnh/cover/body giả; title/chuyên mục/tệp hợp lệ/quyền publish vẫn cần. Draft/preview ảnhPDF không mở khách. Toolbar public là chức năng đọc; bình luận vẫn chờ2C. Nội dung2A/2B dưới đây tiếp tục áp dụng.

Site local: `http://127.0.0.1:5188/`, admin `/admin/login`. Credential vận hành nằm trong `.env` local; không có mật khẩu mặc định và không chia sẻ credential qua tài liệu. Bằng chứng nghiệm thu thực nằm trong `docs/phase-2a-report.md` và `docs/acceptance-matrix.md`.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1
```

Để thử upload ảnh/PDF local khi chưa có scanner, phải chọn ngoại lệ tường minh:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -AllowUnscannedMedia
```

Lệnh này chỉ bật flag trong tiến trình Development trên loopback, không sửa `.env`. Mặc định upload bị chặn và UI báo lý do thật. Tệp upload vẫn **NotScanned**, không phải đã quét; Production không chấp nhận ngoại lệ này. Giới hạn mặc định: ảnh 16 MiB, PDF 32 MiB, chỉ JPEG/PNG/GIF/WebP/PDF; kiểm tra định dạng/ảnh/PDF thực, không chỉ dựa vào tên tệp.

Mỗi tài khoản được cấp tập quyền cụ thể. Người soạn có quyền tạo/sửa nội dung của mình và gửi duyệt. Người duyệt có quyền duyệt/trả bài; xuất bản cần quyền xuất bản riêng. Người được xuất bản trực tiếp có quyền rút gọn; mọi thao tác vẫn kiểm tra concurrency, revision và audit phía server.

Quyền của tài khoản/nhóm hiện có được giữ nguyên, không tự tăng quyền hoặc đổi mật khẩu khi migration. Màn hình quản trị tài khoản/nhóm được bổ sung ở 2B; xem [hướng dẫn tài khoản](account-admin-guide.md). Nếu tài khoản hiện tại chưa có quyền cho một thao tác mới, endpoint từ chối và giao diện không báo thành công.

Tài khoản vận hành cũ được giữ nguyên quyền/mật khẩu. Các quyền `content.unpublish`, `content.trash`, `content.restore` và quyền quản trị mới của 2B không tự được cấp: nếu chưa có quyền, nút tương ứng không hiện và endpoint từ chối. Danh sách quyền tại `cms-architecture.md`; cách cấp trong phạm vi được phép tại [hướng dẫn tài khoản](account-admin-guide.md). Các tài khoản và nội dung kiểm thử được tạo riêng trong SQL Test.

Mẫu bài dùng tiêu đề, mô tả, chuyên mục chính/phụ, tác giả/nguồn, ảnh đại diện, nội dung rich text, ảnh có chú thích, bảng và PDF. Biên tập trực tiếp trong editor, không nhập HTML. Chèn ảnh/PDF từ kho hệ thống hoặc tải từ máy; không dùng đường dẫn nguồn cũ hay nhập URL tệp tùy ý. Video tải trực tiếp thuộc mốc 2D, không thay bằng YouTube.

1. Trong **Bài viết**, tìm/lọc theo tiêu đề, chuyên mục, người soạn, trạng thái; **Soạn bài mới** mở mẫu. Với bài đã đăng/chờ duyệt/đã duyệt, chọn **Mở bản sửa mới** trước khi biên tập. Trạng thái công khai và trạng thái bản sửa được hiển thị riêng.
2. Nhập tiêu đề, mô tả, chuyên mục chính/phụ, tác giả/nguồn. Bài mới cần tiêu đề, chuyên mục chính, nội dung hoặc tài nguyên. Bài nguồn được giữ trường thiếu như ảnh/tác giả/mô tả, không bắt bổ sung hàng loạt.
3. Soạn trong editor Jodit 4.17.3 tự host (MIT). Thanh công cụ có định dạng, danh sách, bảng, liên kết; dropdown đoạn văn có tiêu đề/trích dẫn. Dán từ Word được làm sạch; không có upload Word chuyển bài tự động. **Chèn ảnh có chú thích** mở kho media, nhập chú thích rồi chọn ảnh; **Thêm PDF đính kèm** chọn PDF. Panel ảnh đại diện dùng **Chọn hoặc tải ảnh**.
4. Chọn **Lưu bản nháp** hoặc chờ tự lưu debounce 1,6 giây. Thông báo giờ lưu là kết quả SQL thực. **Xem trước bản đã lưu** xem dữ liệu đã lưu, không đưa thay đổi chưa lưu vào preview. Bài mới được chuyển tới trang chỉnh sửa sau lần lưu thành công đầu tiên.
5. Người soạn chọn **Lưu và gửi duyệt**. Người duyệt mở đúng bài/bản đang chờ; **Trả bài** bắt buộc có lý do hoặc **Duyệt phiên bản này**. Người có quyền xuất bản phiên bản đã duyệt chọn **Xuất bản bản đã duyệt**. Quyền đăng trực tiếp có nút riêng; quyền duyệt không tự bao gồm đăng trực tiếp.
6. **Lịch sử và so sánh phiên bản** ghi người sửa, thời điểm, bản hiện tại/công khai và audit. Xem trước/so sánh tiêu đề, mô tả, nội dung và tài nguyên; **Khôi phục thành bản nháp** tạo revision mới để kiểm tra/duyệt lại. **Nhân bản thành bài nháp mới** không mang source identity/alias của bản gốc.
7. **Gỡ bài** hoặc đưa vào thùng rác cần xác nhận. Khôi phục từ thùng rác giữ lịch sử và không tự đăng lại; chỉ đăng sau thao tác theo quyền.

Bản đang công khai và bản đang biên tập tách biệt. Khi sửa bài đã đăng, khách tiếp tục thấy bản đã đăng, kể cả khi gửi duyệt bản sửa. Chỉ xuất bản đúng revision được duyệt hoặc dùng quyền xuất bản trực tiếp mới thay nội dung công khai. Sửa bản đã duyệt phải làm lại vòng duyệt.

Lưu nháp/tự lưu chỉ báo thành công sau khi SQL đã lưu. Khi xung đột với phiên khác, giữ nội dung đang nhập và đối chiếu với phiên bản mới; không tự ghi đè. Trước chuyển trạng thái, chờ lưu hoàn tất. Không đóng trang khi có cảnh báo thay đổi chưa lưu.

Xem trước cần đăng nhập và quyền xem nội dung. Media chỉ được dùng trong nháp không mở cho khách dù khách biết URL. Tệp nguồn gốc/PDF không bị ghi đè khi tạo bản chỉnh sửa hoặc thumbnail. Tệp chưa được scanner thực quét sẽ hiển thị NotScanned; ngoại lệ development chỉ là thao tác local, không chứng nhận tệp sạch hay áp dụng ngầm ở production.

Khi bỏ ảnh khỏi nội dung/ảnh đại diện hoặc bỏ PDF trong panel đính kèm, lưu rồi xuất bản bản sửa để cập nhật tham chiếu public. Tệp không còn được bài công khai nào dùng sẽ trả 404 cho khách; tệp dùng chung vẫn phục vụ bài khác. Phiên bản cũ giữ tệp và người có quyền vẫn xem trước được trong lịch sử. Bỏ một ảnh chỉ trong nội dung nhưng vẫn chọn làm ảnh đại diện không thu hồi ảnh đó.

Gỡ bài ngừng phục vụ public nhưng giữ lịch sử. Thùng rác không hard-delete source mapping/media. Khôi phục từ thùng rác không tự xuất bản lại. Khôi phục revision tạo draft mới; kiểm tra rồi duyệt/đăng theo quyền. Nhân bản bài tạo một draft riêng không mang định danh nguồn duy nhất.

Hẹn giờ bằng worker và quản trị các hạng mục khác không thuộc phần bàn giao 2A; phần bổ sung 2B được hướng dẫn bên dưới. Bằng chứng 2A trong báo cáo cũ giữ nguyên ý nghĩa của lần chạy đó, không tự chứng minh 2B. Dự án CHƯA PRODUCTION READY.

## Các mẫu nội dung bổ sung ở 2B

Chọn loại nội dung khi soạn mới hoặc lọc danh sách theo loại. Không đổi loại của đối tượng đã có. Tin/bài và các mẫu dưới đây cùng dùng editor, media picker, gửi duyệt/duyệt/đăng/gỡ/khôi phục và quyền của 2A; không phải nhập HTML hoặc JSON để soạn bài.

- **Thông báo:** tiêu đề, mô tả, nội dung, đơn vị, tác giả/nguồn, chuyên mục, ảnh và các PDF có nhãn. Ngày hiển thị/ngày đăng là một trường riêng; không lấy một ngày sự kiện/hạn xử lý trong thân bài làm ngày đăng.
- **Văn bản pháp luật:** số ký hiệu, trích yếu/tiêu đề, loại văn bản, cơ quan ban hành, người ký, lĩnh vực, ngày ban hành và ngày đăng xác nhận. Các danh mục gợi ý lấy từ cấu hình đang áp dụng; chữ nguồn chưa ánh xạ được giữ và có trạng thái rà soát. Số ký hiệu không phải khóa duy nhất của toàn kho.
- **Văn bản dự thảo:** phần giới thiệu/nội dung, cơ quan, loại/lĩnh vực và khoảng lấy ý kiến có bằng chứng. Ngày đăng tách khỏi ngày bắt đầu/kết thúc lấy ý kiến. Trạng thái chưa mở/đang mở/đã đóng/chưa xác định được tính từ khoảng thời gian của bản công khai.
- **Trang giới thiệu:** dùng thông tin tổ chức đã được cung cấp hoặc xác minh. Cho phép bài chỉ có ảnh/PDF thật và không có thân bài chữ; không bắt nhập văn bản bịa để lưu sơ đồ/ tài liệu giới thiệu. Không tự bổ sung tên lãnh đạo hoặc cơ cấu tổ chức.

Ngày ban hành, ngày đăng xác nhận và khoảng lấy ý kiến nhập dưới dạng ngày, giữ nguyên ngày Việt Nam. Ngày kết thúc lấy ý kiến được hiểu hết ngày UTC+07:00, tức mốc đóng là đầu ngày kế tiếp; đây là quy ước ứng dụng. Chưa có bằng chứng ngày thì để trống. Khoảng bắt đầu sau kết thúc bị từ chối.

Ngày hiệu lực/trạng thái còn hiệu lực, hết hiệu lực hoặc hiệu lực một phần chỉ nhập khi có **căn cứ xác nhận hiệu lực**. Thiếu căn cứ phải để chưa xác định; không suy ra hiệu lực từ ngày ban hành, tiêu đề hoặc số văn bản.

Các trường này thuộc revision. Sửa số văn bản, cơ quan, ngày, hiệu lực, hạn lấy ý kiến, ảnh hoặc nhãn PDF trong bản nháp không thay bản đang công khai hay kết quả tra cứu. Duyệt gắn với đúng revision/hash; sửa sau duyệt cần gửi duyệt lại. Mở bản sửa mới và khôi phục lịch sử luôn tạo vùng biên tập riêng, không tự công khai.

## Chuyên mục, PDF và xem trước

Picker chuyên mục dùng danh mục trong cấu hình đang áp dụng. Chuyên mục bổ sung mới chỉ nằm trong cấu hình nháp chưa trở thành mục để biên tập sử dụng công khai. Giữ đủ 14 chuyên mục nguồn; tắt một khối trên trang chủ không xóa chuyên mục hoặc nội dung. Cấu hình menu/chuyên mục thuộc [hướng dẫn quản trị cổng](site-admin-guide.md).

Sau **Thêm PDF đính kèm**, nhập **Nhãn tài liệu** để phân biệt các tệp của cùng một văn bản, ví dụ phụ lục và nội dung chính. Nhãn được lưu cùng revision và xuất hiện theo bản đã xuất bản. Bỏ PDF ở panel rồi lưu chỉ thay bản nháp; muốn cập nhật public cần xuất bản bản sửa theo đúng quyền. Không phải tải lại ảnh/PDF nguồn đã có trong kho.

**Xem trước bản đã lưu** cần đăng nhập và quyền xem. Preview lấy các trường đã lưu; thay đổi chưa lưu trong editor chưa xuất hiện. Tài nguyên chỉ thuộc bài nháp hoặc cấu hình banner/logo nháp không tải được bởi khách. Tệp dùng chung với một bài/cấu hình live vẫn có thể được phục vụ theo tham chiếu đang công khai; bỏ một tham chiếu không xóa tệp đang được nơi khác dùng.

## Tra cứu và các màn hình liên quan

Trang tìm kiếm public tìm phần chữ của bài đã xuất bản và metadata; không tìm chữ nằm trong PDF/ảnh hoặc OCR. Có bộ lọc loại nội dung, chuyên mục và ngày đăng. Tra cứu văn bản có thêm cơ quan, loại, lĩnh vực và ngày ban hành; ngày đăng và ngày ban hành là hai bộ lọc riêng. Query string giữ bộ lọc khi quay lại hoặc chia sẻ URL. Bản nháp không tham gia kết quả, tổng số hoặc snippet public.

Người có `content.schedule` và quyền xuất bản phù hợp mở **Hẹn giờ revision đã lưu**. Lịch trỏ tới revision cụ thể, giờ nhập/hiển thị Việt Nam, không lấy bản nháp mới nhất. Sửa/rút duyệt/gỡ/trash hoặc xuất bản thủ công có thể vô hiệu lịch cũ; phải kiểm tra trạng thái lịch và lập lại theo quyền. Worker phải đang chạy; màn hình thể hiện heartbeat và lý do lịch cần rà soát. Xem [runbook scheduler](scheduler-runbook.md) để chạy/dừng worker thật, xử lý retry hoặc lịch quá hạn.

Banner/logo/menu/trang chủ có cấu hình nháp, xem trước riêng và thao tác áp dụng có quyền riêng; lưu nháp không đổi public. Xem [hướng dẫn quản trị cổng](site-admin-guide.md). Quản trị tài khoản, nhóm và thu hồi phiên xem [hướng dẫn tài khoản](account-admin-guide.md); không tự thêm quyền vào tài khoản vận hành khi thiếu quyền.

Bình luận dưới bài và các nội dung công chúng gửi phải có kiểm duyệt ở 2C. Video mới phải tải trực tiếp từ máy biên tập viên ở 2D, không thay bằng YouTube. Chuyển toàn bộ dữ liệu nguồn thuộc Giai đoạn 3 bằng công cụ tự động, không yêu cầu biên tập viên nhập lại từng tin. Scanner, restore thực, MFA, UAT và nghiệm thu production chưa được xác nhận bởi tài liệu hướng dẫn này.
