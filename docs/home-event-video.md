# Banner sự kiện và VIDEO trang chủ

Ngày 10/11/2026: đã áp dụng trên Local `LandPortal_LanDemo` (17 → 18),
chưa truy cập SQL dùng chung hoặc triển khai IIS. Bản này cần migration cộng thêm
`20261110050000_HomeEventBanner` (Description, Enabled của SiteBanner), phải được
duyệt và áp dụng theo quy trình schema trước khi chạy binary mới. Không chạy
migration tự động lúc Web khởi động. Giá trị mặc định giữ banner cũ và hash
revision cũ; không sửa bài, media, menu, quyền hoặc tài khoản.

- CMS → Cấu hình website → Banner: tải ảnh qua kho media hiện có, chọn ảnh đã
  kiểm tra, chọn “Dải sự kiện sau Tin mới cập nhật”, nhập nhãn/tiêu đề, mô tả,
  liên kết, lịch bắt đầu/kết thúc, bật/tắt. Lưu nháp → Xem trước → Áp dụng.
  Thay ảnh bằng bộ chọn, xóa bằng nút Bỏ. Ảnh khuyến nghị 1170 × 100 hoặc cùng
  tỉ lệ 117:10. Khung căn giữa, rộng tối đa 1170px, cao tối đa 100px; mobile co
  theo cùng tỉ lệ. Ảnh dùng cover, crop ở giữa. CMS cảnh báo nếu phần ảnh bị
  cắt dự kiến vượt 10%; cảnh báo không chặn lưu và chỉ dành cho home_event_strip.
- Chưa có banner hợp lệ: trang công khai không chiếm chỗ; trang xem trước hiện
  vị trí hướng dẫn. Nhiều banner tự chuyển mỗi 5 giây, có dots và tạm dừng khi
  tương tác; một banner không autoplay/dots. Vị trí `home`
  tiếp tục là slider hoạt động, không tái sử dụng cho sự kiện.
- CMS → Trang chủ → khối Video: bật, đặt số lượng 3, ghim video đã công bố nếu
  cần. Không ghim thì dùng video mới nhất. Một khu vực VIDEO duy nhất cuối trang;
  khối Video bị tắt được tôn trọng. Không có khối cấu hình thì lấy tối đa 3 video.
- Chỉ video công khai có bản xử lý Ready, proof phù hợp, không cách ly, đủ file
  gốc/MP4/poster mới được chọn. Trang chủ kiểm tra metadata/kích thước, endpoint
  video hiện hành tiếp tục xác minh SHA/quyền trước phát. Không tải MP4 trên trang
  chủ; Play mở trang chi tiết với player hiện có. Không có video hợp lệ thì ẩn.

Kiểm tra: build Web PASS; 14 trường hợp Razor/Playwright cô lập tại
1920/1366/768/390, banner 0/1/3, video 0/1/3, preview trống và fallback PASS.
Kiểm tra migration/hash/clone/validation offline bằng `--home-event-video-offline`.
Local: `http://127.0.0.1:5298/`. Hai banner có nhãn LOCAL DEMO đã công bố qua
SiteConfigurationService; thêm/sửa/thay ảnh/xóa/bật-tắt, lưu nháp và công bố được
kiểm tra với quyền hiện hành và audit. Nháp không thay bản công khai. Đã bổ sung
mapping trình bày còn thiếu cho chuyên mục nguồn 60 bằng đúng tên/slug/cha hiện
có; không đổi Category hoặc menu. Chưa kiểm thử nút CMS bằng phiên đăng nhập
trình duyệt của người dùng; kiểm tra workflow nói trên là ở service CMS thật.

Video tổng hợp 4 giây đã tải bằng pipeline chunk, quét Defender thật, xử lý H.264
và công bố qua WorkflowService. Chromium phát vượt 1 giây, kích thước 640×360,
HTTP Range 206/1024 byte PASS. Đường dẫn:
`/bai-viet/97351b96-57a7-418f-ab40-76d27088eadd` (chỉ Local).
File gốc video vẫn cách ly theo thiết kế; chỉ MP4/poster đã Ready được phục vụ.

