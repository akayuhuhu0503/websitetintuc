# NukeViet — mapping chuẩn bị trong Phase3H

SRS1.0 xác định gdla.gov.vn là nguồn chính thức thứ hai. Quyền thu thập công khai đã được người dùng xác nhận; không có blocker mặc định vì thiếu file approval crawl riêng. Bảng này đối chiếu code hiện có với phần còn thiếu, không phải quyết định mapping đã duyệt hoặc dữ liệu nhập thật.

| Thành phần nguồn | Cơ chế hiện có | Đích dự kiến / điều kiện còn thiếu |
|---|---|---|
| `/{vi/}news/...-ID.html` | `MigrationSourceIdentityPolicy.FromUrl`, namespace nguồn/module/loại/id | Source=`gdla.gov.vn`, SourceId=`module:id`; không dùng ID số đơn thuần hoặc namespace QLDD |
| canonical và tiêu đề | canonical phải cùng identity, `h1` không rỗng | Cần snapshot thật chứng minh template; không tự gán URL canonical khi thiếu |
| thân bài | `#news-bodyhtml`, text không rỗng, raw SHA | Revision nguồn riêng; body-match chỉ đề nghị so sánh, không tự merge |
| ngày/tác giả/tóm tắt | News preview hiện chưa xuất đủ các trường này | Cần mẫu HTML/export nghiệp vụ và parser được kiểm chứng; không lấy thời điểm crawl thay ngày đăng |
| chuyên mục NukeViet | Chưa có bảng mapping nghiệp vụ đã duyệt từ catalog thật | Đối chiếu catalog gốc→chuyên mục đích, giữ14 chuyên mục cũ; không suy từ tiêu đề |
| module văn bản/dự thảo | Route chưa có adapter bằng chứng thật trả `NEEDS_DOCUMENT_ADAPTER_OR_ROUTE_REVIEW` | Cần schema/mẫu thật, giữ số hiệu, cơ quan, loại văn bản, ngày và file; không áp selector news cho tài liệu |
| ảnh/PDF/Word/Excel/video | `NukeVietBundle` kiểm input raw/file SHA, sao quarantine, không tin nhãn Clean từ input | Adapter format và scanner đúng policy; không hotlink, không thay video thiếu bằng YouTube của bài khác |
| URL cũ | Giữ canonical/route/source identity ở private preview | Alias đích/301 chỉ sau mapping và quyết định import; chưa sửa website hoặc DNS |
| dữ liệu SQL/CMS | Bundle là offline preparation, không tự ghi CMS | SQL write adapter nguồn NukeViet và nghiệm thu nguồn thật còn thiếu; không đổi nhãn fixture thành export thật |

Code tham chiếu: `src/MigrationCli/SourceIdentity.cs`, `Phase3FCommand.cs` (`NukeVietBundle`), `Phase3DCommand.cs`. Trong Phase3H không crawl lại, không chỉnh website NukeViet hiện hành, không tạo snapshot/export giả. Lỗi HTTPS443 trong báo cáo3F là quan sát lịch sử, chưa kiểm tra lại trong lượt này. Các batch QLDD có receipt thật hoạt động độc lập với các điểm chờ NukeViet.

## Kiểm tra lại Phase3J21/10

DNS trả45.120.229.41;TCP443 vàHTTP80 đều ConnectionRefused10061. TLS chưa bắt tay,không kết luận certificate sai;2GETrobots không nhậnHTTPresponse.0realcontent/0SQLimport/0officialexport được cung cấp. Phần mapping nguồn→đích vẫn dự thảo chưa có catalog thật;không tạo dòng mapping giả hoặc đổi adapter news để ép đọc văn bản khi chưa có mẫu. Source identity namespace/canonical/rawSHA/privatebundle/cross-source suggestions hiện có giữ nguyên. Khi khôi phục kết nối hoặc nhận export, kiểm chứng template và metadata thực,review dedupe theo body+ngày+tác giả;duyệt mapping trướcDevimport.
