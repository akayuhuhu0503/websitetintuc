# Màu sắc và điều hướng GDLA

Nhận diện công khai dùng `#8B2E2E`, hover `#702424`, nền nhấn `#F7EEEE`; nền trang sáng, chữ xám đen. Giữ Roboto/Merriweather, tên cơ quan, URL và màu riêng của trạng thái thành công/cảnh báo/lỗi. Tham khảo cách chia nhóm và tiện ích của [PPD](https://www.ppd.gov.vn/), không sao chép tài sản hoặc liên kết của nguồn.

## Cấu trúc menu đề xuất trong CMS

| Nhóm chính | Các mục con và đích hiện có |
| --- | --- |
| Trang chủ | `/` |
| Giới thiệu | `/gioi-thieu/lanh-dao-cuc`, `/gioi-thieu/chuc-nang-nhiem-vu`, `/gioi-thieu/co-cau-to-chuc` |
| Tin tức – Sự kiện | `/tin-tuc`, các chuyên mục CMS đang cho phép hiện trong điều hướng |
| Văn bản | `/van-ban`, `/du-thao` |
| Công khai thông tin | `/cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy`, hai nhóm công khai đã triển khai theo SRS |
| Tra cứu – Hỗ trợ | `/so-tay`, `/thong-ke-dat-dai`, `/hoi-dap` |
| Thư viện | `/video`; chỉ thêm loại thư viện khác khi đã có route hợp lệ |
| Liên hệ | `/lien-he` |

Đặt Lịch tuần (`/lich-tuan`) ở vị trí `supplementary` để hiện thành hàng tiện ích riêng. Chỉ thêm Thư điện tử bằng URL chính thức được xác nhận; không tự suy đoán URL. Thống kê đất đai dùng đích **Đường dẫn nội bộ** `/thong-ke-dat-dai` nếu danh sách Mục hệ thống chưa có mục tương ứng.

## Áp dụng qua CMS

Lần đọc HTML công khai ngày 09/11/2026, menu chính của IIS chưa có mục. Thay source chỉ đổi cách hiển thị, không tạo hoặc công bố cấu hình.

1. Người có quyền `site.manage` mở `/admin/site?section=menus`, thêm/sắp xếp các **Nhóm menu** và chuyển mục con trong bản nháp; giữ ID, đích URL và chuyên mục hiện có. Không tạo bản sao những mục đã tồn tại.
2. Chọn **Xem trước nháp** tại `/admin/site/preview`; kiểm tra desktop/mobile và đích công khai. Nếu CMS chưa được khởi tạo, cần xử lý điều kiện khởi tạo theo runbook hiện hành trước; không dùng SQL trực tiếp hoặc tự tạo menu thay thế.
3. Người có quyền `site.publish` chọn **Áp dụng bản nháp** sau khi rà soát. Lưu nháp không làm đổi menu công khai.

Dropdown hiển thị từ đúng revision CMS; nhiều mục chia cột, nhóm con thu gọn. Mục cha có URL vẫn có liên kết Tổng quan trong panel. Enter/Space mở nhóm, Escape đóng và trả focus, chạm mở/đóng trên mobile; trạng thái trang hiện tại được đánh dấu theo URL nội bộ. Không có menu đã công bố thì không dựng menu giả.

## Phạm vi bàn giao

Chỉ CSS, Razor và điều khiển điều hướng phía trình duyệt; không đổi SQL, tài khoản, quyền, dữ liệu 33 bài hoặc pipeline 252 bài/703 media. Bản kiểm tra riêng dùng HTML công khai và model menu thử cục bộ, không ghi cấu hình lên LAN/IIS. Cập nhật source lên IIS do người vận hành thực hiện sau khi chốt giao diện; lượt này không deploy IIS.

Build Web/Razor: 0 warning, 0 error. Kiểm tra tập trung 6 loại trang ở 390/768/1366/1920px: không tràn ngang; menu desktop không xuống hàng; dropdown, bàn phím, focus và trạng thái trang hiện tại hoạt động trên preview. Sáu URL gốc lấy snapshot trả HTTP 200. Tương phản chữ trắng/primary 8,31:1, trắng/hover 10,68:1, primary/nền mềm 7,29:1; đây là kiểm tra các cặp màu liên quan, không thay thế nghiệm thu WCAG toàn website. Không chạy full regression hoặc chụp ảnh/quay video.
