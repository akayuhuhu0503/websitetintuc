# Kiến trúc CMS — nền tảng 2A và bổ sung 2B

Delta2D: [video/media/revision](video-media-architecture.md) mở rộng association `Video/Subtitle`, upload SQL/private và worker riêng; metadata/caption vẫn theo revision. Không thay workflow2A hoặc scheduler2B. Binary/native engine, gate quét/Ready và Test-only scope xem [runbook](video-worker-runbook.md).

Giữ ASP.NET Core 10 MVC/Razor, EF Core/Identity và SQL Server hiện có. Migration additive `20261012083717_CmsRevisionWorkflow` áp dụng tường minh cho `LandPortal_Test`, đối chiếu bảo toàn, rồi `LandPortal_Dev`; không migrate lúc web khởi động. SQL review nằm tại `evidence/phase2a-migration.sql`. Sao lưu SQL COPY_ONLY/CHECKSUM được VERIFYONLY; chưa thử khôi phục database từ bản sao lưu. Không chạy Down: các bảng mới chứa lịch sử CMS cần giữ.

## Phiên bản và trạng thái

`ContentDraft` cùng `DraftCategories`/`DraftAssets` là vùng làm việc có thể sửa. Autosave chỉ cập nhật vùng này trong trạng thái Draft/Rejected; không tạo lịch sử cho mỗi lần gõ. Lưu tay/gửi duyệt tạo snapshot `ContentRevision` bất biến gồm tiêu đề, mô tả, HTML, tác giả/nguồn, chuyên mục chính/phụ, cover và quan hệ tài nguyên. EF từ chối sửa/xóa revision và quan hệ lịch sử tại chỗ.

`CurrentRevisionId` là snapshot biên tập; `SubmittedRevisionId`, `ApprovedRevisionId` và `ApprovalHash` gắn duyệt với đúng snapshot. `PublishedRevisionId` cùng `IsPublic && !IsTrashed` quyết định truy cập công khai. Mở bản sửa, autosave, gửi duyệt không đổi con trỏ công khai. Query public lấy cả metadata và media từ revision đã xuất bản.

Thứ tự tin mới dùng ngày nguồn nếu có, nếu không dùng ngày xuất bản hệ thống đã biết theo múi giờ Việt Nam; không sửa hoặc tự điền ngày nguồn còn thiếu. Bài CMS mới vì vậy xuất hiện theo ngày đăng hiện tại thay vì đứng dưới mọi bài nguồn có ngày. Nếu cả hai ngày đều chưa biết, vẫn để chưa biết. Submit/publish chặn thân bài HTML rỗng về mặt nội dung khi không có tài nguyên; metadata nguồn thiếu chuyên mục chính được giữ khi có SourceRecord thật và chuyên mục công khai ban đầu đã thiếu, còn bài mới/nhân bản phải đáp ứng mẫu mới.

Mở sửa hoặc lưu bản sửa vô hiệu hóa approval cũ. Xuất bản bản đã duyệt yêu cầu đúng revision/hash, rowversion hiện tại và vùng làm việc khớp snapshot. Xuất bản trực tiếp có quyền riêng, vẫn kiểm tra trạng thái/rowversion/audit. Gỡ bài thu hồi grant công khai; thùng rác giữ toàn bộ revision/source mapping; khôi phục không tự đăng lại. Khôi phục revision tạo snapshot draft mới. Nhân bản không mang source identity/alias/ngày nguồn.

Mỗi lệnh có GUID chống gửi lặp, lưu `ContentCommands`; ID đã dùng với payload/người dùng khác bị từ chối. Transaction cập nhật working draft/snapshot/con trỏ/audit/command. `EditGeneration` buộc mọi lần autosave đổi SQL rowversion của Content; request cũ bị 409 và rollback, không tự retry ghi đè. Frontend giữ nội dung đang nhập khi xung đột.

## Quyền

Quyền hành động: `content.create`, `content.view.own`, `content.view.all`, `content.edit.own`, `content.edit.all`, `content.submit`, `content.review`, `content.publish.approved`, `content.publish` (xuất bản trực tiếp theo convention cũ), `content.unpublish`, `content.trash`, `content.restore`, `media.manage`. Vai trò là tập quyền, không hardcode tên vai trò ở endpoint.

Mỗi request admin đọc user/role claims hiện hành từ SQL; user khóa hoặc quyền bị thu hồi không được dùng cookie cũ để tiếp tục hành động. Policy kiểm tra quyền và service kiểm tra owner/tài nguyên. Người duyệt có thể xem đúng bài đang gửi duyệt; người được đăng bản duyệt có thể xem đúng bài có approval. Mọi mutation dùng DTO allowlist và antiforgery; không nhận owner/state/approval/ngày công khai từ client.

