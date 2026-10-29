# Backlog theo giai đoạn

Hiện tại **2A và 2B PASS LOCAL**, tiếp tục nền tảng0–1 đã có. Bảng dưới giữ lịch sử phân kỳ ban đầu; kết quả2B tại `phase-2b-report.md`, `PROJECT_STATE.md` và `acceptance-matrix.md`. Các mục2C/2D/Giai đoạn3–4 và quyền vận hành chưa cấp vẫn còn việc; CHƯA PRODUCTION READY.

| Giai đoạn | Công việc | Điều kiện hoàn thành |
|---|---|---|
| 0 | Khảo sát workspace/mạng/SQL; inventory tất cả hạng mục và phân trang đại diện | Bằng chứng snapshot/URL/điểm chưa xác minh; không tuyên bố tổng nguồn |
| 0 | Solution MVC/EF SQL Server, cấu hình, schema và scripts | Build thật, migration rõ đích; không migrate lúc web startup |
| 1 | Lấy mẫu 30–50 nội dung thật, tải ảnh/PDF và lưu provenance | Báo số phát hiện/nhập/tài nguyên/ngoại lệ; rerun không trùng |
| 1 | Public trang chủ/chuyên mục/bài/văn bản/tìm kiếm | Nội dung từ SQL; tài nguyên local hoạt động không phụ thuộc hotlink |
| 1 | Identity/admin soạn nháp/gửi duyệt/đăng theo quyền ban đầu | Có luồng E2E; bản nháp không thay bản công khai; endpoint kiểm quyền |
| 1 | Test SQL và UI desktop/mobile | Kết quả thật, ảnh chụp và danh sách NOT RUN/BLOCKED |
| 2 | CMS đầy đủ: autosave/preview/duplicate/trả duyệt/hẹn giờ/gỡ/khôi phục | Kiểm quyền, audit, revision và concurrency từng luồng |
| 2 | Comment/câu hỏi/liên hệ/góp ý có kiểm duyệt, spam/rate limit/PII | Pending không công khai; kiểm hạn dự thảo; mail sink/retry |
| 2 | Video upload chunk từ máy biên tập, resume, scan, worker FFmpeg | Chữ ký/codec thật; retry/restart; Range/seek; không Ready giả |
| 2 | Menu/homepage/banner/liên kết/tài khoản/cấu hình đầy đủ | Quyền riêng cho từng chức năng; audit không sửa bởi editor |
| 3 | Crawler sâu, tất cả phân trang/loại nguồn/alias/tệp/video | Tổng nguồn đối chứng hoặc nêu rõ chưa biết; không coi queue rỗng là đầy đủ |
| 3 | Đồng bộ thay đổi và cutover bền vững | Không ghi đè bản editor; cờ SQL chặn sau restart; đối soát độc lập |
| 4 | UAT/an ninh/WCAG/performance/backup-restore/deploy rollback | Kiểm thử hệ đích, SQL+media+keys+config nhất quán; chốt hạ tầng |

Các mục giai đoạn 2–4 chưa triển khai không phải lỗi dữ liệu được che bởi bản mẫu; phải giữ trên ma trận chức năng và kế hoạch tiếp tục. Không public website, đổi DNS, push Git hoặc gửi email thật trong lượt này.

## Phân kỳ theo đặc tả CMS 2A

- 2A: CMS bài viết, editor mẫu, ảnh/PDF, autosave, preview, phiên bản, quyền, gửi duyệt/duyệt/trả bài/đăng trực tiếp/gỡ/thùng rác/khôi phục/nhân bản. Kết quả nghiệm thu thực tại `phase-2a-report.md`; không coi dòng Giai đoạn 2 tổng hợp ở trên là đã hoàn thành.
- 2B: quản trị mẫu nội dung còn lại/tài khoản/nhóm/menu/banner/liên kết/trang chủ/search/worker lịch đã PASS LOCAL. Không bao gồm scanner, source coverage toàn kho, analytics mới hoặc hạng mục nguồn chưa xác minh. Quyền vận hành mới vẫn chưa được cấp; dùng công cụ grant dry-run khi có yêu cầu có thẩm quyền.
- 2C: câu hỏi, liên hệ, **bình luận dưới bài** và góp ý dự thảo; kiểm duyệt, quyền riêng tư, chống spam, outbox email.
- 2D: **video tải trực tiếp từ máy biên tập**, resume/xử lý/poster/bản phát web/worker; không thay bằng URL YouTube.
- Giai đoạn 3: toàn bộ dữ liệu nguồn/các phân trang/alias/tệp và đối soát/cutover; không yêu cầu nhập tay từng bài. Chỉ đồng bộ trong xây dựng/chuyển giao, dừng sau cutover.
- Giai đoạn 4: UAT, môi trường triển khai thực, scanner, backup-restore, an ninh/accessibility/performance và bàn giao. Dự án vẫn CHƯA PRODUCTION READY.

Theo dõi kỹ thuật trước tải đại diện: SQL category từng timeout trong failed UI runs, full cuối2B đã PASS sau simplification query nhưng chưa chứng minh nguyên nhân. Giữ log/plan/DMV và điều tra nếu tái xuất hiện; không tăng timeout hoặc bỏ assertion để che. Hash utility hiện lưu manifest không sắp thứ tự rg: dùng so sánh path/SHA và canonical sorted hash như `final-code-verification.json` khi chứng minh code state; không suy từ raw traversal hash khác nhau là code thực đổi.
