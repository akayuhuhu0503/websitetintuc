# Cập nhật BA sau SRS v1.2 — mật khẩu và đăng nhập lần đầu, 29/10/2026

Yêu cầu BA mới thay chính sách 7 ký tự trước đây: tối thiểu **8 ký tự**, đủ chữ hoa A–Z, chữ thường a–z, số 0–9 và ký tự đặc biệt. Identity dùng cùng policy khi tạo, đổi, reset và bootstrap. Không thêm MFA/TOTP. Mật khẩu tài khoản hiện có vẫn được xác thực bình thường; không chạy migration hoặc quét/đổi hàng loạt hash, cờ hay quyền.

## Tạo và reset tài khoản

1. Người có `users.manage` mở `/admin/users/create`, nhập tên đăng nhập/email/tên hiển thị. Không có ô mật khẩu do quản trị tự chọn. Hệ thống sinh riêng mật khẩu 24 ký tự bằng CSPRNG; tài khoản chưa có nhóm quyền và có `MustChangePassword=true`.
2. Sau commit, trang chi tiết chỉ thông báo **đường dẫn tệp private trên máy chủ**, không trả plaintext mật khẩu qua HTTP. Tệp JSON tại `<Storage.TempRoot>/identity-credentials/<random-id>.json`, tạo mới không ghi đè, ACL Windows chỉ identity chạy Web/CLI và SYSTEM. Không đặt kho này trong code/webroot; không đưa vào Git, log, artifact công khai hay email website.
3. Cán bộ vận hành có quyền đọc tệp qua phiên quản trị máy chủ được bảo vệ, bàn giao cho đúng người qua kênh an toàn đã duyệt. Trên IIS, identity app pool sở hữu tệp; không tự mở ACL cho Users/Everyone. Nếu cần cán bộ khác đọc, TTDL phải cấp kênh/quyền riêng theo quy chế. Xử lý/xóa tệp sau bàn giao theo quy chế; nếu mất mật khẩu thì dùng reset, không đọc lại hash để khôi phục mật khẩu.
4. Quản trị gán nhóm/phạm vi theo workflow hiện có. Người dùng đăng nhập bằng mật khẩu tạm và CAPTCHA → bắt buộc đổi mật khẩu → quản trị theo quyền. GET/POST quản trị và endpoint có yêu cầu xác thực ngoài `/admin` cũng bị chặn; request editor nhận403 với mã `password_change_required`. Chỉ trang đổi mật khẩu và đăng xuất được phép qua gate.
5. Reset tại trang chi tiết tài khoản → **Tạo mật khẩu tạm mới**. Endpoint POST `/admin/users/{id}/reset-password` kiểm tra quyền service/controller, quyền đặc biệt nếu đối tượng có quyền nhạy cảm, concurrency stamp, CSRF và đường quản trị cuối cùng. Không tự reset tài khoản đang dùng; không reset tài khoản bị vô hiệu hóa. Mật khẩu sinh mới, security stamp đổi, bắt buộc đổi lần tới; role/phạm vi giữ nguyên.

Nếu ghi SQL thất bại hoặc kết quả commit chưa chắc chắn, tệp private đã phát sinh có thể còn để đối soát. **Không bàn giao tệp của thao tác chưa xác nhận thành công**, không tự retry/reset hoặc công bố mật khẩu. Tệp không phải bằng chứng tài khoản đã được tạo/reset. Audit chỉ ghi actor/đối tượng/trạng thái, không ghi mật khẩu/token/đường dẫn bí mật.

## Đổi mật khẩu và bootstrap

Trang `/admin/account/change-password` yêu cầu cookie đăng nhập, mật khẩu hiện tại, mật khẩu mới khác mật khẩu hiện tại và xác nhận khớp. Báo từng điều kiện thiếu bằng tiếng Việt. Identity đổi hash/security stamp, xóa cờ bắt buộc và ghi audit trong cùng transaction, rồi refresh phiên hiện tại; các cookie cũ bị từ chối trên yêu cầu kế tiếp. Không thay CAPTCHA, lockout5lần/15phút hoặc idle30phút.

Bootstrap vẫn là thao tác operator riêng trên đúng target được duyệt, không chạy trong Web startup: private config cần `Bootstrap:AdminEmail` và các kho Storage đã cấp. Không dùng `Bootstrap:AdminPassword`; hệ thống sinh mật khẩu tạm/tệp private như trên. Tài khoản mới bắt buộc đổi lần đầu; chạy lại với email đã có không đổi mật khẩu/cờ/roles. Không tự chạy bootstrap khi cập nhật LAN.