Để tương thích quyền đã có, edit-own/edit-all suy ra create/view-own; edit-all suy ra view-all; quyền publish trực tiếp suy ra publish-approved. Không ghi nâng quyền vào SQL và không sửa mật khẩu/tài khoản vận hành. Quyền mới gỡ/thùng rác/khôi phục cần được người quản trị phân bổ có chủ đích; 2A chưa có màn quản trị người dùng. Tài khoản kiểm thử ba cấu hình chỉ tạo ở SQL Test, credential nằm trong `.local`, không commit.

## Import và media

Importer dùng luồng migration đặc quyền với ngày nguồn; nguồn không đổi không tạo revision/alias trùng. Revision nguồn mới được dựng đủ metadata/category/media trước khi lưu; bài đã có biên tập/gỡ/thùng rác giữ con trỏ public/draft, ghi ngoại lệ cần đối chiếu. Kiểm thử dùng manifest/snapshot có sẵn và Test riêng; không import/crawl Dev trong 2A.

Ảnh/PDF dùng kho ngoài static/publish, UUID, giới hạn stream/decode/parser và quyền theo revision. Publish kiểm MIME/validation/scan policy và tệp gốc tồn tại đúng kích thước trước khi cấp quyền công khai; checksum gốc được xác minh lúc tải/nhập và trong đối chiếu bảo toàn, không tuyên bố quét mã độc. Xem `media-2a.md` về exception development rõ ràng và `NotScanned`; không có scanner production. Draft/preview/private media và HTML public đều `no-store`; route public kiểm tra grant trước cả conditional/range response. Media dùng chung còn public nếu ít nhất một revision công khai tham chiếu. Không hard-delete hoặc ghi đè byte nguồn.

Thiết kế đối chiếu [EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency), [ASP.NET Core resource authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0) và [file upload](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0); bằng chứng nghiệm thu là test SQL/HTTP/UI của dự án, không phải các liên kết hướng dẫn.

## Thành phần bổ sung 2B

Migration additive `20261013030300_PortalAdministration` bổ sung model 2B; không sửa hai migration đã áp dụng hoặc seed cấu hình mỗi lần web khởi động. Trình tự vận hành vẫn là xác minh đúng SQL local, sao lưu SQL/kho tệp, Test trước và đối chiếu bảo toàn trước khi áp dụng Dev. Trạng thái áp dụng và kết quả kiểm thử thực được ghi trong PROJECT_STATE/báo cáo 2B; tài liệu kiến trúc không thay bằng chứng nghiệm thu.

`ContentMetadata` là kiểu có trường rõ, được EF sở hữu trong `RevisionMetadata` và `DraftMetadata`, gồm metadata văn bản, ngày, đơn vị và căn cứ xác nhận. Lookup/filter không dùng EAV hoặc JSON lớn lọc trong RAM. `LegalDocuments`/`DraftDocuments` cũ giữ nguyên làm provenance nguồn; backfill chỉ chuyển trường đã biết vào metadata phiên bản, giữ null/unknown. Giá trị metadata đưa vào fingerprint snapshot; thay metadata phải có revision/approval mới tương ứng. EF bảo vệ cả owned metadata của revision khỏi sửa/xóa tại chỗ.

Ngày văn bản là `DateOnly`, lịch đăng là UTC datetime riêng. Mốc đóng lấy ý kiến date-only là đầu ngày kế tiếp ở UTC+07:00; giờ nguồn có độ chính xác riêng không bị tự điền từ ngày. Xác nhận hiệu lực cần căn cứ, nguồn thiếu không được suy diễn. Mẫu Announcement/Static/Legal/Draft dùng chung `WorkflowService` và media pipeline với Article; không tạo workflow xuất bản khác hoặc nới quyền để dễ demo.

`RevisionSearchProjection` lưu phần chữ và metadata đã chuẩn hóa Unicode/dấu/đ–d theo revision. Snapshot mới tạo projection trong giao dịch; backfill tường minh theo batch 50 revision, không crawl. Query public luôn join `PublishedRevisionId` và điều kiện công khai, vì vậy projection của draft/historical revision không tự tạo kết quả, snippet hoặc count public. SQL `Contains` có tham số và escape literal theo provider; các bộ lọc kết hợp, page size giới hạn, sort whitelist và ID tie-breaker chạy phía SQL. Quan hệ nhiều chuyên mục dùng EXISTS, không nhân đôi nội dung. Đây là substring SQL search; index số ký hiệu không chứng minh tối ưu `LIKE '%...%'`, không index nội dung PDF/OCR và không tự cài Full-Text/đổi collation instance.