4 viewport Local 1920/1366/768/390 PASS, không tràn ngang, đúng vị trí banner,
slideshow và video. Bộ offline hiện có 10 kiểm tra PASS, gồm từ chối proof LAN
ngoài Development và từ chối video chưa quét. Không full regression.

Local demo dùng provider `MicrosoftDefenderLanMediaSample`, chỉ nhận tối đa 10
SHA đã khai báo của mẫu vô danh, engine pin và Windows SID hiện hành; hạn 32 MiB,
phải có cấu hình demo còn hạn, đúng SQL local, SMTP Disabled. Đây không phải
scanner cho upload công khai/IIS. Hai ảnh thử cũ bị cách ly do lỗi đối chiếu tên
provider đã giữ nguyên ScanFailed, không dùng hoặc giả Clean; hai ảnh thay thế
và video được quét thật thành công. Không chuyển các mẫu này sang IIS.

Backup Local COPY_ONLY/CHECKSUM, VERIFYONLY/HEADERONLY và hash PASS trước DDL;
đối soát toàn bộ dữ liệu cũ trước/sau migration PASS. Sau tạo mẫu, 288 content,
290 revision, 703 media gốc, 570 alias, 19 category và toàn bộ bảng Identity
giữ nguyên digest. Source thay đổi đã sẵn sàng rà soát Git; kế hoạch IIS bên dưới
chưa được thực thi.

## Một đợt triển khai IIS sau khi được duyệt

1. Chốt commit và tạo một gói bằng `powershell -NoProfile -File scripts/build-for-iis.ps1`
   với `.env` private của IIS; không đưa cấu hình/mẫu Local vào gói. Giữ nguyên
   SQL app login, application role, scanner và kho dữ liệu đích.
2. Kiểm kê đúng `WIN-S1IBJB84OOO/website_tin_tuc`, history đúng 17 migration,
   chưa có migration `20261110050000_HomeEventBanner`; đối soát baseline bài,
   media, tài khoản và menu. Nếu đã có migration thì reconcile, không chạy lại.
3. Dừng đúng app pool và 5 Worker trong cửa sổ bảo trì; giữ bản binary/cấu hình
   cũ để rollback. Tạo đúng một backup SQL COPY_ONLY/CHECKSUM trước ghi, kiểm tra
   VERIFYONLY/HEADERONLY và SHA thực tế, gắn receipt với target/schema/packet.
4. Chuẩn bị packet schema chỉ từ `20261102042625_DraftConsultationEmail` đến
   `20261110050000_HomeEventBanner`, dùng generator SET options đã sửa. Review
   hash/phạm vi trước Apply bằng DBA: chỉ thêm SiteBanner.Description và Enabled
   với default bảo toàn dữ liệu; không bootstrap, import, tạo lại role hoặc login.
5. Cập nhật Web và cả 5 Worker cùng gói; giữ nguyên private config, media và keys.
   Chạy preflight runtime, mở Web; không tự bật SMTP hoặc Worker chưa được duyệt.
6. Xác minh 18 migration, dữ liệu/menu/tài khoản cũ, health, trang chủ, CMS nháp →
   preview → publish banner, lịch/bật-tắt, poster/play/Range với video thật đã
   được phép công bố. Chưa có video hoặc scanner/processor hợp lệ thì ẩn VIDEO,
   báo điều kiện thiếu; không lấy mẫu Local hoặc giả Clean để nghiệm thu.
7. Nếu lỗi binary, dừng bản mới và phục hồi binary/cấu hình cũ; hai cột cộng thêm
   tương thích binary cũ. Không tự chạy Down/DROP/RESTORE, không xóa dữ liệu;
   sự cố schema phải reconcile trước phương án phục hồi riêng.

Chưa tạo packet Apply IIS hoặc backup đích trong lượt Local này. Kế hoạch này
không phải receipt nghiệm thu hay lệnh tự động triển khai Production.