## Kiểm tra và cập nhật LAN

- Kiểm tra offline tập trung: `LandPortal.Tests.dll --ba-password-offline` (Identity thật trên store in-memory cô lập, không phải SQL E2E).
- Kiểm tra SQL/HTTP: `LandPortal.Tests.dll --ba-password-http --test-backup-receipt <FRESH_VERIFIED_TEST_RECEIPT>`, chỉ `LandPortal_Test`; giữ guard backup. Khi owner tạm hoãn backup và chưa có receipt mới hợp lệ, kiểm tra này **BLOCKED/NOT RUN**, không bỏ guard hoặc lấy unit test thay nghiệm thu SQL/HTTP.
- Cập nhật LAN bằng binary Web đã build, giữ nguyên `.local/lan-demo/data`, config/keys/credentials và `LandPortal_LanDemo`; dùng `stop-lan-demo.ps1`, đổi đúng pointer web rồi `run-lan-demo.ps1`. Không chạy `LanDemoSetup` không tham số (tạo DB/seed), bootstrap, reset-password, migration hoặc copy Dev sang LAN. Giữ binary cũ để quay lại nếu startup/smoke thất bại. Không cần migration: `MustChangePassword` đã có.

Không áp dụng hoặc thay đổi SQL dùng chung `website_tin_tuc`, Production, DNS hoặc quyền vận hành trong lượt cập nhật này. SRS v1.2 giữ nguyên bản chính thức; đây là yêu cầu BA bổ sung, không tự nâng toàn dự án thành PRODUCTION READY.

## Source thay đổi và bằng chứng

Operator Windows ADMIN có hai command tách biệt cho LAN: `reset-lan-admin-password` chỉ admin và `reset-lan-ba-test-password --user-name lan-ba-writer|lan-ba-reviewer` chỉ hai account giả lập đã có. Cả hai yêu cầu target/physicalmachine/expiry/WindowsADMIN/ACLfile/nooverwrite/active+unlocked/exactrole/preservation guards; không tạo tài khoản hoặc thay quyền. Đây là thao tác khôi phục owner riêng, không phải cách bỏ xác thực/guard reset của CMS. Chỉ chạy khi có phê duyệt reset cụ thể; không chạy tự động khi deploy. Sau reset cả3, người quản trị cũng phải đổi mật khẩu trước khi quản lý tài khoản.

- Policy/credential/service/bootstrap: `PortalIdentityPolicy.cs`, `TemporaryPasswordDelivery.cs`, `AccountAdministrationService.cs`, `BootstrapIdentity.cs` trong `src/Infrastructure/`.
- Web: `AccountAdministrationController.cs`, `AdminController.cs`, `OwnAccountController.cs`; `FirstLoginPasswordGate.cs`; `AccountModels.cs` và `IdentityPasswordPolicyAttribute.cs` thay `IdentityPasswordLengthAttribute.cs`; `Program.cs`; ba view `Accounts/Create`, `Detail`, `ChangePassword`.
- Tool/config mẫu/hướng dẫn: `tools/LanDemoSetup/LanAdminPasswordReset.cs`, `scripts/init-local.ps1`, `.env.example`, `README.md`, `scripts/uat-operator-steps.md`; không sửa `.env` hoặc config private.
- Focused tests: `BaPasswordOfflineTests`, `OwnPasswordHttpTests`, `Phase2bAccountTests`, `Phase2bAccountHttpTests`, `P0ReadyTests`, runner `tests/LandPortal.Tests/Program.cs`.
- Kết quả code cuối: build Web/CLI/tests và tool reset0warning/0error;34offlinePASS;5LANauthHTTPsmokePASS riêng;11nhóm bảngLAN0mismatch. Không full regression. SQLTest first-login/reset E2E chưa chạy do freshbackup guard và quyết định tạm hoãn backup.
- LAN đã dùng binary này tại `http://192.168.0.122:5188/`; tài khoản cũ không bị sửa. Pointer chứa workingTreeBaPassword=true vì source chưa commit/push; không dùng một SHA Git sạch để đại diện cho toàn bộ WIP. Thao tác từ thiết bị BA/UAT vẫn cần xác nhận.
