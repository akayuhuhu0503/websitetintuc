# Tiếp nhận dữ liệu NukeViet — phương án khi mạng bị chặn

Giữ adapter hiện có; chưa có dữ liệu thật để sửa parser. Không dùng technical fixture làm export nghiệp vụ hoặc tự điền catalog nguồn. Public crawl hai nguồn đã được cho phép; export CSDL gốc, credentials và mapping/Apply là quyền riêng.

Đơn vị quản trị gdla cung cấp bản xuất theo module công khai hoặc HTML snapshot + kho uploads, kèm phiên bản NukeViet/module, thời điểm, phạm vi, encoding/timezone và người bàn giao. Ưu tiên export đã loại accounts/password/session/PII/private drafts; không cần đưa nguyên database chứa bí mật vào workspace để bắt đầu adapter news. Có manifest path/size/SHA256, số bản ghi từng module/chuyên mục, mô tả URL cũ/canonical, ngày đăng/tác giả/nguồn, quan hệ file/video và quyền công khai.

Gói được nhận tại thư mục riêng `.local` có ACL, kiểm hash/đường dẫn traversal/reparse, giữ raw bất biến, không chạy script/SQL do nguồn gửi. Quarantine và scanner đúng policy trước preview/import. Nếu cung cấp database backup thật, cần thẩm quyền, target recovery riêng và kế hoạch bảo vệ; không restore đè Dev/Test hoặc chạy dump SQL tùy tiện.

| Trường mapping cần nghiệp vụ xác nhận | Giá trị khi chưa có export |
|---|---|
| Source/module/local ID/category ID/name/parent/URL | UNKNOWN — chờ catalog thật |
| Target category ID/name | Chọn từ danh mục hiện hành sau đối chiếu, không đoán theo title |
| Tin/bài/văn bản/dự thảo/trang/video | Giữ loại và metadata nguồn; document adapter cần mẫu thật |
| Trùng giữa hai nguồn | Body/date/author/file evidence; title giống chỉ là gợi ý, không tự merge |
| Canonical/alias301 | Namespace gdla/module/type/id tách QLDD, từng URL và review ownership |
| PII/publication/file licence | Chỉ dữ liệu được quyền công khai; private không vào public inventory |
| Người duyệt/time/evidence | Chưa có; bảng này không phải mapping đã được duyệt |

Sau khi kết nối được từ mạng do đơn vị cho phép, probe DNS/TCP/TLS/robots hữu hạn rồi lấy vài mẫu đúng module. Không vòng qua VPN/proxy không được phép, không tắt TLS và không retry vô hạn. Nếu chưa có mạng hoặc export: **BLOCKED_BY_NETWORK / EXPORT_NOT_PROVIDED**,0 dữ liệu NukeViet thật được chấp nhận.
