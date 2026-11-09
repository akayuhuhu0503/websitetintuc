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

## Bố cục trang chủ — 09/11/2026

Trang chủ mở bằng một tin chính (khoảng 66% cụm tin trên desktop, ảnh 16:9, tiêu đề 28–36px), hai tin phụ bên phải và hàng tin mới bên dưới. Tiếp theo là chuyên mục: một bài lớn và danh sách bài phụ; thông báo, văn bản và **Tra cứu và tiện ích** nằm sau phần tin tức. Giữ nhận diện, header/menu/thanh tin/footer. Bài không ảnh dùng tin chữ; tiêu đề mobile không bị cắt. `home.css` chỉ tải trên hai view trang chủ. Tin đọc nhiều giữ thống kê thật 7/30 ngày và lưới 1/2/3/4 cột ở 390/768/1366/1920px.

CMS tiếp tục dùng **Featured/PinnedContentIds**, không tạo cơ chế Featured thứ hai. Tin ghim hợp lệ theo thứ tự CMS được chọn trước, sau đó là cờ Featured của revision đã xuất bản và thứ tự ngày hiện hành. Lọc pin không hợp lệ trước khi áp dụng giới hạn; loại lịch tuần khỏi cụm tin chính, không lặp bài giữa Featured và Recent. Chỉ lấy Article công khai và thumbnail Clean, không lấy ảnh của bản nháp hoặc file cách ly. Thứ tự/số lượng từng chuyên mục giữ theo CMS. Cấu hình cũ có Featured nhưng chưa có khối Recent được bổ sung danh sách tin mới trong bộ nhớ để đủ tối đa bảy tin; khối Recent đã tắt vẫn được tôn trọng, không ghi lại cấu hình. Không tự gắn nhãn “Tin nóng”.

Build Web/Razor PASS (0 warning/error). Tám kiểm tra chọn tin bằng truy vấn chỉ đọc PASS: ghim, loại private/lịch tuần, chống trùng, Featured, giới hạn CMS, thumbnail Clean và số truy vấn có giới hạn, không N+1. Cả view Configured và fallback được render từ Razor đã compile với dữ liệu LAN; tám tổ hợp view/kích thước PASS, ảnh tải đúng, không tràn ngang. Menu/bàn phím, bộ chọn 7/30 ngày, tìm kiếm, mở bài và URL bài với slug cũ PASS. Hai biến thể không ảnh/ít bài chỉ sửa DOM trong trình duyệt, không tạo dữ liệu. Không full regression, không ghi SQL, không sửa tài khoản hoặc triển khai IIS.

Không cần cấu hình mới để dùng bố cục. Để chủ động chọn tin chính, người có quyền cấu hình vào `/admin/site?section=blocks`, sắp xếp bài ghim trong khối Featured rồi xem trước và áp dụng qua workflow CMS hiện có. Nếu chưa ghim/đánh dấu Featured, hệ thống chọn Article hợp lệ theo thứ tự ngày; lịch tuần không chiếm vị trí này.

### Hoàn thiện kích thước và nhịp đọc

Ảnh hero desktop giới hạn 300–420px theo viewport, tiêu đề 30–34px/tối đa ba dòng; mobile giữ đầy đủ tiêu đề. Đo local 1920×1080: ảnh giảm từ 590 xuống 420px, tiêu đề từ khoảng y=981 xuống y=801 và kết thúc y=939, nằm trong màn hình đầu. Ảnh dùng cover, không kéo méo. Hai tin phụ có thumbnail đồng nhất và tiêu đề tối đa ba dòng trên desktop.

Chuyên mục có nhiều thumbnail hợp lệ dùng mẫu ảnh lớn + tin phụ; ít ảnh dùng bài chính + danh sách chữ, không phân loại bằng tên hardcode. Văn bản dùng mẫu riêng, hiển thị số ký hiệu có sẵn từ revision Published; chỉ bổ sung trường trình bày, không đổi schema. Giữ thứ tự, số bài và URL CMS. Tin đọc nhiều trên trang chủ hiển thị tối đa bốn card đầu, phần còn lại thành hàng chữ gọn theo đúng thứ hạng; ba bài dùng ba cột desktop. Danh sách trong sidebar bài chi tiết không đổi.

Build PASS 0 warning/error. Hai view × bốn breakpoint PASS; trường hợp 1/3/5 tin đọc nhiều và không ảnh/ít bài PASS (biến thể chỉ dùng DOM, không ghi dữ liệu). Tin thứ năm cao khoảng 42px trên desktop, không thành card lẻ. Menu, tìm kiếm, mở bài, URL cũ, bộ chọn 7/30 và kiểm tra ghim/Featured chỉ đọc PASS. Chỉ cập nhật preview local; không deploy IIS, thay private config, SQL hoặc pipeline import.
