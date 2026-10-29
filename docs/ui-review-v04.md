# Review bốn trang public — V04-B

**PASS LOCAL kiểm tra giao diện, CHỜ DUYỆT THIẾT KẾ.** Ảnh là browser Edge thật với dữ liệu SQL Dev đã nhập từ qldd; không phải mockup hoặc ảnh AI. Web chỉ loopback5198; capture chỉ GET/HEAD, chặn mutation và off-origin. Admin2A/2B không được đổi CSS để đồng bộ màu.

CSS chung public navy/green/trắng, spacing/variables nhất quán. Homepage tin chính/tin phụ cạnh thông báo, category modules tiết chế, giữ điểm đến14 mục; rỗng ẩn, lỗi riêng với mã/log. Video block ẩn khi0tệp đủ điều kiện, route video metadata vẫn tồn tại. Footer/header lấy live config hiện có, không bịa nhận diện/liên hệ/chức vụ.

Danh sách chuyên mục dùng thumbnail/sapo/ngày thật, paging server. Bài đọc790px/chữ18px, toolbar đổi/reset cỡ chữ, copy canonical và print hoạt động. Ảnh giữ tỷ lệ, table vùng scroll riêng. Tra cứu có labels, sort liên quan/mới, người ký/hiệu lực xác nhận, filter URL, ưu tiên số hiệu exact, table scroll thay tràn cả trang. Chưa tìm toàn văn PDF hoặc fake viewcount/bình luận.

| Trang | URL local | Ảnh trước390/1440 | Ảnh sau390/1440 |
|---|---|---|---|
| Trang chủ | `http://127.0.0.1:5198/` | [390](../.local/evidence/v04/ui/before/home-390.png),[1440](../.local/evidence/v04/ui/before/home-1440.png) | [390](../.local/evidence/v04/ui/after/home-390.png),[1440](../.local/evidence/v04/ui/after/home-1440.png) |
| Chuyên mục | `/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat` | [390](../.local/evidence/v04/ui/before/category-390.png),[1440](../.local/evidence/v04/ui/before/category-1440.png) | [390](../.local/evidence/v04/ui/after/category-390.png),[1440](../.local/evidence/v04/ui/after/category-1440.png) |
| Bài nguồn thật | ID/title trong `ui/after/fixture.json`, URL `/bai-viet/{id}` | [390](../.local/evidence/v04/ui/before/article-390.png),[1440](../.local/evidence/v04/ui/before/article-1440.png) | [390](../.local/evidence/v04/ui/after/article-390.png),[1440](../.local/evidence/v04/ui/after/article-1440.png) |
| Tra cứu | `/van-ban` | [390](../.local/evidence/v04/ui/before/documents-390.png),[1440](../.local/evidence/v04/ui/before/documents-1440.png) | [390](../.local/evidence/v04/ui/after/documents-390.png),[1440](../.local/evidence/v04/ui/after/documents-1440.png) |

Mỗi stage20/20 page checks:4route×5width360/390/768/1024/1440; trước2actions, sau3actions; không image lỗi, tràn ngang toàn trang, control không nhãn, JS error, hotlink hoặc mutation. Sau có keyboard skiplink tới main, mobile menu14 đích200/Escape; fontsize/reset/copy clipboard thật, gọi window.print; canonical/OGtitle đối chiếu SQL. Print chỉ kiểm tra invocation, chưa nghiệm thu máy in/file PDF.

Empty category83 là dữ liệu rỗng thật200; không có ID/draft404; `/loi`500 là kiểm tra error view riêng, không giả là lỗi SQL chuyên mục do browser gây. Lỗi SQL trang chủ được log riêng trong regression thất bại. Ảnh `empty-category-390.png`, `notfound-390.png`, `error-390.png` cùng stage.

Đã xem ảnh trước home1440/article390 và ảnh sau home1440/documents390/article390; ảnh cũ có skiplink đang focus do probe chụp khi cuộn. Probe sau reset blur/instant scroll, không che focus khi người dùng Tab. Bài dài nguồn được giữ toàn văn; bảng tra cứu mobile cuộn trong vùng bảng, không yêu cầu toàn trang cuộn ngang. Bộ filter dài trên mobile vẫn cần PO cân nhắc bố cục; không tự gọi là thiết kế được duyệt.

CMS browser cuối40/40 có reauth thật giữ bản đang nhập và lưu SQL Test, typed14/14, accounts18/18, config34/34, scheduler10/10 là **các đơn vị riêng**. Ảnh/Test statements ở `.local/evidence/v04` và summary; không cộng chúng với83 nhóm runner.

Giới hạn: Edge viewport, chưa iOS/Safari/thiết bị vật lý, chưa WCAG2.2AA chứng nhận, chưa CWV thực địa/tải đại diện/UAT. Chưa có đích Dịch vụ công/Thư điện tử chính thức live(Q04); không tạo link# giả. Không có sitemap/structured-data mới để tuyên bố SEO hoàn tất.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -IsolatedBuild -NoBuild -Port 5198
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/capture-v04-public.ps1 -Stage after -IsolatedBuild
```

Capture chỉ chạy khi5198 trống, tự dừng process do nó tạo; không dừng web5188 có trước lượt. Để capture lại khi preview5198 đang mở, người vận hành dừng đúng preview của lượt này trước. Bước tiếp theo: PO xem ảnh/web và duyệt thiết kế, giải quyết Q04/Q12; chưa tự bắt đầu2C.
