# Phase 3B — ánh xạ URL đề xuất

**85 alias của40 danh tính**, target `https://gdla.gov.vn/bai-viet/{CMSId}`. Đây là đề xuất chuẩn bị cutover, không đổiDNS/domain thật. JSON/CSV từng URL: `.local/evidence/phase3b/phase3b-20261019T020204Z-5170f87b/url-mapping.json` và `url-mapping.csv`.

Sáu alias của3 bài mới đã được import SQLTest và kiểm HTTP301 một bước tới bài200 bằng loopback, không gọi gdla/qldd. Các alias khác giữ trạng thái PROPOSED_RECHECK_ON_APPROVED_TARGET; có ExistingDevCmsId riêng và chưa chắc alias mới đã được apply. Với bài mới, TestCmsId là ID trên Test, không tự cam kết ID sẽ giống Dev sau batch được duyệt. Phải tạo mapping lại từ kết quả Commit Dev được phép trước cutover.

Không map về home, không gộp vì trùngtitle. Unique URL hash phải so sánh chuỗi gốc, khác nội dung bị chặn; canonical mới không redirect ngược vào alias cũ. Quarantined/draft/unpublished không nhận redirect công khai. Media dùng route local `/media/{id}`, giữ URL nguồn trong provenance; không redirect media chưa quét hoặc trỏ ra hotlink.

| SourceIdentity | Alias Test đã kiểm | Trang Test |
|---|---:|---|
| Article:3666 | 2 | /bai-viet/4f6daf9f-8013-4f6d-aaff-fdbc8c061032 |
| Article:3670 | 2 | /bai-viet/fdfd0665-0843-48aa-b5f1-6170bce22d08 |
| Article:3673 | 2 | /bai-viet/3a5458f4-b1c6-4158-86ea-c8ed36d27d1a |

Phải recheck toàn bộ mapping trên target sau Apply, xác minh mã301/200/404, link nội bộ/tệp và vòng lặp trước nghiệm thu Production. Chưa có bằng chứng triển khai redirect trên domain thật.
