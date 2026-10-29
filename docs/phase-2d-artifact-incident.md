# Incident artifact worker legacy trong 2D — PARTIAL

Ngày15/10/2026. Khi chạy regression2D, testcase `2B standalone worker publishes with web stopped and rejects Staging media bypass` vẫn dùng đường cố định `.local/evidence/phase2b/worker-web-off.json`, bỏ qua `LandPortal__EvidenceRoot`. Lượt full2D đã dùng lại đường này. Bản hiện có chứaUTC`2026-10-15T05:09:27.7313584Z`, thuộc lượt `video-tests-20261015T050857Z-bf215fed`, SHA256`d542073a0c56fac17bdfb092471994bca220f0036bd0d84eb83cd75110b49ece`.

Chưa tìm được bản raw trước2D có thể kiểm chứng qua tìm tên tương ứng trong `docs` và `.local/evidence` của workspace; chỉ một tệp hiện hành. Không có hash gốc trong baseline2D để chứng nhận phục hồi. **Không tái tạo JSON bằng các trường từ báo cáo/log rồi gọi đó là raw đã khôi phục.** Bản hiện hành được sao chép nguyên byte vào task2D`legacy-worker-artifact-incident/current-post-overwrite-copy.json`, cùng `incident.json`; đây là bảo quản bản mới, không thay bản gốc đã mất.

Ảnh hưởng: bằng chứng raw lịch sử của testcase worker này không còn đầy đủ; dữ liệu SQL nguồn, ảnh/PDF/snapshot, Identity và.env được đối chiếu độc lập. Incident này **khác** V04rawsmoke2B đãPARTIAL từ trước; không xóa hoặc đổi trạng thái incident cũ.

Đã sửa testcase dùng `TestEvidencePaths.At(workspace,"phase2b")`. Stdout/stderr/ownedPID/mốcUTC/deadline/exit của mỗi processDevelopment/Staging được giữ trong thư mục run kể cả timeout, không tăng deadline30s. Wrapper2D ghi hash các raw legacy phase2a/phase2b/v04/ui trước/sau và báoFAIL nếu códrift. Các lượt sau córaw mới chỉ chứng minh lần chạy mới; không hoàn lại provenance cũ.

Vì yêu cầuD15 cấm ghi đè artifact cũ, trạng thái nghiệm thu **2D tổng thể vẫn PARTIAL cho artifact**, kể cả nếu chức năng/E2E và regression cuối đạt. Không đổi lịch sử FAIL thànhPASS, không cộng các lượt test. Bước tiếp: giữ incident trong bàn giao và đối soát bản backup raw gốc nếu người vận hành có bản bên ngoài workspace; hiện không có bằng chứng phục hồi.
