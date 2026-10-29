# Thiết kế Giai đoạn 0–1

Ngày lập: 12/10/2026. Đây là hệ thiết kế và phạm vi kiểm tra, không phải chứng nhận WCAG hoặc nghiệm thu production.

- Nhận diện lấy từ nguồn qldd.mae.gov.vn đã lưu snapshot; không sinh logo hay suy diễn tên/cơ cấu cơ quan. gdla.gov.vn chỉ là tham chiếu giao diện nếu truy cập được.
- Màu chính xanh lam đậm, điểm nhấn xanh lục tiết chế, nền trắng/xám nhạt. Các màu, spacing và giới hạn chiều rộng dùng biến CSS.
- Khung desktop khoảng 1.320 px; cột đọc bài khoảng 780 px, thân bài 18 px, line-height khoảng 1,75. Không dùng slideshow tự chạy.
- Trang chủ trình bày một tin chính, nhóm tin phụ, thông báo, lối vào văn bản/dự thảo/sổ tay và chuyên mục. Giữ đủ tên và quan hệ chuyên mục.
- Menu Tin tức có toàn bộ 14 chuyên mục; menu bấm mở bằng bàn phím/mobile. Liên kết chức năng chưa làm dẫn đến trang phạm vi có thông báo cụ thể, không có nút gửi hoặc thông báo thành công giả.
- Danh sách có ngày, ảnh nếu tồn tại, phân trang thật và query trong URL. Trang bài giữ nguyên ảnh/tài liệu nguồn đã nhập, không lấy ảnh ngoài để che thiếu dữ liệu.
- Văn bản có mẫu tra cứu riêng cho số ký hiệu, cơ quan, ngày; các trường chưa có bằng chứng giữ trống. Không suy đoán ngày/trạng thái hiệu lực.
- Admin có đăng nhập Identity, danh sách và form soạn/lưu nháp/xuất bản theo quyền. Thành công chỉ sau SQL commit; xung đột rowversion phải báo.
- Video mới, tương tác công chúng và cấu hình trang chủ đầy đủ thuộc giai đoạn 2; không thay upload video bằng ô dán YouTube.

Kiểm tra thực tế phải ghi ở `acceptance-matrix.md`: viewport 360, 390, 768, 1.024 và 1.440 px; trang chủ/chuyên mục/bài/văn bản/admin; menu bấm, focus, skip link, form labels, chữ tiếng Việt dài, bảng và ảnh dọc. Screenshot lưu `.local/evidence/ui/`, ngoài Git. Reflow/keyboard không đồng nghĩa chứng nhận toàn bộ WCAG 2.2 AA.
