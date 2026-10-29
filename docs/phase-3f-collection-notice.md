# Ghi nhận phạm vi thu thập công khai — Phase 3F

**ĐÃ ĐƯỢC NGƯỜI DÙNG XÁC NHẬN TRONG PHIÊN CHAT.** Biểu mẫu này phục vụ bàn giao nội bộ; không tạo thêm gate yêu cầu ký trước khi chạy phần đã được cho phép.

- Nguồn: `qldd.mae.gov.vn` (SharePoint), `gdla.gov.vn` hiện tại (NukeViet), ảnh/tệp công khai được nội dung hai nguồn tham chiếu và qua allowlist an toàn.
- Phạm vi: tin bài, thông báo, văn bản, dự thảo, giới thiệu, metadata/video và tài nguyên công khai; chuẩn hóa, private staging, quét và nhập SQLServer Test. Không đọc CSDL gốc.
- Người xác nhận: chủ dự án/người dùng trong phiên Phase3F; họ tên/chức danh để bổ sung vào hồ sơ đơn vị: __________. Không gán Codex là người phê duyệt nghiệp vụ.
- Người vận hành chịu trách nhiệm theo dõi: __________; run hiện hành `phase3f-20261019T070725Z-fa27c9d0` ghi lại từng batch/checkpoint/hash/lỗi. Máy đọc tham chiếu bản ghi xác nhận và SHA, không phải file approval tự ký.
- Tốc độ: một kết nối, nghỉ ít nhất2giây; không song song nguồn để lách giới hạn. Mỗi batch20detail, trần120GET/100asset/600giây/256MiB; trần phiên2000GET/2GiB, lease vận hành4giờ từ thời điểm ghi nhận. Lịch tiếp tục: theo checkpoint trong `PROJECT_STATE`; hết trần phải đánh giá lại khối lượng/tình trạng nguồn trước đặt cửa sổ vận hành tiếp.
- Lỗi: lưu response/status/hash và URL private; tôn trọng robots/cutover, Retry-After/backoff, dừng khi truy cập bị chặn hoặc nguồn không chịu tải. Không vượt đăng nhập/CAPTCHA, không retry vô hạn, không tăng timeout SQL để che lỗi, không gọi COMMIT lại khi chưa đối soát.
- Đầu ra: inventory/snapshot/file private, phân loại lỗi, báo cáo scanner thật, plan/import/verify/Test; không hotlink, ảnh giả hoặc gắn Clean thủ công.
- Ngoài phạm vi quyền này: Apply Dev, scan/promote53 Dev, quyền SQL/Identity, sửa.env, DNS, Production, email thật, truy cập nội dung riêng tư và thay đổi website nguồn.

Đối chiếu [runbook3F](phase-3f-runbook.md). Nếu quy trình đơn vị có yêu cầu bổ sung về vận hành, ghi rõ văn bản/điều kiện cụ thể; không dùng một placeholder approval chưa tồn tại để phủ nhận xác nhận của người dùng.
