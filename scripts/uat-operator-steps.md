> Chuẩn chính thức hiện hành: **SRS v1.2 ngày 23/10/2026**, thay v1.1. Máy chủ TTDL; đội phát triển bàn giao nguồn Git, Trưởng phòng build và cài đặt. Không đưa tài khoản giả lập Dev/Test vào Staging/Production.

## Build từ nguồn Git theo RB-01

Cài SDK đúng `global.json` (.NET 10.0.112), Node.js/npm và RTK CLI được các script dự án sử dụng. Pull đúng commit bàn giao, tại gốc source:

Mã nguồn đã có trên `https://github.com/akayuhuhu0503/websitetintuc`, nhánh `main`.
Đối chiếu SHA bàn giao trước build; `git pull --ff-only` chỉ tại checkout sạch,
không reset/stash hoặc tự gộp thay đổi của đơn vị. Bản sửa local chưa push không
được coi là đã có trên GitHub. Không đưa cấu hình đích hoặc dữ liệu vào checkout.

Máy build cần **SDK 10.0.112**; máy IIS cần **.NET 10 Hosting Bundle x64** gồm
.NET Runtime, ASP.NET Core Runtime và ASP.NET Core Module V2 (ANCM). SDK không
thay thế Hosting Bundle. TTDL chọn bản vá 10.0.x đã được phê duyệt từ nguồn
Microsoft, ghi version/SHA/giấy phép trong hồ sơ cài đặt. Cài IIS trước Hosting
Bundle; nếu đã cài bundle trước IIS thì operator repair bundle theo Microsoft.
Kiểm `dotnet --list-sdks`, `dotnet --list-runtimes` có cả Microsoft.NETCore.App
và Microsoft.AspNetCore.App 10.0.x, IIS có AspNetCoreModuleV2; installer không tự
cài các prerequisite hoặc khởi động lại IIS toàn máy. [Hướng dẫn IIS .NET10 của
Microsoft](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0).

```powershell
rtk proxy npm --prefix src/Web ci
rtk proxy npm --prefix src/Web run build
rtk proxy powershell -NoProfile -File scripts/package-uat.ps1 -RunId uat-YYYYMMDD-UNIQUE
```

Thay nhãn bằng thời điểm/ID riêng; script từ chối ghi đè. Publish/sinh schema offline không cần `.env` thật, SQL đang chạy hoặc mật khẩu. Gói nằm `.local/publish/<RunId>`. Schema mẫu có chặn UNALLOCATED; chỉ dùng artifact đã ràng buộc target được cấp qua `prepare-uat-target.ps1`. Không chạy lệnh bootstrap giả lập trên UAT; tài khoản cán bộ theo quy chế được phê duyệt. Không sao chép Identity Dev/Test.

Tra cứu hỏi đáp v1.2 dùng mã ngẫu nhiên128bit + email + CAPTCHA một lần, không OTP; chỉ trả trạng thái/câu trả lời. SQL giới hạn theo mã/phiên và CAPTCHA theo IP; CSRF/no-store; nội dung gốc, tệp và PII không được trả qua endpoint này. Thực hiện đánh giá chống dò/ATTT trên hạ tầng UAT trước mở public.

---

> Cập nhật BA/SRS v1.1: máy chủ vận hành chính thức thuộc TTDL, không phải máy chủ của Bộ. Giữ IIS + SQL Server + Web/Worker; hạ tầng đích chưa được cấp. Video mới tải trực tiếp lên hệ thống, không nhúng YouTube.

# Gói UAT — chưa triển khai, chưa nghiệm thu hạ tầng

## Bàn giao nhanh cho đơn vị CNTT

**Chưa được cấp hạ tầng. `LandPortal_Uat` chỉ là tên đề xuất, không phải database
đã được tạo hoặc được phép sử dụng.** Điền `ops/uat-target.example.json` bằng
thông tin đã được đơn vị phê duyệt: máy IIS, SQL instance vật lý/database đã cấp,
hostname HTTPS/certificate có sẵn, thư mục code/private, service identity, scanner
và relay SMTP. Ghi `ApprovalReference` là văn bản/quyết định thật, không dùng fixture.
Không gửi password/private key qua tài liệu hoặc chat.

