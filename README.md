# Land Portal

Cổng thông tin sử dụng **ASP.NET Core 10 MVC/Razor, EF Core và SQL Server**.
Đặc tả chính thức: **SRS v1.2 ngày 23/10/2026**, thay v1.1.
Repository chứa mã nguồn và công cụ vận hành; không chứa database, bài nguồn,
media, snapshot crawler, tài khoản, chứng chỉ hoặc cấu hình bí mật của máy Dev.

## Chức năng

- Trang chủ, chuyên mục, bài viết, tìm kiếm tiếng Việt và tra cứu tài liệu.
- CMS theo revision, phân quyền endpoint, gửi duyệt/duyệt/xuất bản, lịch xuất bản.
- Bình luận, hỏi đáp, liên hệ, góp ý dự thảo có kiểm duyệt; CAPTCHA một lần,
  phân công, SLA, tệp private và outbox có đối soát.
- Upload video trực tiếp, worker quét/xử lý, poster và HTTP Range.
- Lịch tuần có phiên bản, đọc DOCX và preview trước xuất bản.
- Identity, CAPTCHA đăng nhập, TOTP/recovery codes và thu hồi phiên.
- Crawler/import có checkpoint, chống trùng, manifest, chữ ký và verify/rollback.

**CHƯA PRODUCTION READY.** Có chức năng còn chờ dữ liệu nghiệp vụ, quyền phê duyệt
và nghiệm thu hạ tầng. Tra cứu hỏi đáp dùng mã tiếp nhận + email + CAPTCHA,
chỉ trả trạng thái/câu trả lời, không trả PII hoặc tệp riêng tư. Không coi việc
clone/build thành công là bằng chứng đã chuyển dữ liệu hay đã nghiệm thu UAT.

RB-01: đội phát triển bàn giao nguồn Git; Trưởng phòng build/cài đặt trên máy chủ
TTDL theo [hướng dẫn UAT](scripts/uat-operator-steps.md). Không đưa `.env`, backup,
private key hoặc mật khẩu vào Git. Tài khoản giả lập chỉ local Dev/Test, mật khẩu
mã hóa CurrentUser; xem [hướng dẫn quản trị](docs/site-admin-guide.md).

## Yêu cầu máy phát triển

- Windows PowerShell, Git và `rtk` (các script gọi `rtk proxy`).
- .NET SDK theo [global.json](global.json), SQL Server compatibility 160.
- Node.js/npm khi build frontend hoặc chạy browser tests; Edge cho UI tests.
- Scanner thật và FFmpeg/ffprobe do người vận hành cung cấp ngoài repository
  khi kiểm thử các luồng phụ thuộc. Không dùng mock để xác nhận file Clean.

```powershell
rtk proxy dotnet restore LandPortal.sln --locked-mode
rtk proxy dotnet build LandPortal.sln --no-restore
rtk proxy npm ci --prefix src/Web
```

Xem scripts trong [package.json](src/Web/package.json) để build frontend.

## Cấu hình và chạy local

Chỉ trên môi trường mới do bạn quản lý: tạo cấu hình riêng bằng
`scripts/init-local.ps1`, xem [.env.example](.env.example), rồi kiểm tra SQL instance,
database và kho private trước khi chạy setup/migration/bootstrap. Script init giữ
nguyên `.env` đã tồn tại và tạo secret ngẫu nhiên; không có mật khẩu mặc định.
Không commit `.env` hoặc đưa secret vào command line/log.

```powershell
rtk proxy powershell -NoProfile -File scripts/preflight.ps1
rtk proxy powershell -NoProfile -File scripts/run-dev.ps1 -Port 5188
rtk proxy powershell -NoProfile -File scripts/run-worker-dev.ps1 -NoBuild
```

Web local: http://127.0.0.1:5188; admin: `/admin/login`. Các lệnh trên cần database
đã được chuẩn bị. Web không tự migrate khi khởi động. Với database đã có dữ liệu,
backup và review migration trước; không chạy lại import để khởi tạo repository.
`LandPortal_Test` và kho Test riêng dùng cho test nghiệp vụ, không dùng Dev.
Full runner cần SQL/scanner/binaries; không chạy mặc định chỉ để kiểm tra Git.

## UAT và vận hành

```powershell
rtk proxy powershell -NoProfile -File scripts/package-uat.ps1 -RunId <UNIQUE_RUN_ID>
```

Xem [hướng dẫn UAT](scripts/uat-operator-steps.md),
[config mẫu](scripts/uat-config.example.json) và [launcher](scripts/uat-runtime.ps1).
UAT dùng Staging; `LandPortal_Uat` chỉ là tên đề xuất, chưa được cấp database.
Config/kho private nằm ngoài publish; SQL TLS hợp lệ và MFA bắt buộc. Scanner là
Clamd hoặc MicrosoftDefenderOnPrem đã được đơn vị vận hành phê duyệt, pin đúng
identity/hash và kiểm chứng tại đích. SMTP mặc định tắt.
Gói publish không tự cài IIS/service, cấp quyền, migrate, gửi thư hoặc deploy.

Media nguồn có policy phát hành riêng; proof Test không tự cấp quyền phát hành
UAT/Production. Scanner, SMTP, restore, quyền vận hành, dữ liệu nghiệp vụ và UAT
thật phải được nghiệm thu trước cutover. Import Dev cần approval exact batch/hash,
backup SQL/media và guard chữ ký/scanner/target/preflight đạt.

## Tài liệu và phạm vi repository

- [Kiến trúc](docs/architecture.md), [CMS/revision](docs/cms-architecture.md).
- [Biên tập](docs/editor-guide.md), [tài khoản](docs/account-admin-guide.md),
  [cấu hình cổng](docs/site-admin-guide.md), [scheduler](docs/scheduler-runbook.md).
- [Tương tác](docs/interaction-admin-guide.md), [outbox](docs/interaction-outbox-runbook.md),
  [scanner](docs/scanner-operations.md), [tài liệu](docs/editorial-documents-runbook.md).
- [Video](docs/video-admin-guide.md), [worker video](docs/video-worker-runbook.md),
  [dependencies](docs/dependencies.md), [license/security](docs/licenses-security.md).

Các hướng dẫn có thể dẫn tới báo cáo/approval/evidence nội bộ không có trên GitHub.
Không dùng ví dụ trong hướng dẫn làm quyền thực hiện thao tác ghi trên hệ thống thật.
SRS Word, báo cáo nghiệm thu, dữ liệu crawler và bằng chứng nội bộ giữ ngoài
phạm vi bàn giao nguồn. `MASTER_PROMPT_LAND_PORTAL.md` là marker xác định workspace
được công cụ hiện hành sử dụng; SRS v1.2 vẫn là đặc tả chính thức. Chỉ fixture video tổng hợp không chứa dữ liệu nguồn được kèm
theo test; binary FFmpeg, database và media thật không được kèm theo.
