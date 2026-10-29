# gdla.gov.vn — checkpoint Phase3L, 22/10/2026

**CHƯA PRODUCTION READY.** [Báo cáo3L](phase-3l-report.md), [handoff hạ tầng/UAT](phase-3l-infrastructure-handoff.md). Newest250 SQL/file restore đãPASSLOCAL trongDBriêngREAD_ONLY;khôngthaynghiệmthu target/RPO-RTO. Packet9unsignedREADY_FOR_APPROVALvớifresh18proof143vàTest/DevpreflightPASS,0sign/Apply. SQLrootUNKNOWN/MONITORING cònrủiro;targetprofile/IIS/SQL/ACL/HTTPS301/scannerSMTP/MFA/businessUAT/NukeVietvẫnBLOCKED/NOTRUN. Khôngdeploy/DNS. Nội dung3K dướiđây làlịchsử;60legacyproof140cònstale,18proofjob9đãrefresh.

---

## Lịch sử checkpoint Phase3K

**CHƯA PRODUCTION READY / NO GO LIVE.** [Báo cáo3K](phase-3k-report.md), [UAT16cases](phase-3k-uat-plan.md), [resume/restore](phase-3k-runbook.md). Dev250 giữ nguyên;9bàiTest cópacket riêng nhưngDevpreflightSQLtimeout,khôngREADY/Apply. SQLUNKNOWN/MONITORING tái diễn; không dùng kết quả3I để đóng incident.

FinalDefender143khácproof140:78proofhiệnSTALE,packet9cònchặnstrictfreshness. Khôngkếthừafreshness từnhãnPASSnativeClean140trongbảngdưới.

| Điều kiện | Trạng thái3K / cần cung cấp |
|---|---|
| Target hosting/SQL | BLOCKED: chưa có server/OS/SQLinstance/database/storage/serviceidentities được duyệt; localIIS/W3SVC/WASabsent |
| Web/Worker profile | BLOCKED phần mềm: PublicBaseUrl/AllowedHosts/SQL/scan/docs/video/batchmedia cònlocalDevelopment/Testguard; cầnprofiledelta đượcreview/kiểm thử,khôngfakeDevelopment |
| Scanner | BLOCKEDProduction:78nativeCleanTest140 không cấpquyềnpháthànhProduction;53originalDevNotScannedgiữnguyên;531CleanbatchcònDevelopmentservefence |
| SMTP/secrets | BLOCKED:host/port/TLS/from/relay+secretstore dođơnvịcungcấp;khônggửithưthật/đổisecretvào.env |
| MFA | BLOCKED: chưa cóenrol/challenge/recoveryflow;IdentitytokenproviderskhôngthayMFA;khôngtựbậtcờ/nângquyền |
| Restore/backup | PARTIAL:actual235SQLrestorePASSREAD_ONLY;newDev250backupPASSCHECKSUMVERIFYONLY+files;newestSQLrestore/targetRPO-RTONOTRUN |
| HTTPS/hostname/301 | NEEDS_APPROVAL / NOTRUNtarget:binding/cert/renewal/trustedproxy/routingoldqldd→gdla,cảmappingNukeViet và kếhoạchrollbackDNS |
| UAT | PREPARED16cases;chưa có người ký/lịch/môi trường/tài khoản đượcđơnvịcấp,3Wordthật/lookupauthoritycònthiếu |
| NukeViet | BLOCKED_BY_NETWORK80/443refused;0realcontent,officialexportNOTPROVIDED;mappingdraftchưanghiệmvụduyệt |

Khôngdeploy/càiIIS/service/đổiDNS;khôngcóProductionsecrets hoặc nghiệmthu ngầm từ checklist. Cácbảng3Jdướiđâylàlịchsử;điềukiệnchưađóngvẫngiữnguyên.

---

## Lịch sử Phase3J,21/10/2026

**CHƯA PRODUCTION READY. Không có phê duyệt GO LIVE.** Bảng dưới là trạng thái hiện hành; bảng cũ bên dưới chỉ là lịch sử. Dev đã có250 nội dung nguồn/261revision/584media/500alias sau3I, không còn baseline37. 53 gốc vẫnNotScanned. Restore SQL thật Phase3J đã PASS **mốc backup235**, không phải đủ250 hoặc môi trường Production. [Báo cáo3J](phase-3j-report.md), [scanner](scanner-production-readiness.md), [restore](phase-3j-restore-runbook.md).

