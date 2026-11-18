# Nhập 43 lịch tuần trực tiếp trên IIS

**Phương án cũ, giữ để tương thích.** Yêu cầu hiện hành dùng [WEB IMPORT trên máy Developer](weekly-schedule-web.md), không chuyển công cụ hay chạy lệnh trên IIS.

Chỉ chuyển **toàn bộ thư mục CLI đã publish** và `website_upload.zip` vào một thư mục riêng, ví dụ `D:\GDLA-WeeklyImport`. Không đặt trong `web`, `.runtime`, `wwwroot`, kho media hoặc virtual directory nào. Không copy/chỉnh `.env`, `uat.json`, manifest; không tạo `runtime.json` hoặc bản sao secret. Không cần deploy lại website.

Developer publish công cụ (không build/deploy Web):

```powershell
dotnet publish tools/WeeklyScheduleImport/WeeklyScheduleImport.csproj -c Release -o .local/tools/weekly-import-iis
```

Máy IIS cần .NET 10 runtime hiện có và tài khoản Windows vận hành đã có quyền đọc metadata IIS, gói WebBundle; đọc/tạo tệp và thư mục con trong media/temp. Công cụ không cấp quyền. Tài khoản CMS hiện hành cần quyền soạn/sửa bài, quản lý media và phạm vi chuyên mục 76; không khóa, không buộc đổi mật khẩu hoặc yêu cầu MFA chưa được CLI hỗ trợ. Không dùng tài khoản giả/DBA để thay người biên tập.

## Các lệnh trên IIS

Mở PowerShell bằng tài khoản vận hành có sẵn, tại thư mục CLI. Đường dẫn dưới đây là IIS Physical Path đã dùng cho GDLA; nếu site đã chuyển, dùng đúng Physical Path hiện tại. Công cụ tự xác minh bằng metadata IIS, không đoán kho tệp.

```powershell
Set-Location D:\GDLA-WeeklyImport
$web = 'D:\WWW_GDLA\publish\TTDL-IIS\web'

# 1. Chỉ đọc: không tạo file, không ghi SQL, không hỏi mật khẩu CMS.
.\NHAP-LICH.ps1 -Action Preflight -IisWeb $web

# 2. Nhập tên tài khoản CMS, mật khẩu được CLI hỏi riêng và không hiển thị.
$cms = Read-Host 'Tên đăng nhập CMS được phép nhập lịch tuần'
.\NHAP-LICH.ps1 -Action DryRun -IisWeb $web -User $cms

# Đọc ket-qua\inventory.json và ket-qua\plan.json; rà soát STT 39–42.
# Chỉ thực hiện bước 3 khi được cấp quyền nhập và có điểm khôi phục SQL/storage theo quy trình hiện hành.
# Thay giá trị bên dưới bằng Plan SHA256 thật do DryRun in ra, không tạo mã phê duyệt.
$plan = Read-Host 'Plan SHA256 đã đối chiếu'
.\NHAP-LICH.ps1 -Action Import -IisWeb $web -User $cms -PlanSha256 $plan

# 4. Đối soát chỉ đọc SQL, phục hồi mapping nếu Import bị gián đoạn.
.\NHAP-LICH.ps1 -Action Reconcile -IisWeb $web -User $cms

# 5. Chỉ sau khi cán bộ gửi duyệt/duyệt/xuất bản qua CMS:
.\NHAP-LICH.ps1 -Action SyncLinks -IisWeb $web -User $cms
```

Chạy từng bước và đọc kết quả; không chạy nguyên khối để tự nhập/công bố. Dừng khi có `BLOCKED`. `DryRun` cần thư mục `ket-qua` chưa tồn tại; muốn lập kế hoạch mới dùng `-OutputDirectory` khác, không xóa kế hoạch cũ. Các lệnh sau dùng cùng thư mục đó. Import gián đoạn: chạy Reconcile trước, xem các trạng thái rồi tiếp tục cùng plan nếu các guard cho phép; không tự retry mù.

## Công cụ kiểm tra gì

