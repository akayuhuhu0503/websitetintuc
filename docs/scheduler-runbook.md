# Vận hành scheduler local — Giai đoạn 2B

## Bổ sung V04 — 14/10/2026

Worker dùng quyền/scope requester và reviewer **hiện thời**; mất một chuyên mục phụ cũng chuyển NeedsReview, không tự nới quyền hoặc đăng revision cũ. Cấp lại quyền không tự tái dùng approval/lịch bị đóng; operator qua workflow và lập lịch đúng revision mới. Không cấp content.schedule Dev trong lượt này.

Chạy binary V04 riêng: `rtk proxy powershell -NoProfile -File scripts/run-worker-dev.ps1 -IsolatedBuild -NoBuild`; kiểm tra Test giới hạn: thêm `-TestProfile -Once`. Chạy Dev là thao tác vận hành, không cần để xem bốn trang public và chưa được tự cài service. Worker/publish probes V04 thực chạy Test, không chạy lịch Dev. Có thể dùng Background/stop descriptor hiện có với IsolatedBuild; không kill dotnet/SQL hàng loạt.

Scanner policy không đổi: NotScanned không Clean, ngoại lệ chỉ Development local tường minh; Staging/Production hoặc worker sai môi trường bị chặn. Pkcs app-local10.0.13 được xác nhận ở Release worker; process dùng binary cũ phải được người vận hành restart đúng phạm vi. SQL timeout vẫn MONITORING/ROOT CAUSE UNKNOWN, xem `sql-category-timeout-observation.md`; không tăng timeout hoặc reset lịch để che lỗi.

Scheduler là tiến trình console riêng trong `src/Worker`, chỉ chạy job xuất bản. Không chạy crawl, gửi email hoặc xử lý video. SQL Server lưu lịch, lease và attempts; dừng web/trình duyệt không mất lịch. Tài khoản Dev hiện có không tự được cấp `content.schedule`. Quản trị viên có thẩm quyền phải gán quyền hẹn giờ cùng quyền xuất bản thích hợp qua quy trình cấp quyền được ghi trong hướng dẫn quản trị.

## Khởi chạy và dừng

Chạy trong thư mục gốc workspace bằng PowerShell:

```powershell
rtk proxy powershell -NoProfile -File scripts/run-worker-dev.ps1
rtk proxy powershell -NoProfile -File scripts/run-worker-dev.ps1 -NoBuild
```

Lệnh trên dùng đúng `LandPortal_Dev`, kho tệp dự án và Development loopback; không thay `.env`. Nhấn Ctrl+C để dừng. Lease đang xử lý sẽ hết hạn, lịch khác giữ nguyên. Chỉ bật ngoại lệ tải ảnh/PDF chưa quét khi kiểm thử local có chủ đích:

```powershell
rtk proxy powershell -NoProfile -File scripts/run-worker-dev.ps1 -NoBuild -AllowUnscannedMedia
```

Tệp vẫn `NotScanned`; ngoại lệ không chứng minh đã quét mã độc. Worker từ chối startup khi bật cờ này ở Staging/Production hoặc ngoài Development loopback. Không có scanner production là blocker vận hành riêng.

Có thể chạy background local bằng `-Background`. Script in ID descriptor, lưu PID/thời điểm khởi động và log riêng trong `.local/workers`, không cài service hệ thống. Dừng đúng worker bằng ID đó:

```powershell
rtk proxy powershell -NoProfile -File scripts/run-worker-dev.ps1 -NoBuild -Background
rtk proxy powershell -NoProfile -File scripts/stop-worker-dev.ps1 -Id <ID-32-ky-tu-vua-in>
```

Script dừng xác minh PID chưa bị tái sử dụng, đường dẫn executable, command line và DLL thuộc project. Nếu khác, script từ chối; không tìm/diệt tiến trình theo tên chung. `-TestProfile` chỉ dùng `LandPortal_Test` và kho Test riêng. `-Once` chạy một lượt foreground rồi thoát, phù hợp smoke. Đây không phải công cụ ép job chưa đến hạn.

## Nhập giờ và trạng thái

