# Cập nhật approved Batch9 / Article3075

22/10/2026:9bài đãApplyDev theoexacte02a8cb2/6e2374..., không thuộc18case dưới. Article3075 trong18case đã sửa parser,282cacheitemschỉ1affected, native5Clean143,Test+1bài/1revision/5ảnh/2alias,repeatAlreadyCommitted/HTTP8PASS;**chưaApplyDev3075**.17casekhácgiữchặn. PDF3205/48MiB vẫnBLOCKED_POLICY_APPROVAL, không tảibody/khôngbiếtSHA/chưavalidate/scan. Bảng dưới là phân loại gốc trước sửa3075; xem [báo cáo tiếp nối](phase-3l-approved-batch9-report.md).

---

# Phase3L — giới hạn PDF nguồn và18canonical

**Chưa tăng giới hạn hoặc tải PDF3205.** Giữ byte/hash nguồn, scanner, ACL và quyền public.39.192.764byte là Content-Length từ HEAD thật3K, không phải số byte đã tải trong3L. Lượt3L dùng cache,0requestQLDD/GDLA mới.

| Lớp | Bằng chứng / ảnh hưởng |
|---|---|
| Crawler/response | `InventoryProcessing.ProcessAsync`: file tối đa32MiB, còn bị giới hạn bởi work-order/cumulative byte budget. Đây là cửa chặn đầu tiên của3205, trước khi nhận body đầy đủ |
| Web upload/API | Không nằm trên đường lấy PDF nguồn này. Tăng request body limit Web/API không giải quyết lỗi đã ghi |
| Media validation | `MediaUploadOptions.MaxPdfBytes` mặc định32MiB; cấu hình chung có upper bound64MiB. Không thay cấu hình chung để vượt riêng3205 |
| Import bundle | `Phase3FCommand` đọc private file tối đa32MiB; cần policy nguồn đồng bộ, không chỉ tải được file |
| Recovery inventory | `MediaRecovery.PublicBatchInventoryAsync` chỉ nhận capture hợp lệ≤32MiB và whole-item projection; không thể khai lại bytes nhỏ hơn |
| Scanner | `MediaRecovery.Scanner` và `InventoryReport` tạo scanner với cap32MiB. Engine hỗ trợ giới hạn lớn hơn không có nghĩa luồng này được phép vượt32MiB |
| Storage/proxy | Chưa phải nguyên nhân được chứng minh: bị từ chối tại Content-Length trước download đầy đủ. Không có bằng chứng lỗi dung lượng, IIS hoặc proxy trong trường hợp này |

**Đề xuất chờ duyệt:** policy riêng cho PDF nhập nguồn, cap48MiB/file, chỉ URL/identity công khai được allowlist trong work order có tổng byte/request budget hữu hạn. Giữ Web/editor upload hiện hành. Cần version policy mới xuyên crawler→private storage→PDF format/pages/signature validation→scanner cùngcap→proof đúngSHA/engine/definitions/time→SourceBatch/ACL/publication. Không re-encode/cắt byte PDF đã ký, không lấy compressed file khác thay bản gốc khi chưa review nội dung/chữ ký. Không bật cap mới chỉ bằng thêm biến môi trường; policy/hash/plan/proof thay đổi phải được review và manifest mới khi cần. Đây **không phải phê duyệt đã có**, chưa viết đường bypass32MiB.

##18canonical: phân loại theo nguyên nhân chính, chưa đóng trường hợp nào

Tất cả lấy từ `canonical27-disposition.json`3K và cache hiện có; bảng này không cộng với34Dev-gap hoặc52typed rows.

| SourceIdentity | Nguyên nhân chính / hành động |
|---|---|
| Article3458 | Có1PDF thiết yếu không đạt định dạng; cần bản gốc hợp lệ, không strip/giảPDF |
| Article3075 | **Parser `srcset` cắt tại khoảng trắng:**5ảnh thật đã cache nhưng phát sinh thêm URL cụt `Small_%C4%90BQH`. Raw `<img src>` và3srcset candidates cùng filename đầy đủ có khoảng trắng +descriptor800w/1024w/1920w. Cần parser fix/fixture đúng raw, checkpoint mới có provenance; chưa xóa URL khỏi staging để ép import |
| Article3194 | PDF gốc vượt response budget; PDF compressed khác có cache không tự thay bản gốc/annex |
| Article1721 | Ảnh từmonre.gov.vn ngoài resource-host allowlist hiện hành |
| Article3238 | Một PDF phụ lục thiết yếu vượt budget; giữ cácPDF khác đã tải |
| Article3276 | Ảnh từbtnmt.1cdn.vn ngoài allowlist |
| Article3297 | Ảnh từbtnmt.1cdn.vn ngoài allowlist |
| Article3296 | Ảnh từbtnmt.1cdn.vn ngoài allowlist |
| Article3303 | Ảnh từbtnmt.1cdn.vn ngoài allowlist |
| Article1599 | URL ảnh ngoài allowlist và các bản internal cache; cần chứng minh mapping/ownership, không tự coi filename giống là cùngbyte |
| Article1465 | Ảnh từmoitruongachau.com ngoài allowlist |
| Article3623 | Ảnh vượt giới hạn pixel/frame; cần xử lý nguồn/derivative có policy, không tự tăng cap toàn hệ thống |
| Article3301 | Ảnh từbtnmt.1cdn.vn ngoài allowlist |
| Article3298 | Ảnh từbtnmt.1cdn.vn ngoài allowlist |
| Article3690 | Có ảnh internal cache, còn URL media-dlrpd.monre.gov.vn ngoài allowlist; mapping phải có bằng chứng |
| LegalDocument240 | PDF vượt response budget; chưa có byte đầy đủ để validate/scan |
| Article1716 | URL ảnh external cùng các bản internal cache; cần xác minh mapping, không bỏ required asset |
| Article3205 |39.192.764byte>32MiB, phương án48MiB riêng ở trên chờ duyệt |

Nguyên nhân chính loại trừ nhau trong bảng:11host/mapping,4oversizePDF,1PDF khônghợplệ,1pixel,1srcset. Đây không phải thống kê số lỗi tài nguyên độc lập; một bài có nhiều URL/lỗi. **0/18 nội dung được nhập thêm trên Test hoặc Dev3L.** Findings và rawSHA cho3075 được lưu riêng trong run; không gọi sửa parser hoặc importPASS khi chưa sửa/kiểm chứng.3parser identities lịch sử Article2024/DraftDocument2024/DraftDocument3 vẫn là tập riêng và chưa có bằng chứng nguồn mới.

Quyền crawl công khaiQLDD/GDLA đã được người dùng xác nhận. Các vấn đề trên là format, provenance và policy tài nguyên cụ thể; không khôi phục blocker mặc định “thiếu approval public crawl”. Không tự mở host bên thứ ba, tạo optional-image review thay nghiệp vụ hoặc thay asset nguy hiểm bằng ảnh giả/hotlink.
