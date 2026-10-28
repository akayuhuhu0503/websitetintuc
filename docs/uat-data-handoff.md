# Bàn giao dữ liệu công khai cho TTDL — SRS v1.2

Gói chỉ chứa whitelist nội dung nguồn đã công khai, published revision, danh mục/metadata/alias và cấu hình live để đối chiếu. Không có Identity, tài khoản/mật khẩu, token, CAPTCHA/session, outbox, hồ sơ người dân, audit Dev, crawler snapshot hoặc fixture. Không restore/copy nguyên Dev/Test/LAN.

`data.json` tách `Ready` và `PendingReview`. `Ready` chỉ gồm nội dung không phụ thuộc media bị chặn. `PendingReview` có byte/hash Clean trên Dev nhưng **chưa được cấp quyền phát hành UAT**. Media `Phase3BValidated` không tự đổi thành Clean dùng trên UAT. 53 NotScanned và tệp lỗi không được chép byte; `exclusions.json` ghi từng Content/Media và lý do. Tổng nguồn QLDD/GDLA vẫn chưa xác định, gói không phải toàn bộ dữ liệu nguồn.

## Source và công cụ

Clone/pull repository `https://github.com/akayuhuhu0503/websitetintuc`, nhánh `main`, rồi đối chiếu SHA bàn giao trước build. Source công cụ nằm tại `tools/UatDataHandoff/`, launcher tại `scripts/uat-data.ps1`. Không chép dữ liệu gói vào Git. Bản gói dữ liệu cũ có thể chứa `ops/source/` để đối chiếu; dùng source từ commit bàn giao đã duyệt khi build. Công cụ tái sử dụng EF model, UAT target guard, scanner và signature bridge hiện có; không thay CMS hoặc importer Dev.

```powershell
dotnet build tools/UatDataHandoff/UatDataHandoff.csproj -c Release -p:RestoreLockedMode=true
```

Đường dẫn tool mặc định sau build: `tools/UatDataHandoff/bin/Release/net10.0/UatDataHandoff.dll`. SDK .NET10 bắt buộc; IIS cần .NET10 Hosting Bundle, app pool No Managed Code x64/Integrated và HTTPS. Build này không cấp database hoặc service.

## Trước import

1. TTDL cấp SQL Server/database riêng (không tạo lại nếu đã cấp), operator Windows hoặc SQL Login được duyệt có quyền import, hostname HTTPS, bốn private stores/ACL, thư mục backup SQL mà SQL service ghi và operator đọc được. Điền profile Staging UAT theo `uat-operator-steps.md`, không copy secret Dev. `LandPortal_Uat` chỉ là tên đề xuất; cấu hình database dùng chung đã cấp theo [shared-sql-iis.md](shared-sql-iis.md).
2. Chạy schema SQL đã ràng buộc đúng target từ migrations hiện hành, rồi `site-initialize` và bootstrap tài khoản riêng theo runbook. Không seed BA/Test/Dev Identity. Mật khẩu ≥7, không độ phức tạp bắt buộc; giữ CAPTCHA, lockout5 lần/15phút, idle30phút, CSRF/cookie/quyền/audit. Không có MFA; TTDL phải nghiệm thu quyết định này về ATTT trước Internet.
3. Thiết lập Defender nội bộ theo privacy policy, service identity, Microsoft binary pin và definitions hợp lệ; hoặc Clamd nội bộ đã duyệt. SMTP mặc định Disabled, chỉ bật relay/STARTTLS theo phê duyệt. Web và 5worker dùng cấu hình/identity riêng như runbook, không cài service hay mở firewall tự động.
4. Đơn vị có thẩm quyền cấp signer code-signing được trust trên target, cấu hình `SafeImport:ApprovedSignerThumbprints` của target. **Không dùng certificate/pin Development.** Người duyệt phải duyệt đúng package/plan/selection và quyền phát hành. Công cụ chỉ đọc Authenticode; không chạy script approval, không tự ký.

## Kiểm tra offline và lập plan đúng đích

Chạy `ops/uat-data.ps1` với `-ToolPath` DLL đã build và `-Package` thư mục gói:

```powershell
./uat-data.ps1 -Mode inspect -ToolPath <TOOL_DLL> -Package <PACKAGE>
./uat-data.ps1 -Mode offline-plan -ToolPath <TOOL_DLL> -Package <PACKAGE>
./uat-data.ps1 -Mode plan -ToolPath <TOOL_DLL> -Package <PACKAGE> -DataHome <PRIVATE_HOME> -Config <UAT_JSON> -Output <NEW_PLAN_DIRECTORY>
```

