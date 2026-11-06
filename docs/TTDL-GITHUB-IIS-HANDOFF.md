# Bàn giao GitHub → IIS UAT TTDL

## A. Lấy source

```powershell
git clone https://github.com/akayuhuhu0503/websitetintuc.git
cd websitetintuc
# Nếu đã có clone, kiểm tra không có thay đổi cục bộ cần giữ rồi:
git pull --ff-only origin main
git rev-parse HEAD
git ls-remote origin refs/heads/main
```

Đối chiếu SHA với Developer khi nhận bàn giao. Không copy code hoặc binary schema15 từ LAN sang IIS. Source chuẩn có FR-06, 17 migration, Roboto/Merriweather tự host và tra cứu Giấy hủy.

## B. Nhận cấu hình private

Developer bàn giao riêng **một `.env` hoàn chỉnh**, dùng credential hiện hành, qua kênh an toàn; không yêu cầu người triển khai tạo lại secret/login/role. Mẫu tham chiếu: `scripts/ttdl.env.example`. Giữ bản gốc ngoài repository/webroot; nếu dùng lệnh mặc định, chỉ đặt bản private vào `.env` gốc clone (đã Git-ignore), hạn chế ACL và không đưa vào gói code/Git.

File phải có đủ năm khóa, không gửi giá trị qua chat/log:

- `ConnectionStrings__Portal`: `45.120.229.43,1433` / `website_tin_tuc` / `website_tin_tuc_app`.
- `Runtime__RolePassword`: secret hiện hành của `LandPortalRuntime`.
- `ConnectionStrings__GiayHuyReadOnly`: cùng endpoint / `QuanLyDuAnTrungTamDuLieuDatDai` / `landportal_giayhuy_reader`.
- `GiayHuy__PublicationScope=OfficialConfirmedEightFields-OwnerApproved-20261104`.
- `GiayHuy__LanUatTlsException=OwnerApproved-20261104`.

Profile **UAT**, môi trường **Staging**, `Encrypt=True`; ngoại lệ `TrustServerCertificate=True` chỉ theo đúng phê duyệt UAT, không chuyển sang Production. Không dùng sa. Physical SQL dự kiến `WIN-S1IBJB84OOO`; schema website đã70 bảng/17 migration, role/login/View đã có — **không Apply lại**. Reader nguồn chỉ SELECT `dbo.vw_GiayChungNhanHuyCongKhai`.

TTDL điền tên máy/site (`IIS_MACHINE_NAME`, `IIS_SITE_NAME`) và các reference đã được duyệt trong **cùng file .env**: `Deployment__TargetApprovalReference`, `Scanner__PolicyReference`, `Scanner__DefenderPrivacyPolicyReference`; chỉ đổi `Deployment__TargetApproved=true` khi đã được duyệt thật. Không dùng reference tự tạo/PENDING. Site/pool, HTTPS, service account và Defender phải đạt guard. Đường dẫn private/storage, JSON và metadata nội bộ do công cụ tự xử lý; không tạo file trung gian thủ công. SMTP vẫn Disabled.

## C. Build một lệnh

Cần .NET SDK đúng `global.json`, Node.js/npm và Hosting Bundle ASP.NET Core10 trên IIS đích. Từ gốc clone:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Output `.local/publish/TTDL-IIS/`: `web/`, `worker/` (5 chế độ), tools, ops và manifest hash. **IIS Physical Path chỉ trỏ `web/`.** Chế độ mặc định Auto: nếu build trên đúng máy IIS đã có site/pool dừng và đủ điều kiện, công cụ tự tạo private home ngoài webroot, đặt ACL và gắn JSON vào Web. Không khởi động IIS.

Nếu build trên máy khác: copy nguyên thư mục Publish lên máy IIS, cấu hình site trỏ `web/`, dừng site/pool, rồi mở **`CAU-HINH-IIS.cmd` bằng tài khoản vận hành được cấp quyền và chọn cùng file `.env`**. Công cụ tự tạo `uat.json`, chuyển đúng phê duyệt từ .env và dùng lại pipeline bảo mật hiện có. Không cần biết tên/đường dẫn các file nội bộ hoặc chuyển JSON từ máy build. Giữ .env ngoài webroot và bàn giao qua kênh an toàn.

Đã có gói cũ thì giữ nguyên, không dùng `-Replace` cho bản đã dùng/sealed. Khi cần build một bản riêng:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1 -EnvFile '<private.env>' -OutputName ("TTDL-IIS-"+(Get-Date -Format yyyyMMdd-HHmmss))
```

Build không kết nối SQL, không migration, không khởi động Web/Worker. Auto chỉ chuẩn bị file/ACL trên đúng IIS đã được duyệt, không tạo site, tài khoản hay sửa policy Defender. Gói luôn là schema17 chuẩn; không bật chế độ LANschema15. Font, license, JS/CSS và manifest được kiểm tra khi đóng gói.

## D. Trước khi chạy IIS

Làm theo [iis-one-command.md](iis-one-command.md) để chạy Preflight bằng tài khoản dịch vụ sau khi công cụ đã cấu hình. Kiểm tra app login kích hoạt LandPortalRuntime, physical server/database/schema17, và reader Giấy hủy chỉ SELECT View; tra cứu thử qua Web sau khi được phép khởi động UAT. Preflight chưa PASS thì không gọi UAT sẵn sàng vận hành.

Nếu bỏ qua cấu hình hoặc thiếu điều kiện, website UAT giữ đóng và hiển thị HTTP503 tiếng Việt (`IIS-CONFIG-001/002/003`); chạy nút cấu hình để nhận đúng điều kiện còn thiếu. Đây không phải cách bật ứng dụng khi guard chưa đạt. Lỗi Hosting Bundle/ANCM xảy ra trước mã ứng dụng vẫn cần log IIS/Windows; không kết luận nguyên nhân mọi lỗi500.30 chỉ từ mã lỗi.

Không ghi đè hệ thống IIS đang có, không import/bootstrap dữ liệu, không tự chạy Worker, không gửi email thật hoặc đổi DNS. SQL/schema/role đã hoàn tất không phải bước cài lại. Publish PASS không thay nghiệm thu hạ tầng, scanner, nội dung hay UAT/Production. Dữ liệu/media vận hành được bàn giao và phê duyệt riêng, không nằm trong GitHub.