| Điều kiện | Trạng thái hiện hành | Bằng chứng / bước còn thiếu | Vai trò cần quyết định |
|---|---|---|---|
| Kiến trúc và dữ liệu local | READY LOCAL / độ bao phủ PARTIAL | ASP.NET Core10/MVC-Razor/SQL Server giữ nguyên;250 nguồn; tổng hai website UNKNOWN, không tái Apply12packet | Đội migration + chủ quản dữ liệu |
| Target OS/SQL/IIS/Web/Worker/HTTPS/media | BLOCKED: chưa có target được cấp | Local SQLEXPRESS không phải nghiệm thu máy chủ đích; worker/web có khóa môi trường, cần profile staging và quyền triển khai riêng | Vận hành/DBA/ATTT |
| Scanner Production và quyền phát media | BLOCKED | Không có engine/policy PII đích;53NotScanned,531Clean batch còn hàng rào Development; cần delta được review và proof đích thật | ATTT + vận hành |
| SMTP/OTP/outbox Production | BLOCKED | Chỉ sink Test được phép; chưa cấu hình/kiểm chứng SMTP thật, delivery receipt/Uncertain/escalation | Vận hành mail + ATTT |
| Admin MFA, quyền và secrets | BLOCKED / NEEDS_APPROVAL | CAPTCHA đăng nhập đã có, không thay MFA;10 quyền vận hành Dev chưa Apply và giữ nguyên; chưa cấp service identity/least privilege đích | Chủ dự án + ATTT/DBA |
| SQL/media restore local | PASS LOCAL tại mốc235 | DB riêng `LandPortal_Restore3J_3f7ec1d0`, READ_ONLY, DBCC/byte/schema/readbackPASS; không đè Dev/Test | DBA review kết quả |
| Backup/restore vận hành đích, RPO/RTO | NOT RUN / BLOCKED release | Chưa nghiệm thu lịch backup/offsite/encryption/monitoring, restore đủ250 và dữ liệu phát sinh tại target | DBA + vận hành |
| Nguồn QLDD đầy đủ | PARTIAL | Có inventory/cache và báo cáo từng danh tính/chuyên mục; chưa chứng minh tổng nguồn/phân trang đầy đủ,52 dòng lịch sử không đồng nghĩa52 bài mới | Chủ quản dữ liệu + QA |
| NukeViet gdla | BLOCKED transport/mapping | DNS45.120.229.41; HTTP/HTTPS từ chối TCP, TLS chưa bắt tay;0 nội dung thật kiểm kê; chưa có official export/mapping được duyệt | Chủ quản website + PO |
| FR17 nhóm2 | SRS1.0 đã chốt tên; không còn blocker tên mặc định | “Người nước ngoài định cư ở Việt Nam”; quyết định nội dung/PII và việc phát hành vẫn cần đúng quyền, không tự đổi live Dev | Nghiệp vụ + pháp chế |
| Word lịch tuần / private lookup | BLOCKED riêng | AC16.1 còn thiếu3 Word nghiệp vụ khác tuần; lookup chi tiết riêng tư vẫn khóa chờ cơ chế sở hữu được ATTT/PO duyệt | Văn phòng + PO/ATTT |
| UAT/SRS1.0, hiệu năng/a11y/an ninh | NOT RUN nghiệm thu Production | Local technical tests không thay UAT; không chạy full suite trong3J khi không đổi module | QA/PO/ATTT |
| DNS, TLS,301 và cutover dừng sync | NEEDS_APPROVAL / NOT RUN | Có500alias local; chưa fullmapping hai nguồn, quyền DNS/SSL/IIS hoặc rehearsal backout nguồn cũ; chưa deploy | Chủ domain + vận hành/PO |
| SQL timeout | UNKNOWN / MONITORING | Giữ incident; các thao tác3J không chứng minh nguyên nhân gốc đã chữa; không tăng timeout/config để che lỗi | DBA + đội phát triển |
| Artifact lịch sử | PARTIAL | V04rawsmoke2B/2Dworkerraw mất giữ nguyên, không phục dựng giả | QA/PO |

Ưu tiên ngắn nhất: xử lý dữ liệu còn thiếu trên Test và review báo cáo restore; song song cấp môi trường staging/engine/chính sách ATTT, rồi làm delta Production theo đúng guard thay vì đổi cờ môi trường. Chỉ lập lịch cutover khi có UAT và release authority riêng.

## Checklist lịch sử trước Phase3J — không dùng làm baseline hiện tại

**Không có APPROVED RELEASE / GO LIVE. CHƯA PRODUCTION READY.** Đây là checklist/dry-run trong workspace; không thay DNS, IIS hoặc website đang vận hành. Kiến trúc ASP.NET Core10/MVC-Razor/EF Core/SQL Server giữ nguyên. 37 nội dung mẫu không chứng minh chuyển toàn bộ dữ liệu.

