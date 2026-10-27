# Quản trị tài khoản và nhóm quyền — 2B

## Bổ sung V04: phạm vi chuyên mục

Chi tiết tài khoản có mục phạm vi: legacy/toàn bộ hoặc chọn tập chuyên mục cụ thể. Scope cụ thể bao trùm **mọi** mục chính/phụ của draft và revision public/current; lịch sử/media ngoài scope không đọc được. Tập rỗng không là quyền toàn bộ. Cần ManageUsers+GrantPermissions; gán toàn bộ còn cần GrantPrivileged và actor đang toàn bộ. Không tự thay scope mình hoặc gán rộng hơn actor, dù role có EditAll. Optimistic stamp/applock/audit và SecurityStamp revoke phiên áp dụng như2B; worker đọc scope mới.

Chưa gán scope/quyền Dev trong V04.10 quyền vận hành vẫn thiếu, ghi tại báo cáo; không reset mật khẩu. Kiểm thử assignment/concurrency/revoke/self/expansion dùng SQLTest và tài khoản QA riêng. Cookie idle30min, polling không renew; editor có reauth cùng principal giữ prose như hướng dẫn biên tập. Web V04 riêng5198, web5188 có trước lượt dùng binary cũ.

Các màn hình `/admin/users` và `/admin/roles` dùng Identity đang có. Không có đăng ký công khai, mật khẩu mặc định, xóa tài khoản hay chức năng gửi/reset mật khẩu. Đăng nhập không dùng MFA/TOTP theo quyết định SRS v1.2; đơn vị vận hành phải xem xét trong nghiệm thu ATTT trước khi mở Internet.

## Quyền và phạm vi ủy quyền

- `users.manage`: xem, tìm và phân trang tài khoản; tạo tài khoản nội bộ; sửa email/tên hiển thị; vô hiệu hóa/mở lại; thu hồi phiên. Không tự cấp quyền xuất bản hoặc phân quyền.
- `roles.manage`: xem, tạo và sửa tên nhóm. Thay đổi tập quyền cần thêm `permissions.grant`.
- `permissions.grant`: gán/bỏ nhóm cho người khác khi có `users.manage`; cấp/bỏ permission của nhóm khi có `roles.manage`.
- `permissions.grant.privileged`: cho phép tác động quyền nhạy cảm khi có các quyền quản lý/cấp quyền tương ứng. Các quyền xuất bản, lịch đăng, cấu hình, người dùng, nhóm, cấp quyền và import được coi là nhạy cảm.

Chỉ được cấp hoặc thu hồi các quyền thuộc catalog và chính mình đang có. Quyền nhạy cảm cần quyền ủy quyền đặc quyền tường minh. Không sửa tập quyền của nhóm mình đang thuộc; không tự thay nhóm của mình. Quản lý tài khoản có quyền nhạy cảm cũng phải qua kiểm tra phạm vi ủy quyền ở server. Tên nhóm `Administrator` không tự tạo quyền mới.

Mọi thay đổi Identity chạy trong transaction SQL Server có cùng application lock. Kiểm tra đường quản trị còn hoạt động kết hợp `users.manage`, `roles.manage`, `permissions.grant`; một thao tác không được làm mất đường cuối cùng. Request đồng thời về tài khoản và nhóm cũng dùng cùng lock. Mỗi form có concurrency stamp; lỗi 409 yêu cầu tải lại và đối chiếu.

## Tạo tài khoản và mật khẩu tạm

1. Mở **Tài khoản → Tạo tài khoản**. Nhập tên đăng nhập, email nội bộ, tên hiển thị và mật khẩu tạm riêng (tối thiểu 7 ký tự, không bắt buộc độ phức tạp).
2. Cổng lưu password hash bằng Identity, không ghi mật khẩu vào audit, báo cáo hoặc log. Không gửi email. Chuyển mật khẩu qua kênh nội bộ được cơ quan chấp thuận.
3. Tài khoản mới chưa có nhóm quyền. Người có quyền cấp nhóm chọn các nhóm phù hợp trên màn hình chi tiết rồi áp dụng. Không chọn quyền nhạy cảm khi không cần.
4. Sau đăng nhập, người dùng bắt buộc đổi mật khẩu của mình tại `/admin/account/change-password` trước khi truy cập màn hình hoặc endpoint quản trị. Chỉ đổi mật khẩu và đăng xuất được phép trong thời gian này.

Không nhập mật khẩu vận hành vào tài liệu/chat/repo. Luồng này tạo tài khoản mới; không reset mật khẩu người đang sử dụng.