- `--iis-web` đọc **đúng `.runtime/private/uat.json` và manifest của web hiện hành**. Dùng `PortablePackageLayout` để kiểm tra hash private, Web/Worker binary và web.config. Giữ khóa đọc các file này đến khi lệnh kết thúc; tránh cập nhật website đồng thời lúc nhập.
- Kiểm tra Staging/UAT từ gói đã bind; không cho biến môi trường máy hoặc cấu hình cũ ghi đè. HTTPS phải khớp `gdla.gov.vn` của duy nhất site đang chạy với Physical Path này. BaseUrl trống được suy ra từ HTTPS binding, không lấy request header.
- Dùng cùng cơ chế chuẩn hóa storage với Web. Với cấu hình portable mặc định: media là `D:\WWW_GDLA\publish\TTDL-IIS\private\media`; các đường dẫn đã cấu hình khác được giữ nguyên nếu guard hợp lệ. Không copy media, không tạo kho thay thế khi kho hiện hành thiếu hoặc không đọc được.
- Xác minh private/storage ngoài mọi IIS webroot, không junction/symlink hoặc ánh xạ `.runtime` riêng. Không sửa ACL, không chặn chỉ vì ACL có quyền rộng; kiểm tra quyền truy cập thực tế bằng handle thư mục `OPEN_EXISTING`, không tạo tệp thăm dò. Quyền của tệp con vẫn được kiểm tra khi đọc/ghi thực tế.
- Sau các kiểm tra file/IIS mới mở SQL qua login/secret và Application Role **hiện có**. Kiểm tra physical server `WIN-S1IBJB84OOO`, database `website_tin_tuc`, login `website_tin_tuc_app`, role `LandPortalRuntime`, 70 bảng và chính xác 18 migrations. Giữ TLS/Runtime18; không migration, backup SQL hoặc cấp quyền.
- Preflight không phải chứng nhận đã import: chỉ xác minh cấu hình/đường dẫn/quyền truy cập và SQL chỉ đọc. Quyền CMS được kiểm tra khi DryRun/Import; scanner/policy PDF vẫn áp dụng khi nhập. Nếu no-AV chưa được bật hợp lệ thì không tự bật, không giả Clean.

## Kết quả và giới hạn

Import tạo nháp StaticPage chuyên mục 76, attachment PDF nguyên byte/tên/SHA, receipt từng STT. Không tạo WeeklyScheduleRelease hay công bố. Giữ các quy tắc chống trùng/phục hồi của công cụ gốc. STT 39–42 có khoảng ngày Excel/PDF lệch, chỉ báo để cán bộ rà soát; giữ nguyên tiêu đề.

`ket-qua\LichCT-links-*.xlsx` là **bản sao workbook gốc**, giữ 17 sheet, ngày/kiểu/định dạng cột B và tiêu đề C. Sync chỉ ghi D sau khi kiểm tra published revision và HTTP bài/PDF; E48 sửa 2 → 3, các số trang đúng không sửa. Có thể chạy lại sau mỗi đợt xuất bản; đây không phải tác vụ tự chạy trên IIS.

Lỗi riêng: `BUNDLE_READ` (thiếu file/quyền đọc), `BUNDLE_HASH` (gói không đồng nhất), `IIS_METADATA` (không đọc được IIS), `IIS_SITE`/`IIS_HTTPS` (sai site/binding), `STORAGE_ACCESS` (đường dẫn và mã truy cập Windows cụ thể), `SQL_ACCESS`/`SQL_IDENTITY` (kết nối/quyền/danh tính không đạt). Dừng và dùng đúng tài khoản/gói hiện có; không sửa JSON hoặc manifest để vượt guard.

Kiểm thử bổ sung ngày 17/11/2026: Release build PASS, 0 lỗi/cảnh báo; **34 kiểm tra tương thích PASS** dùng filesystem Windows và metadata IIS cô lập. Có kiểm tra quyền truy cập Windows thật, private hash/binary bị sửa, sai site/binding/storage, giữ cấu hình trong bộ nhớ, khóa đọc và CLI dừng trước SQL/output khi hash sai. Đối soát danh tính SQL dùng dữ kiện giả lập để kiểm tra guard, không phải kết quả kết nối SQL đích. Không kết nối SQL vận hành hay kiểm thử trực tiếp trên IIS thật. Các kiểm tra SQL/HTTP nhập 43 hồ sơ trước đó vẫn giữ nguyên, không chạy lại full suite vì chỉ bổ sung bộ đọc WebBundle.

Chạy lại riêng bộ tương thích nếu cần:

```powershell
powershell -NoProfile -File scripts/test-weekly-bundle.ps1 -SourceZip C:\website_upload\website_upload.zip
```
