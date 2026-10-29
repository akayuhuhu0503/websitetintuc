# UI V05-Finish — bằng chứng thật trên SQL Test

Run sau sửa cuối: `v05-finish-tests-20261015T134049Z-dc3d22a1`. Headless **Edge**, viewport1440 và390 cho vùng Finish; các vùng2A/2B giữ kiểm tra360/390/768/1024/1440. Đây là trình duyệt thật thao tác với web/SQL/storage Test; không chứng nhận thiết bị vật lý, Safari/Firefox, WCAG hoặc UAT nghiệp vụ.

[Báo cáo UI Finish](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-http/ui/ui-report.json):10 kiểm tra trang ở2 kích thước;3 thao tác: nhập CAPTCHA và login thật, chỉnh trường preview khi parser báo sai mẫu, mở menu mobile và nhóm FR17. Không cộng các page/action vào123 nhóm full runner. Kết quả full/exit/hash cuối xem [báo cáo nghiệm thu](phase-v05-finish-report.md).

| Màn hình / bằng chứng | Nhận xét |
|---|---|
| [Lịch tuần desktop](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-http/ui/weekly-1440.png) | Hiện releasev3 sau khôi phụcv1,2 dòng ô gộp đã đọc, địa điểm đã sửa trong preview, link Word gốc. Năm2097 và chữTechnical/QA là fixture Test, không phải lịch chính thức |
| [Tài liệu desktop](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-http/ui/documents-1440.png) | SQL state/hash/engine, upload thật và trạng thái quarantine, chỉ cho tải/đọc khi đủ điều kiện |
| [Preview lỗi Word mobile](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-http/ui/word-preview-390.png) | Cảnh báo vị trí bảng/dòng tiêu đề; trường lịch có thể sửa, chưa publish. Form14 dòng scaffolding còn dài, chưa tối ưu UX nghiệp vụ |
| [Search hỏi đáp mobile](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-http/ui/search-answers-390.png) | Kết quả chỉ public snapshot, có bộ lọc loại, ngắt identifier dài, không tràn viewport |
| [Menu mobile đang mở](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-http/ui/menu-open-390.png) | Có nhómCông khai thông tin/overview và2 tên đã duyệt; không xuất hiện tên nhóm2 đang chờ PO. Tất cả là cấu hình live SQL Test, chưa tự áp menu Dev |

Đã xem ảnh thực desktop lịch và mobile menu/preview. Bảng lịch mobile cuộn trong vùng bảng, không làm rộng cả viewport; có thể cần cải thiện cách đọc lịch trên thiết bị nhỏ sau khi có Word nghiệp vụ. Screenshot homepage Test chứa fixture QA tích lũy và dữ liệu thật được sao chép từ nguồn, không dùng để tuyên bố website chính thức có nội dung FR17/lịch mới.

Regression liên quan sau sửa gồm editor2A40pagechecks và typed-content14pagechecks/7actions (đơn vị riêng), giữ bản soạn khi hết phiên→CAPTCHA reauth→lưu SQL; site config reflow/draft/live/menu/source14categories; các UI interaction/scanner/video có báo cáo riêng trong run. Các lượt trước đã phát hiện reflow ở search/homepage và helper cũ thiếu đáp án reauth; raw FAIL được giữ, không sửa ảnh để làm PASS.