## Identity và cấu hình cổng

Identity bổ sung trạng thái vô hiệu hóa, tên hiển thị và bắt buộc đổi mật khẩu tạm. Cookie kiểm tra security stamp/trạng thái SQL mỗi request; policy lấy permission hiện thời từ catalog. Thay quyền nhóm dùng chung thu hồi security stamp các thành viên. `AccountAdministrationService` dùng transaction cùng SQL application lock cho thay đổi tài khoản/nhóm, concurrency stamp và kiểm tra đường quản trị người dùng/phân quyền cuối cùng. `users.manage`, `roles.manage`, `permissions.grant`, `permissions.grant.privileged`, quyền xuất bản, cấu hình và lịch đăng là các capability riêng; không suy ra từ tên Administrator và không tự cấp quyền mới cho Dev. Xem [policy tài khoản](account-admin-guide.md).

`SiteConfiguration` giữ con trỏ draft/live tới `SiteConfigurationRevision` bất biến. Các bản cấu hình version hóa category presentation, menu cây/đích allowlist, banner, homepage block/pin và vocabulary; định danh Category/source slug vẫn ổn định. Áp dụng cấu hình kiểm validation, quyền riêng, concurrency và chuyển live pointer/audit atomically. Restore tạo draft. Nội dung ghim lưu ID, khi render lấy published revision; nội dung bị gỡ không hiện lại vì rollback cấu hình. Category picker/điều hướng dùng bản live, chưa áp dụng cấu hình không đổi nhãn/chuyên mục public.

`PublicSiteService` có snapshot trong phạm vi **một request**, để header/menu/home/footer cùng revision. Không giữ public cache trong bộ nhớ qua request/process: request kế tiếp đọc SQL live pointer và published pointers, kể cả thay đổi do worker khác thực hiện. Search và nội dung public cũng xác nhận trạng thái SQL hiện thời; HTML/private media dùng `no-store`. Khoảng banner được kiểm tra theo thời gian khi render và khi cấp grant media, không cần chờ worker/cache hết hạn. Logo/banner draft có preview riêng; public grant chỉ từ bản live và các tham chiếu public hợp lệ. Tệp dùng chung không bị xóa vì một banner/bài bỏ tham chiếu. Xem [quản trị cổng](site-admin-guide.md).

## Worker lịch đăng độc lập

`PublicationJobs`, attempts và heartbeat nằm trong SQL Server. Lịch lưu content/revision/hash, người yêu cầu, nhánh quyền/approval, giờ UTC, trạng thái, generation, rowversion và lease token; không có timer đăng bài trong web. Console project `Worker` chỉ xử lý publication job allowlist. Lệnh chạy/dừng và cấu hình thực nằm trong [scheduler runbook](scheduler-runbook.md); không tự cài service hệ thống.

Worker claim nguyên tử; commit bị chặn khi token/generation đã đổi hoặc lease hết hạn. Workflow scheduled dùng cùng validation/publish/media/audit với thao tác thủ công, kiểm tra lại tài khoản/quyền người yêu cầu và người duyệt trong nhánh approval. Trạng thái cần rà soát được giữ khi revision/approval/quyền/media không còn hợp lệ, không đăng bằng một tài khoản hệ thống toàn quyền. Hủy/đổi lịch và manual workflow vô hiệu lease/generation trong transaction tương ứng. Command ID và trạng thái hiệu ứng bền vững đối chiếu retry sau commit; attempts kỹ thuật có thể nhiều, không tuyên bố toàn hệ exactly-once.

Mặc định local polling 5 giây, lease 60 giây, chạy bù tối đa 15 phút và 4 attempts SQL có backoff; validation/quyền không retry vô hạn. Đây là ngưỡng ứng dụng local, không cam kết SLA đến từng giây. Worker/web đọc cùng SQL/kho/environment; shutdown giữ lịch SQL và để lease hết hạn theo policy. `-AllowUnscannedMedia` chỉ là lựa chọn Development local tường minh, tệp vẫn NotScanned; Staging/Production/worker không được dùng quyền admin/query/header để lách scan policy.

Các mốc 2C (tương tác công chúng có bình luận kiểm duyệt), 2D (video tải trực tiếp), Giai đoạn 3 (chuyển toàn nguồn), scanner/restore/UAT/production vẫn có ranh giới nghiệm thu riêng. Không suy ra các mốc đó hoàn tất từ model, UI hoặc test 2B.
