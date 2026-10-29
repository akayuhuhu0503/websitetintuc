# Lịch tuần và Công khai thông tin v0.5

## Lịch tuần nhập tay

Trong CMS chọn **Soạn lịch tuần nhập tay** (`/admin/create?kind=StaticPage&template=weekly`). Đây là mẫu trang tĩnh thuộc hạng mục Lịch tuần76 đã có, dùng cùng working draft/revision/workflow/quyền2A; không tạo CMS lịch thứ hai.

Chọn năm và tuần ISO (thứHai–Chủnhật, ngày theoViệtNam), điền từng dòng ngày/buổi/giờ/nội dung/chủtrì/thànhphần/địađiểm/ghichú. UI chuẩn bị14dòng; service giới hạn100dòng/30.000ký tự JSON và2.000ký tự/cell. Dòng trống bỏ qua. Nội dung bảng được sinh có encoding/sanitize, không xuất metadata nháp. Lưu và preview qua quyền hiện hữu, gửi duyệt hoặc đăng trực tiếp chỉ nếu đã có quyền; không tự grantDev.

Mỗi lần **đăng revision mới** ghi `WeeklyScheduleReleases`: sốphiênbản trongtuần, UTC/ngườiđăng/ghichú/revisionID. Không ghi đè release cũ. Chỉ một hồ sơ lịch được phát hành cho một tuần; nếu đã có, tìm/sửa hồ sơ đó. Không chuyển tuần của hồ sơ đã phát hành. Race bị SQLtransaction/applock/uniqueindex/rowversion chặn. Sửa nháp không đổi lịch công khai.

Public `/lich-tuan?year=2026&week=42` chọn đúng PublishedRevision đangcôngkhai, hiển thị phiênbản/ngàycậpnhật, nút tuầntrước/tuầnsau. Bài gỡ không còn public. Lịch sử release chỉ admin `/admin/lich-tuan/{contentID}`; lịch sử bài hiện hữu `/admin/history/{contentID}` cho chọn khôi phục. Khôi phục tạo **draft/revision mới**, cần đăng lại để thành phiênbản tiếp theo (ví dụv3), không tự republish. Public history chưa mở vì chưa PO quyết định. Nội dung lịch nhập nguồn cũ vẫn có tại `/lich-tuan/nguon`.

## DOCX kỹ thuật — PARTIAL, chưa là luồng upload hoàn chỉnh

`WeeklyDocxReader` đọc mẫu **Technical-v05-1**: một bảng8cột `Ngày / Buổi / Giờ / Nội dung / Chủ trì / Thành phần / Địa điểm / Ghi chú`. Gộp dọc kế thừa ngày/buổi; gộp ngang mơ hồ ghi cảnh báo và bỏ khỏi preview để nhập tay, không âm thầm công khai. Kiểm ngày thuộc tuần, trùng nội dung, row/cell/ZIP/XML/ratio/type/path/external relationships/macros/hidden/trackedchanges limits. Không mở Word/COM, không tải quan hệ ngoài, không ghi SQL/publicfile. Metadata/comments ở file gốc chỉ được cảnh báo riêng tư; trackedchanges/hiddentext bị từ chối để làm sạch/nhập tay.

Đã có fixture kỹ thuật, **chưa có3file lịch nghiệp vụ khác tuần**. `AC-16.1=BLOCKED:NO_REAL_WORD_SAMPLES` theo xác nhận người dùng15/10. Mẫu kỹ thuật chưa được Văn phòng Cục duyệt. **Chưa nối upload DOCX→scannerprivate→preview/sửa→workflow→filedownload** và chưa policy công khai fileWord gốc. Không có nút upload giả. Bước tiếp cần mẫu thật/policy scanner + fileoriginal review; tệpNotScanned không được gọiClean. Hiện dùng nhập tay đã có phiên bản; không tự nhập fixture vàoDev.

## FR-17 — group bổ sung, giữ14chuyên mục nguồn

Trong mẫu bài viết có fieldset **Công khai thông tin**. Chọn nhóm ngoài chuyên mục chính/phụ đang dùng cho scope; không thay14category/importer/URLsource. Ba group SQL được seed idempotent trong migration, chưa seed bài:

- Giấy chứng nhận sai hỏng, hủy.
- Người nước ngoài định cư ở Việt Nam — **NeedsNameApproval**, nội bộ; giữ nguyên tênSRS, không public danh mục này cho đến khi PO/pháp chế xác nhận.
- Tổ chức vi phạm pháp luật đất đai.

Nhập tỉnh/mãtỉnh/năm, nguồn chínhthức, căn cứpháthành và xácnhận thẩmquyền/PIIreview. Chưa có dữ liệu thật/danhmục tỉnh được đơn vị cung cấp thì không bịa nội dung. Chỉ metadata của PublishedRevision hiện trênpublic/filter/search; căn cứreviewPII nội bộ không được hiển thị. Directpublish luôn bị từ chối vớiFR17; phải Submit→Approveđúngrevision→PublishApproved, người đăng còn cần `content.disclosure.publish` và quyền/phạmvi category2A hiệnhành. Scheduler cũng đi qua cùng policy/freshrights. Tên chưa duyệt/chưa đủ nguồn-căn cứ-review hoặc cótệpNotScanned thì khôngpháthành.

Public `/cong-khai-thong-tin` chỉ liệt kê tên được xác định trongSRS; nhómchờduyệt404. `/cong-khai-thong-tin/{group}` lọc tỉnh/mãtỉnh/năm/từkhóa vàphântrangSQL10, giữdraftprivate. Không bình luận: cảform/service/publicsnapshotmoderation đều chặn theoPublishedRevision, khôngchỉẩnnút. Gỡ/đínhchính dùngworkflow2A vàaudit/revision; khôngharddelete lịch sử. Chưa có tra cứu từngserial/tên tổchức.

**Còn thiếu:** upload/scan/fileproofCMS cho PDF/Excel theoFR17 và Excelacceptance; hiện pickerCMS chỉPDF vàpolicyFR17 không dùngngoạilệNotScanned. Route FR17 hoạt động trực tiếp nhưng **chưa bổ sung đích typed menu Công khai thông tin vào catalog site2B**; chưa seed/applymenu live hoặc cấpquyềnDev. Bước tiếp bổ sung catalog/menu draft vàtest riêng trước apply. Danhmục tên được seed không đồngnghĩa nội dung cá nhân cóthẩmquyền được phép côngkhai. ToànFR17 hiệnPARTIAL.
# Cập nhật V05-Finish

Hướng dẫn hiện hành: [V05-Finish admin](v05-finish-admin-guide.md) và [worker tài liệu](editorial-documents-runbook.md). Đã có luồng Word/PDF/Excel ở SQL Test với scanner thật, menu nháp FR17 và tìm kiếm hỏi đáp. Phần dưới giữ nguyên hồ sơ hướng dẫn v0.5 delta trước Finish; các ghi chú “chưa Word/Excel/menu” là checkpoint cũ. AC16.1 vẫn cần ba Word nghiệp vụ thật, không thay bằng fixture.