Admin `/admin/schedules` hiển thị thời gian Việt Nam UTC+07:00, revision cụ thể, nhánh trực tiếp/qua duyệt, chủ thể, số attempts và lý do an toàn. Ánh xạ host Windows dùng `SE Asia Standard Time`, Linux dùng `Asia/Ho_Chi_Minh`; startup xác minh base UTC offset +07:00 và test kiểm tra ánh xạ host thực. Lưu UTC trong SQL; không phụ thuộc timezone máy. Ngày của văn bản dạng date-only không đổi qua datetime. Mỗi nội dung có tối đa một lịch hoạt động bằng unique filtered index SQL.

Mở nội dung đã lưu và dùng `/admin/schedules/edit?contentId=<GUID>`. Chọn thời điểm tương lai và revision hiện tại. Lịch lưu hash của revision và căn cứ approval; không chuyển sang bản nháp mới hơn. Đổi lịch tạo lịch mới, vô hiệu lịch cũ trong cùng transaction; lịch Completed không được reset Pending. Hủy lịch không gỡ bài public. Nếu publish đã commit, UI trả trạng thái đã thực hiện.

Pending chờ đến hạn; Running có lease; Completed có hiệu ứng đã commit; Canceled do quản trị; NeedsReview do thay revision, approval/quyền không hợp lệ, quá hạn hoặc media; Failed do hết retry lỗi SQL. Xử lý NeedsReview/Failed bằng kiểm tra nội dung, lưu/duyệt lại nếu cần rồi đặt lịch tương lai rõ ràng. Không sửa trực tiếp bảng SQL hoặc ép trạng thái.

## Chính sách thực thi và sự cố

Mặc định local polling 5 giây, lease 60 giây, chạy bù tối đa 15 phút và tối đa 4 attempts lỗi SQL. Các ngưỡng hợp lệ lấy từ `Scheduler__PollSeconds` (1–60), `Scheduler__LeaseSeconds` (15–600), `Scheduler__CatchUpMinutes` (1–1440), `Scheduler__MaxAttempts` (1–10) qua môi trường tiến trình. Đây là quyết định ứng dụng local, chưa phải cam kết SLA cơ quan. Backoff SQL là 2/4/8… giây, tối đa 60 giây. Validation/quyền không retry tự động.

Worker claim nguyên tử bằng SQL UPDATE/OUTPUT dưới transaction ReadCommitted và lock hints đã được test trên SQL Test. Job được reclaim khi lease hết hạn, token mới thay token cũ. Commit xuất bản kiểm tra lại token/generation/lease và quyền người yêu cầu; nhánh qua duyệt kiểm tra lại cả người duyệt còn hoạt động/quyền duyệt. Transaction Serializable khóa Content trước Job, dùng workflow chung để cập nhật published pointer, public media, command idempotency, audit và Completed cùng transaction. Sửa/gỡ/trả bài/khôi phục/publish thủ công vô hiệu lịch cùng transaction. Worker không dùng tài khoản nghiệp vụ toàn quyền.

Worker chết trước commit: transaction rollback; lease hết hạn cho phép retry. Chết sau commit trước acknowledgement: Completed và publish command trong SQL là căn cứ đối chiếu, không đăng/audit nghiệp vụ lại. Attempts kỹ thuật giữ riêng; attempt hết lease ghi LeaseExpired. Không tuyên bố toàn hệ exactly-once. Khi SQL mất kết nối, log chỉ loại lỗi; lease/jobs vẫn SQL và không bị reset khi restart. Nếu lease hết hạn ngay trước commit, worker cũ bị từ chối.

Heartbeat SQL cập nhật mỗi vòng. Admin chỉ báo heartbeat gần đây (30 giây), không hứa đăng khi worker offline. Public health không chứa SQL/path/tài khoản. Public projection hiện truy vấn SQL theo từng request, `no-store`, không có cache nội dung giữa các request; do đó publish worker được request tiếp theo nhìn thấy và media/public search luôn lọc live pointer/IsPublic. Banner hết hạn được lọc theo giờ trong query cấu hình.

## Đóng gói

```powershell
rtk proxy powershell -NoProfile -File scripts/publish-worker.ps1
```

Gói local ở `.local/publish/phase2b/worker`; không chứa `.env`, kho media/snapshot/key. Lệnh console thật sau khi nạp cấu hình tiến trình đúng đích là `.local/dotnet/dotnet.exe .local/publish/phase2b/worker/Worker.dll`. Không tự cài Windows Service, scheduled task, IIS, firewall, DNS hoặc deploy. Bằng chứng test/gói/khởi động thực ghi trong báo cáo 2B; tài liệu này không tự là bằng chứng PASS hay PRODUCTION READY.
