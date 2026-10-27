# Publish IIS cho TTDL — SRS v1.2

Tại gốc checkout đã kiểm SHA, chạy một lệnh:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-for-iis.ps1
```

Cần Windows PowerShell 5.1, SDK đúng `global.json` (hiện 10.0.112), Node.js/npm và mạng để restore NuGet/npm khi cache chưa đủ. RTK là tùy chọn cho lệnh này. Không cần `.env` hoặc SQL Server khi publish. Không chạy Web, Worker, schema, migration hay import trong lúc build.

Output cố định `.local/publish/TTDL-IIS/`:

- `web/`: Web Release, frontend, thư viện/licence và `web.config`; đây là thư mục copy vào IIS.
- `worker/`: một binary Worker Release cho **5 chế độ** Scheduler, Documents, InquiryScan, Video, Mail.
- `tools/MigrationCli.dll`, `tools/uat-data/UatDataHandoff.dll`: chỉ chạy sau khi có target và phê duyệt phù hợp.
- `ops/`: runtime, cấu hình mẫu và công cụ vận hành; `manifest.json`: kiểm kê hash output.

Không ZIP, PDB, secret, media hay dữ liệu SQL. Nếu output đã tồn tại, mặc định dừng. Chỉ dùng `-Replace` cho output trung gian chưa sử dụng/chưa chốt; bản cũ được giữ nguyên tại `TTDL-IIS.previous`, gồm manifest. Nếu đã có bản previous, script dừng thay vì xóa. Khi bàn giao được chốt, đặt `sealed=true` trong `build-only.json` để ngăn thay thế. Không dùng output này làm trực tiếp thư mục chạy IIS nếu còn định build lại. Lỗi build giữ `TTDL-IIS-building` và log để đối soát, không tự xóa hoặc retry.

## Cài IIS sau khi được cấp môi trường

1. Cán bộ CNTT cài IIS và **ASP.NET Core 10 Hosting Bundle x64**. Tạo application pool riêng, **No Managed Code**, Integrated, x64; copy `web/` đến thư mục code và đặt Physical Path tại đó. Cấp identity Web quyền đọc/thực thi code, không cho ghi webroot.
2. Cấp hostname/certificate HTTPS thật, binding HTTPS và private NTFS home **ngoài webroot**, ví dụ đường dẫn do TTDL quyết định. Cấp read config cho Web/Worker, quyền ghi tối thiểu từng kho media/snapshot/temp/keys theo tác vụ; không cho Users/Everyone đọc config. Không đặt `.env` vào IIS.
3. Chỉ sau khi có SQL/database riêng và phê duyệt target: chuẩn bị `.env` **của môi trường đích**, với các khóa thật `ConnectionStrings__Portal`, `Sql__ExpectedDatabase`, `Deployment__Profile=UAT`, `Deployment__TargetApproved=true`, `Deployment__TargetApprovalReference`, `Deployment__ExpectedSqlServer`, `Deployment__ExpectedHost`, `Runtime__RolePassword`, `Storage__MediaRoot/SnapshotRoot/TempRoot/KeysRoot` và các mục scanner/SMTP. Tham chiếu `ops/uat-config.example.json`. Không dùng lại `.env` Dev. SQL target vẫn phải khớp exact database/server, Windows authentication và TLS theo guard hiện có.
4. Có thể nhập thẳng JSON theo template, hoặc chuyển file `.env` private bằng lệnh offline sau (không kết nối SQL):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File <PACKAGE>\ops\prepare-iis-config.ps1 -EnvFile <PRIVATE_ENV> -DataHome <PRIVATE_HOME> -PublishRoot <PACKAGE>
```

Converter không thực thi `.env`, không nội suy biến, không ghi đè config, không in secret. Tạo `<PRIVATE_HOME>\uat.json` và `iis.web.config` chỉ operator/SYSTEM đọc được ban đầu. Cấp thêm quyền đọc cho identity Web/Worker đã được duyệt. Sau review, copy `iis.web.config` vào thư mục website làm `web.config`. Hai biến **LANDPORTAL_HOME** và **LANDPORTAL_CONFIG** trỏ tới private home và JSON đó; `DOTNET_ENVIRONMENT`/`ASPNETCORE_ENVIRONMENT` là **Staging**. Web/Worker cùng dùng JSON này. Web.config chưa chuẩn bị chứa placeholder và **dừng trước SQL**, không fallback sang Dev. Không khởi động site khi chưa hoàn thành SQL, runtime role và cấu hình được duyệt.

5. Chạy schema/provision role/chuyển dữ liệu **ở bước sau**, theo runbook target/backup/signing/scanner hiện hành và phê duyệt riêng. Build-only package không có schema đã ràng buộc đích: `prepare-uat-target.ps1`/`install-uat-iis.ps1` yêu cầu packet schema đầy đủ, không chạy chúng trực tiếp trên gói build-only. Xem `ops/uat-data-runbook.md`; không tự import 314 bài, copy Dev/Test hay Identity. FFmpeg/ffprobe, scanner, SMTP và policy phải được cấp riêng, không có binary hoặc credential trong package.
6. Sau khi SQL/config/HTTPS sẵn sàng, start IIS và cấu hình tiến trình giám sát cho 5 Worker bằng `ops/uat-runtime.ps1`. Ví dụ dưới chạy foreground; đăng ký task/service chỉ khi TTDL phê duyệt:

```powershell
powershell -NoProfile -File <PACKAGE>\ops\uat-runtime.ps1 -DataHome <PRIVATE_HOME> -Config <PRIVATE_HOME>\uat.json -PublishRoot <PACKAGE> -Mode Scheduler
# Mở 4 tiến trình riêng tương tự: -Mode Documents / InquiryScan / Video / Mail
```

Chỉ sau đó smoke HTTPS, admin, đọc bài, quyền media và worker; SMTP mặc định Disabled. Gói publish PASS không phải kết quả chạy IIS/SQL/UAT hay PRODUCTION READY. Cần TTDL cấp máy chủ, SQL/database/identity, HTTPS, kho/ACL, scanner, FFmpeg, SMTP và nghiệm thu ATTT/UAT trước vận hành.
