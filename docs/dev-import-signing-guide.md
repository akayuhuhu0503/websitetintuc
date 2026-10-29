# Ký duyệt Dev import — cơ chế hiện hành và việc cần authority

**Sau xác nhận Windows20/10:** certificate Dev thumb94797BD79E275B09DEF613036BC00DF4EA91345F đã trust CurrentUser/Root, AuthenticodeValid,16/16 focusedfixture checksPASS (`dev-signing-confirmed-20261020T030517Z-a0e9ad77`). Không thêmLocalMachine/TrustedPublisher/policytoànmáy. Các nhãn chờSecurityWarning bên dưới là lịch sử. Runtime thumbprint policy và approval batch thật còn riêng/chưa có; không tự cấp quyền Apply từ fixture. [Báo cáo](dev-code-signing-report.md).

**Cập nhật20/10/2026:** chủ máy xác nhận máy Dev cá nhân/Administrator và đổi tên THANGPD→MinhAnh; cho phép chuẩn bị thiết lập self-signed Code Signing CurrentUser cho Dev. Điều này thay các nhãn thiếu hồ sơ rename và cấm tự tạo cert Dev trong hướng dẫn lịch sử bên dưới, **không** cấp approval cho batch thật hoặc signerProduction. Xem [setup/lệnh](dev-code-signing-setup.md), [báo cáo](dev-code-signing-report.md). Certificate mới thumbprint`94797BD79E275B09DEF613036BC00DF4EA91345F`,30ngày/non-exportable; trust đang chờ WindowsSecurityWarning, chưa AuthenticodeValid. Không sửa `.env`/runtime signer policy/grants/SQL guard; không Apply Dev.

Đọc [ApprovedBatchExecution.cs](../src/MigrationCli/ApprovedBatchExecution.cs) và [SafeBatchCommand.cs](../src/MigrationCli/SafeBatchCommand.cs). Lượt 3GFix không tạo certificate, private key, trust pin hoặc approval giả. Business authorization đã được chủ dự án xác nhận trong chat; các gate chữ ký hiện hành chưa đạt.

## Ba khái niệm khác nhau

| Khái niệm | Code đang kiểm |
|---|---|
|Authenticode của tài liệu PowerShell|`Get-AuthenticodeSignature` phải trả `Valid`; certificate thumbprint nằm trong policy được duyệt. Approval `.ps1` chỉ được đọc, không thực thi. Không phải yêu cầu mọi script dự án đều đã ký.|
|Chữ ký/binding của manifest|PlanJSON **không có detached signature riêng** trong công cụ hiện hành. SHA256 exact plan nằm trong metadata của approval được ký Authenticode; thay plan hoặc media-root phải preview và duyệt/ký lại. Source PDF signature là việc khác.|
|Phê duyệt nghiệp vụ|Chủ dự án cho phép batch có điều kiện đã USER_CONFIRMED. Cấu hình signer do tổ chức chọn/duyệt; tác nhân không tự biến xác nhận chat hoặc certificate bất kỳ thành authority crypto.|

## Certificate, chain, thumbprint và identity

Đã đọc metadata **CurrentUser/My và LocalMachine/My**, lọc Code Signing EKU `1.3.6.1.5.5.7.3.3`:0candidate. Không đọc/export private key, không kiểm token/HSM ngoài phạm vi được cung cấp. Không có cert cụ thể nên chưa thể cung cấp một thumbprint/chain/subject đã được chấp thuận. Chứng chỉ TLS localhost hoặc chữ ký Microsoft trên Defender binary không cấp quyền ký duyệt batch dự án.

Người có thẩm quyền cần chọn certificate dùng được cho Authenticode (thông thường Code Signing EKU), xác minh quyền sử dụng khóa và chain theo chính sách đơn vị. Điều kiện thực thi là Windows Authenticode status `Valid` trên **đúng approval file**, signer thumbprint khớp chính xác danh sách `SafeImport__ApprovedSignerThumbprints`. Không tự thêm root/CA vào Windows trust store. Một chain kiểm offline hoặc certificate còn hạn chưa chứng minh toàn bộ điều kiện Authenticode/thu hồi/thẩm quyền.