Mặc định chỉ chọn `Ready`. Muốn xét đầy đủ các ứng viên Clean chưa được release, thêm `-ReviewedCandidates` **nhất quán cho plan/import/verify/rollback**. Plan chỉ đọc SQL, kiểm schema/server/database và không ghi đè ID/source/hash/alias đã có. `approval-UNSIGNED.ps1` là bản nháp, không phải phê duyệt. Điền ExpiresUtc (tối đa7 ngày), ApprovalReference thật, Actions (`Import`; `Rollback` phải được duyệt riêng) rồi ký Authenticode bằng signer TTDL; tuyệt đối không tự tạo chữ ký/approval giả.

## Backup → import → verify

```powershell
./uat-data.ps1 -Mode backup -ToolPath <TOOL_DLL> -Package <PACKAGE> -DataHome <PRIVATE_HOME> -Config <UAT_JSON>
./uat-data.ps1 -Mode import -ToolPath <TOOL_DLL> -Package <PACKAGE> -DataHome <PRIVATE_HOME> -Config <UAT_JSON> -ManifestSha <EXACT_MANIFEST_SHA> -PlanSha <EXACT_PLAN_SHA> -ConfirmTarget <ALLOCATED_DATABASE> -Approval <SIGNED_APPROVAL_PS1> -BackupReceipt <BACKUP_RECEIPT_JSON>
./uat-data.ps1 -Mode verify -ToolPath <TOOL_DLL> -Package <PACKAGE> -DataHome <PRIVATE_HOME> -Config <UAT_JSON>
./uat-data.ps1 -Mode reconcile -ToolPath <TOOL_DLL> -Package <PACKAGE> -DataHome <PRIVATE_HOME> -Config <UAT_JSON>
```

Backup SQL COPY_ONLY/CHECKSUM + VERIFYONLY/HEADER/hash, không ghi đè; receipt riêng dưới private home `.local/backups`. VERIFYONLY không phải restore drill. Trước vận hành, TTDL phải backup đồng bộ media/snapshot/keys/config và diễn tập restore riêng. Import chỉ thêm file trong namespace package bằng CreateNew/hash, không sửa/xóa file cũ; lỗi SQL để lại file private có thể đối soát, không được công khai orphan.

Với **mọi media được chọn**, import luôn chạy scanner engine thật trên target, đúng byte/hash sau khi kiểm approval. Chỉ Clean mới được ghi thành SourceValidated/Clean cùng audit proof target; Infected/ScanFailed dừng trước ghi nội dung SQL. Đây là quét phục vụ phát hành mới tại TTDL, không phải quét lại Dev hoặc giả định proof Test hợp lệ. Không có flag bypass scanner. Vì chưa có target/scanner/signer thật, bước này chưa được nghiệm thu tại local.

SQL import là một transaction + exclusive application lock + durable receipt theo package. Lỗi commit không rõ phải reconcile trước retry. Cùng selection/package đã commit chỉ verify và trả AlreadyCommitted/0 writes; không copy database. Danh mục xung đột không bị ghi đè. Chỉ published revision được mang sang, không có bản nháp biên tập. Cấu hình public nguồn chỉ nhập thành **draft** nếu toàn bộ tham chiếu đã được import; admin phải review/apply qua workflow site hiện có. Nếu thiếu logo/banner/content, giữ cả cấu hình trong gói để đối chiếu, không bỏ menu âm thầm.

## Rollback có phạm vi

Lập/sign approval Action `Rollback` đúng cùng plan/selection và tạo backup mới, sau đó chạy `-Mode rollback` với các tham số xác nhận như import. Chỉ unpublish các content thuộc receipt và gỡ alias đúng batch; giữ immutable revisions/media/source để điều tra. Không restore toàn DB hoặc xóa dữ liệu người dùng. Nếu có bản sửa/new revision, rowversion thay đổi, site draft đã publish hoặc drift hash thì dừng để người phụ trách đối soát. Config draft chỉ quay về live cũ khi còn đúng draft do package tạo. Không tự import lại batch đã rollback.

## Bằng chứng cần nghiệm thu tại TTDL

SQL target plan/backup/import/readback/idempotency/rollback, native Clean/EICAR/failure, alias301→bài, ảnh/PDF/ACL, menu/draft publication, HTTPS/IIS, Web+5workers, SMTP, restore và UAT16. Kết quả offline không thay các bằng chứng này. Công cụ hiện chỉ cho profile UAT đã được duyệt, không có đường ghi Dev/Test/LAN/Production. Không deploy/DNS hoặc gửi email từ quá trình export/package.
