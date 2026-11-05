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

TTDL xác nhận site/pool, HTTPS thật, service account ít quyền, storage và scanner trên máy đích. Config JSON được tạo ngoài source/webroot; Web và5 Worker cùng tham chiếu private JSON đó. SMTP vẫn Disabled.

## C. Build một lệnh

Cần .NET SDK đúng `global.json`, Node.js/npm và Hosting Bundle ASP.NET Core10 trên IIS đích. Từ gốc clone:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Output `.local/publish/TTDL-IIS/`: `web/`, `worker/` (5 chế độ), tools, ops và manifest hash. **IIS Physical Path chỉ trỏ `web/`.** Private `uat.json` nằm ở đường dẫn `PRIVATE CONFIG` mà lệnh trả về, không ở trong gói publish. Nếu build khác máy IIS, chuyển JSON riêng bằng kênh an toàn và đặt ACL đúng service identity.

Đã có gói cũ thì giữ nguyên, không dùng `-Replace` cho bản đã dùng/sealed. Khi cần build một bản riêng:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1 -EnvFile '<private.env>' -OutputName TTDL-IIS-<RUN-ID>
```

Build không kết nối SQL, không migration, không khởi động Web/Worker. Gói luôn là schema17 chuẩn; không bật chế độ LANschema15. Font, license, JS/CSS và manifest được kiểm tra khi đóng gói.

## D. Trước khi chạy IIS

Làm theo [iis-one-command.md](iis-one-command.md) để discovery/cấu hình **đúng site đã được cấp** (site/pool dừng), bảo vệ private JSON, xác minh HTTPS/scanner/service account, rồi chạy Preflight bằng tài khoản dịch vụ. Kiểm tra app login kích hoạt LandPortalRuntime, physical server/database/schema17, và reader Giấy hủy chỉ SELECT View; tra cứu thử qua Web sau khi được phép khởi động UAT. Preflight chưa PASS thì không gọi UAT sẵn sàng vận hành.

Không ghi đè hệ thống IIS đang có, không import/bootstrap dữ liệu, không tự chạy Worker, không gửi email thật hoặc đổi DNS. SQL/schema/role đã hoàn tất không phải bước cài lại. Publish PASS không thay nghiệm thu hạ tầng, scanner, nội dung hay UAT/Production. Dữ liệu/media vận hành được bàn giao và phê duyệt riêng, không nằm trong GitHub.