## Đổi/thu hồi quyền và phiên

Chi tiết tài khoản thể hiện nhóm và tập quyền được gán; tài khoản bị khóa hoặc chưa đổi mật khẩu tạm chưa thực thi quyền đó. Thay nhóm, thay tập quyền của nhóm, khóa tài khoản và thu hồi phiên cập nhật security stamp. Cookie được kiểm tra security stamp/trạng thái từ SQL trên mỗi request; quyền endpoint cũng lấy từ SQL hiện thời. Thay nhóm dùng chung thu hồi phiên của tất cả thành viên. Request đang đăng nhập với cookie cũ phải đăng nhập lại.

Vô hiệu hóa giữ nguyên người dùng, password hash, revision, audit và chủ thể lịch sử. Mở lại không tự cấp thêm nhóm hoặc quyền. Không có nút xóa người dùng/nhóm; mốc này chưa cung cấp reset mật khẩu hoặc gửi thư.

## Cấp quyền vận hành tường minh khi tài khoản cũ chưa có quyền mới

Permission mới chỉ đăng ký trong catalog, không tự gán vào role/tài khoản Dev. Người vận hành có thể thiếu `roles.manage`, `permissions.grant`, `permissions.grant.privileged`, `site.view`, `site.manage`, `site.publish`, `content.schedule` tùy nhiệm vụ. Giữ các quyền riêng, cấp đúng phạm vi thực tế.

Đã chuẩn bị công cụ local `scripts/grant-portal-permissions.ps1`. Công cụ chỉ nhận `LandPortal_Dev` hoặc `LandPortal_Test`, Windows authentication trên SQL Express local, ID và tên đăng nhập khớp chính xác. Mặc định chỉ dry-run. Ví dụ dùng ID/tên do người vận hành xác minh (các chuỗi dưới đây là chỗ điền, không phải tài khoản có sẵn):

```powershell
rtk proxy powershell -NoProfile -File scripts/grant-portal-permissions.ps1 `
  -Database LandPortal_Dev -UserId "ID_DA_XAC_MINH" -ExpectedUserName "TEN_DANG_NHAP_DA_XAC_MINH" `
  -Permission "roles.manage,permissions.grant,permissions.grant.privileged"
```

Đọc kết quả dry-run rồi người có thẩm quyền chạy lại cùng tham số với `-Apply`. Nhánh apply thêm các claim được chọn theo catalog, thu hồi phiên và ghi audit với chủ thể Windows; không đổi password hoặc `.env`. Công cụ không phải endpoint web/recovery công khai. Lượt triển khai không dùng công cụ để tự cấp quyền Dev.

## Bằng chứng và giới hạn

Lần chạy SQL Test thực tế có 10/10 nhóm rủi ro PASS trong `.local/evidence/phase2b/account-sql-tests.json`. Lần HTTP/UI sau sửa cuối cho vùng tài khoản (run `ba3e451e-59d3-42c6-9626-cae4f00fa175`, 13/10/2026 10:18:27–10:18:49 Việt Nam) có 5/5 nhóm HTTP PASS; riêng browser có 18/18 lượt kiểm tra màn hình và 4 thao tác UI thực, 0 lỗi trang/0 request ngoài origin. Artifact có hash DLL web/test và xác nhận tiến trình web Test đã dừng tại `.local/evidence/phase2b/accounts/http-report.json`; ảnh/kiểm tra UI ở `accounts/ui/`.

Browser dùng Edge desktop giả lập viewport 390/1440 cho sáu màn hình và 360/768/1024 cho danh sách người dùng/nhóm. Đã xem ảnh chi tiết tài khoản 390 và editor nhóm 1440. Lỗi thật tiêu đề tên đăng nhập dài tràn chiều rộng 390 đã được sửa, chạy lại và giữ artifact thất bại trước sửa (`http-mobile-failed.json`, `ui-mobile-failed.json`, `user-detail-mobile-failed.png`). Công cụ cấp quyền cũng được dry-run thật trên một tài khoản QA trong SQL Test; so sánh độc lập security stamp/số claim/số audit trước-sau không thay đổi (`permission-dry-run.json`). Không chạy apply quyền Dev.

Không cộng các nhóm SQL/HTTP/UI thành full suite hoặc UAT. Regression chung sau thay đổi khác và trạng thái nghiệm thu 2B được ghi riêng trong báo cáo 2B. CHƯA PRODUCTION READY.
