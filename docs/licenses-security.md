# Kiểm tra Web Giai đoạn 0–1

## Cập nhật Giai đoạn 2A — 12/10/2026

Thành phần soạn thảo chuyển riêng từ Quill core 2.0.3 sang **Jodit 4.17.3 (MIT)**. Lý do là giới hạn đã kiểm chứng của bản trước: bảng nguồn phải chỉ đọc và figure/chú thích không được mô hình Quill core bảo toàn. Jodit chỉnh sửa HTML table/figure trực tiếp; không thay MVC, TypeScript, các route hay hệ thiết kế. Không dùng bản PRO, plugin trả phí hoặc API key. Package/lock pin đúng 4.17.3; bundle và CSS ES2021 được self-host tại `wwwroot/vendor/jodit`, kèm nguyên bản MIT LICENSE.txt. [Nguồn Jodit chính thức](https://github.com/xdan/jodit), [giấy phép](https://github.com/xdan/jodit/blob/main/LICENSE.txt). Tên/phiên bản/integrity/giấy phép cũng được kiểm tra trong npm registry và package đã tải. Các ghi nhận Quill ở dưới là lịch sử Giai đoạn 0–1, không mô tả editor đang chạy.

Chỉ mở toolbar soạn văn bản/bảng/liên kết; ẩn tính năng nhập mã nguồn, tải file từ URL, ảnh/video ngoài, iframe và dịch vụ bên ngoài. Ảnh có chú thích/PDF dùng picker và upload riêng của dự án. Dán Word làm sạch định dạng; không nhận file Word để tự biến thành bài. Máy chủ sanitize trước khi lưu và trước khi echo lỗi; chặn script, event handler, scheme nguy hiểm và hình bên ngoài. Preview rewrite tài nguyên sang endpoint có kiểm quyền, đặt no-store. Các query public đọc metadata/chuyên mục/ảnh từ đúng PublishedRevision; quyền nhạy cảm kiểm lại user/role claims hiện thời trong SQL, không dựa vào cookie quyền cũ.

`rtk npm run build` và Web build đã chạy lại sau thay đổi: 0 warning/error. `npm audit` sau gỡ dependency Quill báo **0 vulnerabilities**, gồm đúng Jodit và TypeScript đang pin; đây là kết quả feed tại thời điểm kiểm tra, không phải chứng nhận an ninh production. Kết quả kiểm thử SQL/HTTP/UI, ảnh chụp và phần NOT RUN được ghi riêng trong phase-2a-report/acceptance-matrix.

Các bộ kiểm tra ảnh/PDF SkiaSharp và PdfPig thực hiện decode/parse định dạng, **không quét mã độc**. Native notices và license đi cùng gói publish. Upload mặc định bị chặn khi thiếu scanner; chỉ có ngoại lệ Development tường minh trên loopback, ScanStatus vẫn NotScanned. Production scanner, HTTPS/ACL và triển khai Linux vẫn chưa được nghiệm thu.

Ngày thực hiện: 12/10/2026. Đây là rà soát code và phụ thuộc cho bản chạy thử; không thay nghiệm thu bảo mật production.

## Phụ thuộc editor

Quill 2.0.3 (BSD-3-Clause) và TypeScript 5.9.3 (Apache-2.0) được pin trong `src/Web/package.json` và `package-lock.json`. Bundle, CSS và LICENSE được sao chép từ package cục bộ vào `wwwroot/vendor/quill` bằng `npm run build`; trang public không tải editor hoặc CDN. [Giấy phép Quill chính thức](https://github.com/slab/quill/blob/v2.0.3/LICENSE). `src/Web/THIRD_PARTY_NOTICES.md` giữ thông báo giấy phép và giới hạn sử dụng.

Đã chạy `rtk npm audit --json` trong src/Web: **1 low, 0 moderate/high/critical**, GHSA-v3m3-f69x-jf25 / CVE-2025-15056, ảnh hưởng chức năng HTML export của Quill 2.0.3. [Advisory đã đối chiếu](https://github.com/advisories/GHSA-v3m3-f69x-jf25) chưa có bản vá stable; `npm view quill version` trả 2.0.3. Không tự dùng `npm audit fix --force` hoặc bản experimental để ẩn vấn đề.

Code không gọi getSemanticHTML hay mở chức năng export HTML. Thân bài gửi từ editor phải qua HtmlPolicy.Clean trước lưu trong WorkflowService. Nội dung được trả lại khi validation lỗi cũng được sanitize trước khi đưa vào DOM editor. CSP chỉ cho script cùng origin, chặn inline script. Đây là các biện pháp giảm rủi ro trên đường sử dụng đã triển khai, không chứng minh thư viện hết lỗ hổng. **Cần đánh giá lại editor hoặc áp bản vá upstream trước production.**

Quill core không giữ đầy đủ mô hình bảng/figure nguồn. Bài chứa bảng được hiển thị thân bài chỉ đọc với thông báo rõ, giữ nguyên nội dung khi sửa metadata. Với bài không có bảng, thao tác chỉ sửa tiêu đề/mô tả/tác giả giữ HTML nguồn; chỉ chuyển sang HTML editor khi người dùng thực sự sửa thân bài. Soạn bảng, upload media và video mới thuộc Giai đoạn 2.

## Rà soát code đã thực hiện

| Đường xử lý | Kết quả rà soát | Phần kiểm chứng độc lập |
|---|---|---|
| Admin | Identity/cookie, policy claim permission, không registration; cookie HttpOnly/SameSite Strict, lockout và rate-limit login; redirect sau đăng nhập chỉ local | QA HTTP thực trên SQL test riêng; xem acceptance-matrix |
| POST quản trị | Global antiforgery, intent Save/Submit/Publish allowlist; kiểm quyền server trước lưu; edit theo owner hoặc EditAll; không tin CanPublish/CanSubmit trong form | QA trực tiếp forged publish, CSRF, stale rowversion |
| Public content | Đọc PublishedRevisionId, không đọc CurrentRevisionId; Draft/PendingReview không làm lộ phiên bản đang sửa; loại Unpublished/Archived bị loại | QA draft isolation/publish |
| Media | Chỉ asset có quan hệ revision đang công khai; chặn quarantined và HTML/SVG; resolve đường dẫn trong MediaRoot, attachment/download, Range; kho .local không static | QA draft/private UUID, nội dung thật local |
| Error/health | Lỗi public không stack trace; Error view không phụ thuộc menu SQL nên vẫn hiển thị khi database lỗi; readiness chỉ status | HTTP runtime checks do root/QA |
| Legacy URL | Catchall .htm và .aspx, source domain cố định; hash và URL gốc phải khớp alias SQL; đích local và published mới trả 301 | QA HTTP canonical .aspx?ItemID thật trả301 đúng bản công khai |
| Query | Từ khóa normalize tham số EF; page/page size giới hạn; projection card bỏ SearchText/thân bài sau lọc SQL; menu đủ 14 và các hạng mục phụ có lối vào riêng | QA search/phan trang/UI |
| Footer/date | Địa chỉ/điện thoại/email từ footer nguồn xác minh; ngày ban hành/góp ý không đổi tên thành ngày đăng | Source inventory và parser fixture |

Đã chạy `rtk npm run build`: PASS. Build Web đã từng PASS, 0 warning/0 error; lượt build khi Web process còn chạy bị Windows chặn copy DLL, sau đó root dừng/build/restart có phối hợp. Kết quả build/test cuối cùng ghi tại PROJECT_STATE và acceptance-matrix.

## Giới hạn còn lại

- Asset nguồn trong local có ScanStatus NotScanned: chưa có máy quét mã độc production. Không coi đây là chứng minh tệp sạch.
- HTTP loopback được dùng trong môi trường local; HTTPS, kiểm soát tài khoản đặc quyền, ACL/backup/restore keys và media, revoke phiên, UAT và kiểm tra bảo mật production chưa nghiệm thu.
- Chưa triển khai thao tác duyệt độc lập/trả bài/gỡ/hẹn giờ/khôi phục; Giai đoạn 1 chỉ lưu nháp/gửi duyệt/xuất bản theo quyền. Trên UI không có nút giả cho các phần này.
- Bình luận, hỏi đáp, liên hệ, góp ý và upload video mới chưa mở nhận dữ liệu; chỉ có trang giải thích trạng thái thật, không thông báo backend/email thành công giả.