| BLOCKER | OWNER theo vai trò (chưa chỉ định cá nhân) | EVIDENCE hiện tại | DEADLINE | RELEASE IMPACT | WAIVER AUTHORITY |
|---|---|---|---|---|---|
| Domain gdla/TLS/HTTPS/expiry alert/host binding | Đơn vị vận hành + PO | Chưa quyền DNS/chứng chỉ/IIS; local loopback giữ nguyên | Trước staging/UAT | BLOCKED public | Chủ domain + phụ trách ATTT; không tự waiver TLS |
| Backup/export và inventory website NukeViet đang chạy gdla | Chủ quản website hiện hữu | Chưa cấp quyền/export; import NukeViet còn [GĐ] | Trước lập lịch cutover | BLOCKED cutover/rollback | PO + chủ quản dữ liệu |
| Redirect qldd source→gdla: authority, map từng URL/tệp | Chủ domain qldd + PO | Aliases theo fullURL/host/kind/sourceID; chỉ mẫu, chưa full inventory | Trước cutover | BLOCKED full redirect | Chủ domain + PO |
| Toàn dữ liệu SharePoint/coverage/tài nguyên/video | Đơn vị dữ liệu + đội migration |37content/53asset;8ngoại lệ;2video chỉmetadata/0file | Trước UAT dữ liệu | BLOCKED toàn bộ dữ liệu | PO xác nhận coverage và ngoại lệ, không đổi mẫu thành100% |
| Production scanner + private PII policy + tool provenance/license/isolation | Đơn vị vận hành + ATTT | DefenderTest chỉ anonymous/publicSHA; nguồn53NotScanned; FFmpeg essentials tự báo nguồn/license, chưa legal/supplychain approval | Trước public upload | BLOCKED private upload/media | ATTT + pháp chế; không bypass scan |
| SMTP production/receipts/outbox Uncertain/alerts | Vận hành mail + ATTT | Chỉ SMTP sink Test; không gửi email thật | Trước UAT email bắt buộc | BLOCKED email production | PO và vận hành mail |
| Private lookup ownership mechanism | PO + ATTT | User xác nhận tạm khóa; đề xuất secret32byte hash, chưa duyệt | Trước mở lookup | BLOCKED lookup; giữ deny | PO + ATTT, không CAPTCHA/code-email waiver |
| CAPTCHA abuse/accessibility policy + security scan + privileged MFA | ATTT + QA | CAPTCHA arithmetic offline có TTL/SQLrate/replay; chưa đánh giá bot nâng cao; admin CAPTCHA/MFA còn backlog | Trước public | BLOCKED security acceptance | ATTT; không dùng CAPTCHA thay Identity/MFA |
| SQL least privilege + credentials + keys/media ACL | DBA + vận hành | local namedProjectDB/runtime role có test; chưa cấp hạ tầng/quyềnDev mới | Trước staging | BLOCKED deploy | DBA + ATTT |
| SQL+media+keys+config backup hằng ngày và RESTORE thật | DBA + vận hành | COPY_ONLY/CHECKSUM/VERIFYONLY có; **chưa restore drill** | Trước cutover rehearsal | BLOCKED release | DBA/ATTT; VERIFYONLY không thay RESTORE |
| SQL timeout chưa rõ nguyên nhân | DBA + đội phát triển | MONITORING / ROOT CAUSE UNKNOWN; baselinev05 lặp lại post-login/execution timeout/Test, không tăng timeout | Trước đánh giá UAT tải | BLOCKED đánh giá độ tin cậy | PO/DBA nhận rủi ro có căn cứ; không tự đóng |
| FR17 tên nhóm2, thẩm quyền/nguồn/PII/correction policy | PO + pháp chế + đơn vị nghiệp vụ | Tên giữ nguyên NeedsNameApproval; user xác nhận chưa công bố | Trước bật nhóm2/phát hành dữ liệu | BLOCKED nhóm chưa duyệt | PO/pháp chế |
| Lịch Word:3tuần thật, mẫu được duyệt, policy file gốc/publichistory | Văn phòng Cục + PO | Không có3file; AC16.1 BLOCKED:NO_REAL_WORD_SAMPLES; technicalfixtures không thay | Trước nghiệm thu Word | BLOCKED AC16.1/Word public | PO + Văn phòng Cục |
| Branding/footer/English name/đích DVC-mail | Văn phòng Cục + PO | [GĐ] chưa văn bản xác nhận; không tạo số/email/tên giả | Trước UAT nội dung | PARTIAL UI/content | Văn phòng Cục |
| Responsive/a11y/4Gperformance/browser/capacity | QA + vận hành | Edge viewport + fixture nhỏ; chưa physicalSafari/WCAG/đại diện dữ liệu/1GiB-IIS | Trước release | BLOCKED NFR acceptance | QA/PO/ATTT theo loại |
| Worker độc lập/monitoring/rotation/disk/alert/SLA5phút | Vận hành + DBA | Childworker Test thực; chưa service/HA/giám sát production; không cài service | Trước UAT vận hành | BLOCKED deploy | Vận hành + PO |
| UAT + backout rehearsal + phê duyệt P0 | PO/PM/QA/vận hành | Target đầu11/2026; nhóm ưu tiên1.7 còn [GĐ], chưa UAT/restore/backout | Trước ký release | BLOCKED GO LIVE | PO/PM/ATTT/vận hành |
| Historical evidence incidents | PO/QA | V04rawsmoke2B và2Dworkerraw missing PARTIAL, không tái tạo | Trước chốt hồ sơ nghiệm thu | PARTIAL hồ sơ, không sửa lịch sử | QA/PO đánh giá chính thức |

Thống kê đất/Excel thống kê/ranking/gộp website khác có thể lên mốc sau theo mục1.7 đã được PO chốt; an ninh, riêng tư, scanner, backup/restore và cutover authority không được tự hoãn.
