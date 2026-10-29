# Scanner Production — kế hoạch Phase3J, chưa được kích hoạt

> **Current3L22/10:** chỉ18tài nguyênjob9 đã đượcrefreshnativeClean143, finalhealth/byte/SQLreceiptsPASS;60legacy chưa refresh và vẫnstale140. Job9now36receipts2/resource;khôngscan/promote53originalDev,khôngđổipolicy/khóaupdate. [Báo cáo3L](phase-3l-report.md), [sequencingrunbook](phase-3l-runbook.md). ProofTest143 không cấpquyềnProduction;531Cleanbatch vẫnDevelopmentservefence. Override3K“all78stale” dướiđây là lịch sử của proof140, không phải trạng thái của18proof mới.

> Final3K:definitionsđã143,trongkhi78nativeproofsquét140;proof **STALE_DEFINITIONS_DRIFT**,khôngthểApplytheostrictpolicy dùTTLchưa hết. Giữraw140/thờigianthật,khôngtựsửachữký/metadatađểkhớp143. Cầnrefreshđượcphép sauđiềutraSQLvàfreshplan/hash;khôngscan/promote53originalDev.

> Cập nhật22/10 Phase3K:78bản sao public **Test** quét nativeClean/defs1.461.140.0,proofbytes/SQLreceiptsPASS; khôngscan/promote53originalDev. Job60hiện120receipts2/resource,giữlimit3/180. ApplicationstageSQLtimeout,khônggọiProductionready hoặc áp dụngproofTestchoDev. Các khóa phần mềm và điều kiện đích dưới đây vẫn còn. [Báo cáo3K](phase-3k-report.md), [UAT/input hạ tầng](phase-3k-uat-plan.md). Cần serviceidentity/leastprivilege,quarantine ngoàiwebroot,policyPII/cloudsubmission/retention,engine/definitions+Clean/EICAR/failure đúngbyte vàstagingprofile được review. Không chỉ đổi biếnmôitrường để né khóa.

**BLOCKED: chưa có engine, chính sách và môi trường đích được nghiệm thu.** Không có thay đổi `.env`, quyền dịch vụ, firewall, AV hoặc trạng thái media Dev trong mốc này. Không dùng DefenderTest hoặc proof Test để cấp quyền Production.

## Những khóa đang được code thực thi

| Vùng | Khóa hiện tại | Công việc phải được duyệt trước Production |
|---|---|---|
| `src/Worker/Program.cs` | SQL local/named Dev-Test; `--scan` chỉ Development/Test | Profile đích, service identity, least privilege và kiểm thử fail-closed thực trên staging |
| `EditorialDocumentService.RequireTestInfrastructure` | Word/PDF/Excel mới chỉ Development/Test/storage Test | Scanner tài liệu và quyền private/public theo revision được nghiệm thu |
| `InquiryFileOptions`, `VideoOptions` | Tiếp nhận tệp hỏi đáp/video còn khóa môi trường | Không thể bật chỉ bằng đổi biến môi trường; cần delta phần mềm được review và test thực |
| `MediaUploadOptions.CanServe`, `BatchMediaProof.ValidAsync` | Media `Phase3BValidated` chỉ Development loopback | Cần proof/policy Production đúng byte và cơ chế chuyển trạng thái được duyệt; không gỡ điều kiện Development để né khóa |
| `DefenderInquiryScanner` | SHA vô danh/công khai, Development/Test, opt-in | Không nhận hồ sơ người dân thật; chính sách cloud submission của máy Test không phải chính sách Production |

53 tài nguyên gốc Dev vẫn NotScanned. Ngoài ra, **531 media Clean nhập batch vẫn mang hàng rào Development**; Clean trong Dev không chứng minh website Production có thể phục vụ chúng. Cần kiểm kê và cấp quyền phát hành theo policy đích cho cả hai tập, không tự đổi Origin/ValidationStatus hoặc dùng lại proof đã hết hạn.

## Cấu hình dự kiến — chỉ là đầu vào cần vận hành xác nhận