Trên thư mục gói đã giải nén tại target, sau khi được cấp các thông tin này:

```powershell
powershell -NoProfile -File ops/prepare-uat-target.ps1 -TargetFile D:\Approved\uat-target.json -PublishRoot D:\LandPortal-Uat-Code -OutputDirectory D:\Approved\uat-packet-UNIQUE
```

Chỉ chuẩn bị JSON private (secret rỗng), web.config, schema SQLCMD ràng buộc đúng
server/database và manifest riêng; không tạo DB/IIS/service. Operator đặt bản JSON
private tại `<DataHome>\uat.json`, điền secret từ secret store và cấp ACL tối thiểu.
Không đặt packet/config/secrets dưới webroot. `TargetApproved=false` mặc định; runtime
không khởi động với tên placeholder hay thiếu tham chiếu phê duyệt đích.

DBA tạo database **đúng tên đã cấp**, review và chạy `schema-bound.sqlcmd` bằng
`sqlcmd -S <APPROVED_INSTANCE> -d <ALLOCATED_DB> -E -N -b -i <PRIVATE_PACKET>\schema-bound.sqlcmd`.
Phải dùng SQLCMD và xác minh TLS; không thêm `-C`. `:ON ERROR EXIT` và kiểm tra
`SERVERPROPERTY('ServerName')`/`DB_NAME()` chặn sai đích. File `ops/schema-idempotent.sql`
nguyên gói là **template không được chạy**, luôn dừng khi chưa bind đích.
Không chạy migration tự động lúc web start; không copy SQL Test/Identity/fixture.

Sau provision schema/application role/NTFS, chạy dưới identity dự kiến:

```powershell
powershell -NoProfile -File ops/uat-runtime.ps1 -DataHome D:\LandPortal-Uat-Private -Config D:\LandPortal-Uat-Private\uat.json -PublishRoot D:\LandPortal-Uat-Code -Mode Preflight -Report D:\LandPortal-Uat-Private\reports\config-UNIQUE.json
powershell -NoProfile -File ops/uat-runtime.ps1 -DataHome D:\LandPortal-Uat-Private -Config D:\LandPortal-Uat-Private\uat.json -PublishRoot D:\LandPortal-Uat-Code -Mode Preflight -Online -Report D:\LandPortal-Uat-Private\reports\sql-UNIQUE.json
powershell -NoProfile -File ops/install-uat-iis.ps1 -PreparedTarget D:\Approved\uat-packet-UNIQUE
```

Hai preflight chỉ đọc; lệnh thứ hai mới kết nối SQL và kiểm đúng physical instance,
database, schema, compatibility và kích hoạt runtime role. Không ghi secret vào report.
Installer mặc định **PLAN ONLY**. Chỉ trên máy đã duyệt, operator gọi
`install-uat-iis.ps1 -PreparedTarget <packet> -Apply -WebCredential (Get-Credential)`
và xác nhận Windows PowerShell. Script kiểm hash/certificate/SQL, backup cấu hình
IIS, tạo site/pool riêng, bind HTTPS có sẵn; không cài Windows features, cấp quyền
hay tạo DB. Nếu lỗi sau ghi, giữ audit `PARTIAL_RECONCILE_REQUIRED`, không chạy lại
mù hoặc xóa site/pool. Thư mục web.config sau bind có hash riêng trong audit,
không còn trùng template gốc; giữ gói gốc làm mốc kiểm chứng.

**Thứ tự ngắn nhất:** cấp target → DBA/schema/role + private ACL → preflight →
IIS/HTTPS → khởi động foreground/health 5 worker → import XML task theo phê duyệt →
Xác thực mật khẩu/CAPTCHA + scanner Clean/EICAR/failure + SMTP được duyệt + restart/restore + UAT-16.
Chưa có target nên các thao tác cài và nghiệm thu thật này đều **NOT RUN**.

## Chuẩn bị host worker với ít thao tác

Sau khi operator cấp đúng service identity, private config và thư mục code:

```powershell
powershell -NoProfile -File ops/export-uat-worker-tasks.ps1 `
  -DataHome D:\LandPortal-Uat-Private `
  -Config D:\LandPortal-Uat-Private\uat.json `
  -PublishRoot D:\LandPortal-Uat-Code `
  -ServiceAccount 'DOMAIN\svc_landportal' `
  -OutputDirectory D:\LandPortal-Uat-Tasks-UNIQUE
```

Lệnh chỉ xuất 5 XML task và hash, không cài task/service hay cấp quyền. Operator
review rồi import từng XML trong Task Scheduler dưới identity đã được phê duyệt;
nhập credential qua giao diện bảo mật của Windows, không đưa vào XML/command line.
Account cần quyền đăng nhập batch và quyền SQL/NTFS do đơn vị vận hành cấp.
Không dùng Administrator/SYSTEM hoặc dùng certificate Dev cho target. Nếu chính sách
PowerShell đích yêu cầu chữ ký, operator ký script bằng signer đích được duyệt;
không đổi ExecutionPolicy để né policy.

Task chạy sau boot 2 phút, không giới hạn thời gian chạy, IgnoreNew khi trùng và
restart tối đa 3 lần cách 1 phút khi process lỗi. Sau ba lần lỗi phải cảnh báo và
điều tra config/SQL/scanner; không retry import tự động. Durable SQL leases/receipts
vẫn quyết định tính an toàn khi restart. Chạy thử kill/restart/host reboot tại UAT
và xem Task Scheduler Operational log/exit code cùng trạng thái SQL queue trước
nghiệm thu; XML kiểm tra local không chứng minh worker đích đã tự phục hồi.

Theo dõi health qua launcher `-Health` cho Documents/InquiryScan/Video. Scheduler
và Mail theo SQL lease/outbox, task status và audit; không ghi mật khẩu, CAPTCHA,
OTP hoặc nội dung/email người dân vào log. Gửi thử email thật chỉ sau duyệt
riêng của đơn vị. Dùng `ops/UAT-16.md` để ghi đúng 16 case đã có; không lấy kết quả
local thay chữ ký nghiệm thu cán bộ.

## Trình tự provision và nghiệm thu

Yêu cầu: Windows/IIS + ASP.NET Core 10 Hosting Bundle (x64), SQL Server
compatibility 160, chứng chỉ HTTPS và SQL TLS hợp lệ; website/app pool riêng.
Operator xác nhận server/host/database đã cấp trước mọi thao tác ghi. Không dùng
LandPortal_Dev/Test; không copy toàn SQL Test có fixture. Không mang .env máy Dev.

1. Kiểm manifest SHA của gói; đặt web/worker/tools ở thư mục chỉ đọc của code.
   Copy ops/uat-config.example.json vào **kho riêng ngoài code** dưới tên uat.json.
   Điền đường dẫn thực, exact SQL/HTTPS host, runtime role secret >=24 ký tự.
   Chỉ operator và service account được đọc config/keys. Không ghi secret vào log.
   Web/Worker cần quyền ghi media/temp/keys theo phạm vi, không quyền sửa code.
2. DBA tạo database UAT đã cấp với backup trước, chạy schema-bound.sqlcmd sau review.
   Script chỉ có schema, không có dữ liệu nguồn/tài khoản. Không migrate khi web start.
   Provision APPLICATION ROLE LandPortalRuntime bằng secret riêng từ secret store:
   GRANT SELECT,INSERT,UPDATE,DELETE ON SCHEMA::dbo; DENY ALTER;
   DENY UPDATE,DELETE cho AuditLogs,InteractionPublicSnapshots,InquiryAssignments,
   InquiryCalendars,InquiryCalendarExceptions,InquiryExtensions,OutboxReconciliations.
   Windows login Web/Worker chỉ CONNECT + quyền kích hoạt approle, không db_owner.
   Với commit31cbb8b7, DBA phải kiểm __EFMigrationsHistory cuối =
   `20261023074121_Srs12MenuAndInquiryLookup`. Với bản bàn giao sau, đối chiếu
   schema trong chính gói đã duyệt; không hạ về migration1.1 hoặc sửa history.
   Sau khi DBA đã backup/provision schema + runtime role, operator đặt LANDPORTAL_HOME,
   LANDPORTAL_CONFIG và cả hai biến môi trường Staging như bước bootstrap bên dưới.
   Chạy `dotnet tools/MigrationCli.dll site-initialize --confirm-target <DATABASE_DA_CAP>
   --approval-reference <DUNG_REFERENCE_TRONG_CONFIG>` trên đúng target đã duyệt.
   CLI đọc lại physical server/database/schema trước ghi; tạo 18 danh mục nguồn tham
   chiếu (gồm đủ14 chuyên mục) và cấu hình cổng ban đầu trong cùng transaction.
   Không tạo tin, fixture, tài khoản/quyền; cấu hình đã có thì giữ nguyên. Không dùng
   lệnh migrate/site-initialize Development để thay cho kiểm chứng target UAT.
3. Chưa có quyền cấp tài khoản UAT trong lượt Dev này. Người được phân công bootstrap
   admin riêng bằng công cụ đã có trên target (LANDPORTAL_HOME/CONFIG, Staging;
   dotnet tools/MigrationCli.dll bootstrap). Trong uat.json private, dùng hai khóa
   Bootstrap.AdminEmail và Bootstrap.AdminPassword (mật khẩu riêng theo policy,
   tối thiểu 7 ký tự, không bắt buộc độ phức tạp); không đưa giá trị vào command line/log. Đặt process env
   LANDPORTAL_HOME=<PRIVATE_HOME>, LANDPORTAL_CONFIG=<PRIVATE_HOME>\uat.json,
   DOTNET_ENVIRONMENT=Staging và ASPNETCORE_ENVIRONMENT=Staging trước lệnh CLI.
   Xác minh tài khoản/role bằng đăng nhập thật; không coi dòng CLI success là
   chứng cứ nếu cấu hình bootstrap trống. Xóa hai bootstrap secret sau hoàn tất.
   Không đặt mật khẩu mẫu, không chạy bootstrap Dev hoặc reset người đã có.
   Admin đăng nhập bằng tên tài khoản, mật khẩu và CAPTCHA theo cấu hình;
   đổi mật khẩu lần đầu nếu được cấp mật khẩu tạm, rồi vào quản trị theo quyền.
   Không có đăng ký TOTP, mã 6 chữ số hoặc recovery codes.
4. Operator cấp scanner nội bộ đáng tin cậy: MicrosoftDefenderOnPrem theo cấu hình mẫu hoặc Clamd, policy/version/StreamMaxLength
   đủ 1GiB nếu bật video, cách ly khỏi public, TLS tunnel nếu scanner ở mạng khác.
   Kiểm Clean/EICAR/failure thật tại đích trước bật nghiệp vụ. DefenderTest và
   AllowUnscanned bị chặn ở UAT. File Word/PDF/Excel và hỏi đáp đi queue private;
   ảnh CMS UAT quét đồng bộ bằng provider được duyệt, file không sạch giữ quarantine.
   **Media nguồn hiện có vẫn bị fence Development của proof cũ**: phải có quy trình
   target scan/release được duyệt trước nhập/hiển thị tại UAT. Không sửa 53 NotScanned.
5. Cấp FFmpeg/ffprobe bên ngoài repository, ghi SHA256/nguồn/giấy phép/approval trong
   Video config. Không lấy binary hoặc đường dẫn máy Dev sang target mặc định.
6. SMTP mặc định Disabled. Chỉ sau relay/policy và người nhận Test được duyệt, đặt
   Transport=Smtp, DeliveryEnabled=true, Host/Port/From/Tls=StartTls, credential đôi
   nếu relay yêu cầu. Chứng chỉ relay phải hợp lệ; AUTH chỉ sau TLS. Outbox Uncertain
   phải đối soát receipt bởi người có quyền, không tự retry. Không gửi thư thật khi
   chưa được duyệt. Worker TestSink chỉ Development/SQL Test/loopback.
7. IIS trỏ physical path vào web, app pool No Managed Code x64; operator sửa hai
   đường dẫn LANDPORTAL trong web.config, gắn HTTPS đúng host. Không expose private
   store bằng virtual directory. HSTS/redirect bật; giữ request limit 20MiB vì video
   upload theo chunk 8MiB. Không có cài service/IIS tự động trong gói.
8. Chạy foreground từng Worker dưới service identity qua:
   powershell -File ops/uat-runtime.ps1 -Home D:\LandPortal-Uat-Private
     -Config D:\LandPortal-Uat-Private\uat.json -PublishRoot D:\LandPortal-Uat-Code
     -Mode Scheduler|Documents|InquiryScan|Video|Mail
   Dùng -Once để chạy một vòng, -Health cho Documents/InquiryScan/Video. Operator thiết lập
   supervisor/restart sau nghiệm thu; SQL leases bảo vệ restart/claim.
9. Trước import hoặc thay đổi: dừng writers/scheduler phù hợp; SQL COPY_ONLY,CHECKSUM
   -> VERIFYONLY/HEADERONLY; snapshot media/snapshots/keys/config đồng thời + file
   SHA256 manifest. SQL service cần backup folder riêng đã duyệt. Thực hành RESTORE
   vào DB phục hồi riêng với MOVE mới, không WITH REPLACE vào Dev/Test/UAT đang dùng;
   đối chiếu schema/content/revision/asset/alias/quyền/hash và readback HTTP. Đánh giá
   độ lệch SQL/media và writers sau backup trước rollback. Công cụ backup Dev đã có
   không được tự đổi target sang UAT để lách guard.
10. UAT thật phải nghiệm thu: HTTPS, SQL TLS/runtime role, Clean/EICAR/failure, SMTP
    sink/relay, đăng nhập/lockout/thu hồi phiên, upload+duyệt đúng revision, draft/private ACL,
    video Range, restart worker, search tiếng Việt, restore, 3 Word lịch thật và
    dữ liệu QLDD/NukeViet. Gói publish local không phải bằng chứng các mục này PASS.
    Cutover chỉ sau PO/ATTT duyệt mapping/301/DNS/rollback và tắt nguồn sync.

## Delta P0 đã kiểm chứng local 23/10/2026

- Web và CLI bootstrap dùng cùng chính sách mật khẩu tối thiểu 7 ký tự. Thiếu
  cấu hình bootstrap hoặc mật khẩu yếu trả lỗi; không coi CLI success là tạo được
  admin. Không chạy bootstrap lên Dev đã có hoặc dùng password Test cho UAT.
- Nhật ký `/admin/audit`: cần đồng thời `users.manage`, `roles.manage` và phạm vi
  toàn hệ thống. Không tự cấp các quyền này. Chỉ đọc metadata sự kiện; không xuất
  nội dung chi tiết riêng tư. Kiểm thu hồi quyền trên cùng phiên.
- Inbox Comment có `contentId`, giữ bộ lọc khi phân trang và vẫn áp dụng phạm vi
  chuyên mục trước truy vấn. Nhãn trả lời công khai theo đúng tên Cục.
- Lịch tuần có `year`, `week`, `version`. Chỉ các release đã xuất bản được chọn;
  Word gốc phải Clean đúng byte và có xác nhận riêng tư. Gỡ lịch thu hồi toàn bộ
  lịch sử công khai; không sửa/xóa các release. Ba Word nghiệp vụ thật vẫn cần UAT.
- Khối trang chủ rỗng/lỗi ẩn theo BR-03, correlation giữ trong log vận hành.
  Không tăng SQL timeout; lỗi danh sách admin đã quan sát tại Test, root UNKNOWN.

Gói là **ứng viên triển khai UAT**, chưa phải toàn hệ thống READY FOR UAT. Vẫn cần
đóng delta FR-10 (mẫu bắt buộc/tag), FR-06 (PDF inline/counter), header/footer/menu/
slider/công khai thông tin, dữ liệu nguồn và các điều kiện PO/ATTT. Cơ chế hiệu lực
theo biên ngày Việt Nam đã có trong code; kiểm theo bằng chứng ngày được xác nhận,
không tự suy luận ngày hết hiệu lực từ metadata thiếu. Không bật private lookup.


Microsoft Defender UAT (TTDL)

- Chỉ dùng provider MicrosoftDefenderOnPrem trên Staging/target đã duyệt; không dùng chứng chỉ hay proof Test để release file UAT.
- Điền ScannerPolicyReference, DefenderPrivacyPolicyReference, DefenderBinarySha256 và ServiceAccount thực trong target packet. Pin lấy từ native binary Microsoft ký hợp lệ trên chính target.
- Chạy powershell -File ops/probe-uat-defender.ps1 dưới service identity để kiểm kê chỉ đọc; sau khi cấp config chạy lại với -Settings <PRIVATE_HOME>\uat.json. Không có thao tác thay cấu hình AV trong script.
- ATTT/TTDL phải duyệt chính sách riêng tư: SubmitSamplesConsent=2 (không gửi mẫu) và MAPSReporting=0. Adapter đọc và chặn nếu chưa đạt, không tự tắt cloud hay thêm exclusion.
- Windows service identity phải khớp config; engine active/Normal, definitions mới trong 72h; native binary SHA/signature được kiểm trước/sau quét. Binary cập nhật phải đối soát pin theo quy trình, không tự chấp nhận.
- Guard probe không thay nghiệm thu engine: chạy Clean/EICAR/failure, private download, worker restart trên target trước khi cho dùng dữ liệu nghiệp vụ. Giữ SMTP Disabled đến khi relay/người nhận UAT được duyệt.
- Clamd nội bộ vẫn là lựa chọn cấu hình riêng khi được cấp; không tự chuyển provider khi lỗi. Production chưa được mở bởi profile UAT.

Các trường nhận diện/chân trang mới nằm tại admin/site/edit?section=contact:
tên tiếng Anh, cơ quan chủ quản, người chịu trách nhiệm, giấy phép, mô tả SEO.
Nhập từ văn bản Văn phòng Cục, lưu nháp, preview, rồi áp dụng đúng revision theo
quyền. Để trống khi chưa có dữ liệu chính thức; không tự dùng số giấy phép/tên mẫu.
Thống kê truy cập là phiên cookie ẩn danh30phút, active5phút, ngày/tháng Việt Nam,
lưu SQL; không phải số người duy nhất. Không chuyển số truy cập từ web cũ thành
số giả. Kiểm thử đồng hồ, cookie/HTTPS, thống kê và privacy tại target trước UAT.


## Bổ sung thống kê đất SRS v1.2 (26/10)

Source local có migration `20261026095148_LandAreaStatistics`; bản ZIP/schema bàn giao cũ chưa bao gồm thay đổi này. Khi được duyệt bàn giao Git, build lại từ đúng commit mới và thực hiện schema theo quy trình target/backup. Không tự chạy migration trên server chưa được cấp; không downgrade/drop bảng khi đã có phiên bản nghiệp vụ.

Trang `/thong-ke-dat-dai`, quản trị `/admin/thong-ke-dat-dai`. Quyền có sẵn: `site.manage` nhập/xác nhận nháp, `site.publish` công bố đúng phiên bản; không tự cấp quyền. XLSX≤10MiB, đúng6cột SRS,34mã tỉnh từ2025, giá trị thuần; năm/ha hoặc km²/nguồn bắt buộc. Mặc định `LandStatistics:TotalMismatch=Block`; Warn chỉ khi operator quyết định và người xuất bản kiểm tra cảnh báo. Không công bố fixture.

Gốc XLSX nằm trong private media `land-statistics/`, không có public MediaAsset/URL, giữ cùng SQL phiên bản+SHA+scan proof trong backup. Chỉ Test hoặc Staging UAT đã duyệt scanner mới được nhập/công bố; cấu hình engine đơn thuần không bỏ được UAT/identity/privacy fence. Giữ 5worker hiện có; luồng này dùng scanner service hiện có, không cài worker/service thứ sáu. Cần TTDL cung cấp cấu hình đích và số liệu chính thức; preview/lưu nháp không thay bản công khai.

## Bàn giao source và dữ liệu là hai việc riêng

### Chính sách xác thực bàn giao SRS v1.2

Đăng nhập bằng tên tài khoản + mật khẩu và CAPTCHA theo cấu hình; MFA/TOTP đã được loại bỏ khỏi mã nguồn. Mật khẩu tối thiểu 7 ký tự, không bắt buộc hoa/thường/số/ký tự đặc biệt. Identity dùng cùng policy khi tạo, đổi và reset; không tự reset mật khẩu hoặc thay quyền của tài khoản hiện hữu. Schema Identity cũ giữ nguyên, cờ hai bước cũ không chặn đăng nhập.

Giữ khóa 15 phút sau 5 lần sai liên tiếp, giới hạn CAPTCHA/login, cookie HttpOnly/SameSite/Secure theo HTTPS, CSRF, audit và phân quyền server. `AdminSession:IdleMinutes=30` trong cấu hình mẫu. Các khóa cấu hình MFA cũ không có tác dụng và không cần sao chép sang đích mới.

**Đơn vị TTDL cần xem xét quyết định bỏ MFA trong nghiệm thu ATTT trước khi mở website ra Internet.** Không suy từ PASS LOCAL thành nghiệm thu UAT/Production; vẫn phải cấp máy chủ TTDL, IIS/ASP.NET Core10 Hosting Bundle, SQL Server riêng, HTTPS, ACL/kho media, Defender nội bộ, SMTP và Web + 5 Worker theo các bước trên. Không tự thay môi trường Production đang chạy.

- Source bổ sung FR-08/14/15, LAN sample-only scanner và migration `20261027025057_AuditRecoveryLookup`. Chỉ pull SHA đã được chủ dự án duyệt/push; commit local chưa có trên GitHub. Không dùng ZIP/schema sinh từ commit cũ.
- `git pull` **không mang theo 314 nội dung/325 revision/756 media/628 alias của Dev**. Không copy database Test hoặc Identity/cấu hình/khóa của Dev/LAN sang UAT.
- Khi TTDL cấp database mới, hostname/certificate HTTPS, identity Web/Worker, thư mục private, scanner và SMTP: điền cấu hình mẫu, dùng `prepare-uat-target.ps1` ràng buộc đúng đích, backup nếu có dữ liệu, chạy schema đã duyệt theo thứ tự migration. Lệnh sinh package không tự cấp database/service hay deploy.
- Chuyển dữ liệu bằng packet riêng chỉ gồm nguồn đã đối soát: published content/revision/metadata/chuyên mục/alias và byte media tương ứng, manifest SHA + mapping + audit + rollback. Không xuất phản hồi người dân, bản nháp, tài khoản, mật khẩu, token hoặc fixture. Quy trình Apply Dev hiện hành không tự cho phép ghi UAT/Production; phải có công cụ/plan được ràng buộc và phê duyệt đúng đích mới trước khi chuyển.
- 53 media NotScanned giữ cách ly/chờ quyết định; các media Clean nhưng có proof/validation chỉ dành Development cũng phải được kiểm định và phê duyệt phát hành trên đích. Không đổi nhãn sang Clean bằng cấu hình và không hotlink.
- Trưởng phòng build package từ đúng source đã duyệt, cài IIS trước .NET10 Hosting Bundle, đặt HTTPS/ACL, khởi động Web và 5 Worker theo `uat-runtime.ps1`/task XML đã duyệt; cấu hình SMTP và xác thực bằng thông tin đích. Kiểm thử UAT 16 kịch bản, scanner/privacy, backup/restore và rollback tại TTDL; không đổi DNS trước nghiệm thu.
- Provider `MicrosoftDefenderLanSample` chỉ cho Development/LandPortal_LanDemo/exact public sample hashes, chỉ preview/draft; không dùng cho UAT/Production. UAT dùng provider on-prem đã duyệt, signed binary/identity pin và policy privacy như phần trên. Không sao chép private config LAN.