Signer certificate có thể thuộc người ký/đơn vị có thẩm quyền; **OperatorSid** là SID Windows của người thực thi import trên máy local, không phải username/role ASP.NET Identity, cũng không nhất thiết là chủ certificate. Code so SID với `WindowsIdentity.GetCurrent().User.Value`. SID quan sát được nằm trong private `certificate-inventory.json`; không cấp quyền hoặc thay tài khoản để khớp. Operator phải có sẵn quyền SQL db_owner/sysadmin để kiểm backup/Apply; công cụ không tự grant.

## Trình tự để authority duyệt

1. Đối chiếu báo cáo định danh mới: `.\SQLEXPRESS`/`MinhAnh\SQLEXPRESS`, service/PID/databaseGUID trùng; `@@SERVERNAME` còn THANGPD. Ghi nhận xác nhận lại target tuple; không tự đổi metadata/expected guard. Business approval có điều kiện vẫn giữ, không cần ký lại vì thiếu một file chat riêng.
2. Tổ chức cung cấp signer certificate/chain/identity và quyết định approved thumbprint. Nếu chưa có, dùng quy trình cấp Code Signing của đơn vị/CA được chấp thuận, kiểm soát khóa và người ký; không tự sinh self-signed rồi cài trusted root. Nếu ký ở máy/token khác, người ký chỉ cần nhận đúng plan/hash/metadata, không cung cấp private key cho agent.
3. Người vận hành được phép cấp `SafeImport__ApprovedSignerThumbprints` qua cấu hình runtime được duyệt (biến môi trường tiến trình là cơ chế code đã đọc). **Agent chưa cấu hình và không sửa `.env`**. Thumbprint chỉ lấy từ cert đã được authority duyệt, không tự pin cert vừa tìm thấy.
4. Fresh preview và backupDev chính xác: schema3/media-root hash/checkpoint/proof/source stamps còn hiệu lực, backup<=12h với SHA/header/VERIFYONLY. Không ký planTest để dùngDev. Proof phải còn24h và đúng current definitions theo recovery policy. Bảo vệ thêm SQLTest proof receipts và các file dependency mới.
5. Người có thẩm quyền cung cấp file `.ps1` đã ký hợp lệ dưới private `.local/approvals/phase3c/`. Metadata một dòng bắt đầu `# LANDPORTAL_PHASE3C_APPROVAL `, JSON có:

   | Trường | Giá trị phải khớp |
   |---|---|
   |`ApprovalId`|Mã duyệt thật, không rỗng|
   |`PlanSha256`|SHA256 exact Dev plan được duyệt|
   |`TargetDatabase`|`LandPortal_Dev`|
   |`OperatorSid`|Windows SID thực thi đã được duyệt|
   |`ExpiresUtc`|Còn hạn và không quá7ngày tại lúc kiểm|
   |`Actions`|`Apply`; thêm `Rollback` chỉ nếu authority cho phép|

6. Kiểm read-only `Get-AuthenticodeSignature -LiteralPath <APPROVAL_FILE>`; đối chiếu status/thumbprint và metadata. Không chạy approval như script. SafeApply giữ lock trên signed bytes, kiểm lại hash/chữ ký/target/action/expiry/backup ở Demand và ghi audit `Phase3C.DevAuthorization`. File bị đổi hoặc plan khác sẽ không được chấp nhận.
7. Chỉ sau tất cả gate: safe-apply → verify/readback → idempotency/smoke → đối chiếu oldDev. Có lỗi/ack không chắc chắn thì reconcile trước retry. Không dùng directSQL/batch-commit Dev thay signed entrypoint.

Các bước trên là hướng dẫn trình duyệt, **không phải phê duyệt hoặc cấu hình đã thực hiện**. Chưa có file Authenticode để chạy positive signature path; trạng thái hiện tại vẫn **BLOCKED_SIGNER_POLICY_AND_SIGNED_APPROVAL**.