| Khóa/thành phần | Giá trị/điều kiện đích |
|---|---|
| `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` | Production; tuyệt đối không giả Development trên máy công khai |
| `ConnectionStrings:Portal`, `Sql:ExpectedDatabase` | Database đích riêng được DBA xác nhận, secret qua cơ chế triển khai; chưa được cung cấp |
| `Storage:MediaRoot`, `TempRoot`, `SnapshotRoot`, `KeysRoot` | Thư mục riêng ngoài webroot, ACL và dung lượng do vận hành duyệt; không lấy đường dẫn máy cá nhân |
| `Scanner:Provider` | Dự kiến Clamd/on-prem được ATTT chấp thuận; không DefenderTest |
| `Scanner:ClamdPort` | Cổng loopback 1024–65535 đã được đơn vị vận hành cấp; hiện chưa có giá trị được duyệt |
| `Scanner:TimeoutSeconds` | Giữ mặc định30 khi chưa có số đo; lỗi/timeout giữ ScanFailed, không tăng để che lỗi |
| `Media:AllowUnscannedDevelopmentUploads` | false; không có ngoại lệ Production |
| Daemon | Loopback/INSTREAM; PING, VERSIONCOMMANDS, signature freshness và health trước/sau; StreamMaxLength/MaxScanSize/recursion phù hợp 10MiB hỏi đáp, 32MiB tài liệu nguồn, tối đa video theo policy đã duyệt |
| Cloud/PII | ATTT xác nhận xử lý on-prem, telemetry/sample submission, retention và không đưa file riêng tư lên SaaS |

Không cài daemon/service bằng tài liệu này. Adapter clamd hiện dùng loopback; nếu kiến trúc đích dùng scanner từ xa thì cần thiết kế transport và ACL riêng, không mở cổng public.

## Quyền dịch vụ và cách ly dự kiến

- Web chỉ ghi vùng incoming/private qua tài khoản ứng dụng; không thực thi file, không có quyền sửa cấu hình AV hoặc SQL schema.
- Worker scanner chỉ đọc byte được claim từ hàng đợi, ghi kết quả/audit qua quyền SQL tối thiểu; dùng lease/rowversion/hash để từ chối kết quả đến muộn. Không cấp quyền sysadmin hoặc đọc database ngoài dự án.
- Kho quarantine/private nằm ngoài static route; khách không có quyền tải/preview/Range qua tên tệp trực tiếp. Infected/ScanFailed/NotScanned giữ đóng; không tự xóa byte nguồn hay tự retry Infected.
- Quản trị chỉ được đọc media Clean khi có quyền hiện hành và đúng hồ sơ/revision. Thu hồi quyền/phân công có hiệu lực ở endpoint; log không chứa nội dung, tên/email người dân hoặc secret.
- Bảo vệ DataProtection keys và manifest bằng ACL/backup riêng; không xuất private key hoặc sao secret vào repository.

## Quy trình batch nguồn — chưa thực thi trên Dev/Production

1. Người có thẩm quyền duyệt exact inventory ID/hash, target, policy, engine, phạm vi quét/áp kết quả, ảnh hưởng public và rollback. Inventory 53 gốc chỉ là đề nghị, không phải approval.
2. Backup SQL/media/keys tại cùng mốc; diễn tập restore trên đích được cấp. Kiểm tra hash/length trước scan; copy vào khu cô lập theo allowlist, không đổi byte gốc.
3. Engine thật quét từng byte; lưu asset ID/SHA/size/provider/engine/definitions/time/output digest/lease/result. Clean/EICAR/cancel/offline/timeout phải có bằng chứng engine thực; fixture chỉ chứng minh kỹ thuật.
4. Fresh read/hash và current policy trước transaction áp kết quả. Findings hoặc byte drift dừng batch, giữ private và đưa người có quyền xử lý. Không chuyển Test Clean sang Dev Clean bằng SQL metadata.
5. Việc phát hành cần ACL và published revision; scan Clean không phải quyền xuất bản. Kiểm thử GET/HEAD/Range/private preview và cache invalidation trên staging đúng binary cuối.
6. Reconcile receipt trước retry; rollback đúng batch, không xóa nội dung biên tập phát sinh, không khôi phục toàn DB tùy tiện. Trực ca theo dõi backlog, failures, definition age, disk, expired leases và thông báo ATTT.

**Còn thiếu:** người phụ trách/authority; môi trường staging/Production; engine và policy PII; service account/ACL/SQL role; delta phần mềm cho profile Production; proof mới và kiểm thử thật trên đích; retention, alert/escalation và UAT. Local restore Phase3J không thay nghiệm thu phục hồi trên máy chủ đích.
