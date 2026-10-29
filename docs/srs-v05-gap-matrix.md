# V05-Finish — cập nhật delta kỹ thuật, 15/10/2026

Checkpoint mới thay thế các nhận định “chưa có” ở bảng lịch sử phía dưới. **123/123 PASS** sau sửa cuối, `v05-finish-tests-20261015T134049Z-dc3d22a1`; xem [báo cáo](phase-v05-finish-report.md) và [acceptance hiện hành](acceptance-matrix.md). Toàn SRS vẫn PARTIAL, CHƯA PRODUCTION READY.

| Delta | Hiện trạng | Gate còn lại |
|---|---|---|
| FR01/FR12/FR17 typed menu | Template draft/live + public approved-group filter, giữ14mục; SQL/HTTP/mobile PASS LOCAL | Apply Dev NOT RUN, không tự grant; tên nhóm2 NEEDS_PO |
| FR07 general search QA | SQL union/page/count; accent/noaccent/đ, only current consented approved snapshots, negative privacy PASS LOCAL | Lookup ownership vẫn khóa |
| FR11/NFR03 admin CAPTCHA | Login + reauth SQL one-use và browser retry, giữ Identity/CSRF/lockout/rate, PASS LOCAL | MFA/anti-bot Production assessment chưa nghiệm thu |
| FR16 Word import | Upload10MiB/private/real scanner→parser→preview/edit→revision/originalproof→publish/restore PASS LOCAL kỹ thuật | AC16.1 BLOCKED:3Word thật chưa có; public history NEEDS_PO |
| FR17 PDF/Excel | Genuine Clean proof/immutable approval/exactbytes/ACL; oldUploadedPDF không bypass, real enqueue; PASS LOCAL Test | Production/PII scanner chưa duyệt, source53 không đổi |
| Bảo toàn | 0diff17nhóm/source53NotScanned/Identity/env nguyên; migration additive Test→Dev sau backup | Restore drill/UAT/fullmigration chưa thực hiện |

---

## Lịch sử audit V05 delta trước Finish (không phải trạng thái hiện hành)

# Delta SRS v0.5 — PARTIAL, audit và kiểm chứng, 15/10/2026

Nguồn đối chiếu: toàn bộ `SRS_CTTDT-Cuc-QLDD_v0.5.docx` và `PROMPT_CODEX_DELTA_SRS_V05.md`. SRS ghi **0.5 – Bản nháp**; các [GĐ] chưa được PO phê duyệt. Kiến trúc giữ ASP.NET Core 10/MVC-Razor/EF Core/SQL Server. Không xây lại 2D.

Baseline kiểm trực tiếp: Dev37 nội dung/47revision/74alias,53asset(41ảnh/12PDF) nguyên NotScanned;70capture/68snapshot,8ngoại lệ,2video metadata/0file;0khác biệt/17nhóm so bàn giao2D. Baseline mới105/108(3FAIL) được giữ; fullcuối117/117 exit0,307codepaths0drift,5836guardedartifacts0drift. Không kế thừa108/108 cũ; incidentdiagnosticv05rawđầu overwritten vẫnPARTIAL riêng. D15/V04 artifact PARTIAL; SQL timeout MONITORING / ROOT CAUSE UNKNOWN.

| SRS / yêu cầu chính xác | Ưu tiên | Bằng chứng hiện tại / delta | Trạng thái audit | Rủi ro / kiểm thử cần chạy |
|---|---|---|---|---|
| RB-01/02/03, 1.7: gdla.gov.vn, hạ tầng Bộ, đầu11/2026 | P0 | Local loopback, SQL Server đã chọn; domain/TLS/IIS chưa được cấp | PARTIAL / NEEDS_PO | Target date chưa là phê duyệt release |
| FR-01, AC-01.1–4: menu, search, footer, mobile | P0 | 2B site config + V04 public UI; thông tin chính thức/links còn thiếu | PARTIAL | Không bịa footer hoặc đích menu |
| FR-02/BR-02/03: trang chủ, chỉ published | P0 | PublicContentQuery/SiteConfigurationService; thống kê đất/ranking chưa có | PARTIAL | Không fake counter; giữ khối chưa có dữ liệu ẩn |
| FR-03/BR-04/05/AC-03.1–2:14chuyên mục, published revision, tên tác giả | P0 | Workflow2A/2B/V04, PortalController/OG | PASS LOCAL regression117 cho vùng đã có | Không lấy lịch sử PASS thay test mới |
| FR-04/BR-06–08/AC-04.1–3: CAPTCHA một lần,5lần/10phút, kiểm duyệt | P0 | PublicCaptchaService + endpoint4luồng/CSRF + middleware + SQLrollingwindow; full117 cóSQL/HTTPwrong/expired/missing/replay/ratelimit/4flows | PASS LOCAL các case đã chạy | Arithmeticanti-botproductionassessment/NAT/retention vàCAPTCHAadminlogin chưa nghiệmthu |
| FR-05/06/BR-09: thông báo/văn bản/dự thảo | P0 | Metadata immutable + expiry có căn cứ/V04; feedback2C | PASS LOCAL regression117 cho vùng đã có | Không suy đoán hiệu lực; draft không đổi live |
| FR-07/BR-10/11: hỏi đáp CAPTCHA/receipt/private/assignment/SLA | P0 | CAPTCHA/receipt màn hình;admin2Cgiữ; publickeyword/field/SQLpaging23rows20+3;lookupdenytheouser | PARTIAL | LookupownershipNEEDS_PO/ATTT; generalSearchQA thiếu; SLA10ngày còn[GĐ] |
| FR-08/14: thống kê tỉnh, import Excel | P1 | Chưa triển khai | MISSING | Ngoài delta, không fake bảng/counter |
| FR-09: Giới thiệu/Video/Sổ tay/Liên hệ/Hòm thư góp ý/thống kê | P0 | Contactreused,private1file10MiB/realDefenderTest/notes/Excel/ACL;2DgenuineregressionPASS;sourceintrothiếu | PARTIAL | Chưaofficialintro/statistics/data; productionPII/scanner/mail chưa duyệt |
| FR-10/BR-12–16/AC-10.1–2: workflow/role/exactrevision/scheduler≤5phút | P0 | WorkflowService/Scheduler SQL, 2A/2B/V04/2D | PARTIAL | Worker poll/recovery không đồng nghĩa SLA production5phút; cần infra |
| FR-11/BR-01: endpoint permissions/accounts/audit/idle | P0 | Fresh SQLpermissions, scope, stamps, lastadmin, idle30, lockout5/15 | PASS LOCAL regression117 cho vùng đã có | CAPTCHA login NFR03 chưa có; MFA production thiếu |
| FR-12/13: inbox/batch/assignment/mailbox/Excel/menu/banner | P0 | 2B/2Cregression; Contactlist/read/file/export tách,notes/audit/rowversion,ExcelinlineString/readback | PASS LOCAL vùng đã chạy / PARTIAL toànFR | Contactlist-onlynegative sâu hơn vàFR17typedmenu chưa nối; khônggrantDev |
| FR-15/AC-15.1: ranking7/30ngày,related5/tags/dedup30phút [GĐ] | P1 | Related cùng chuyên mục hiện có; chưa ranking/tags | MISSING | Không seed lượt xem |
| FR-16/BR-20–23/AC-16.1–3: lịch tuần nhập tay/Word/phiên bản/restore | P0 manual; P1 Word | CMSStaticPage/category76+releaseledger,actualUI save/privatepreview; SQLv1/v2/restorev3;DOCXtechnical/vMerge/guards | PARTIAL; AC16.1 BLOCKED:NO_REAL_WORD_SAMPLES | ChưaWordupload/scanner/preview-edit/fileoriginal;publichistoryNEEDS_PO;3fixtureskhôngphải3Wordthật |
| FR-17:3nhóm,province/year,PDF/Excel,two-step,no-comment | P0 | 3groupseed,idempotent;group2NeedsNameApproval;metadatarevision/filter/paging/PIIreviewgate/exactapproval/no-comment SQLHTTP | PARTIAL / NEEDS_PO tên2 | ChưaExcel/PDFCMSscanproof/positivefileacceptance/typedmenu; chưaofficialcontent/PIIpolicy |
| 5.1/AC-05.1–2: toàn dữ liệu/redirect50URL | P0 release | Chỉ mẫu37/53; aliases fullURL/sourcehost; unknown404 | PARTIAL | Không fullcrawl; authority qldd/DNS chưa có; chưa50đối tượng độc lập |
| 5.2: nhập NukeViet/website khác [GĐ] | P1 / NEEDS_PO | Chưa có danh sách/phê duyệt/quyền backup | NEEDS_PO | Không gộp identity chỉ theo path/id |
| NFR-01–04:HTTPS/responsive/CAPTCHA/security/errors | P0 | Local CSP/CSRF/ACL/scan thậtTest; TLS/MFA/vulnerabilityscan chưa nghiệm thu | PARTIAL | Security không được hoãn qua cutover |
| NFR-05/09:4G<3s/UX/lazyimages | P1 | V04/2D viewport emulation; chưa benchmark đại diện | PARTIAL | Không chứng nhận physicalSafari/WCAG/perf |
| NFR-06:SEO/canonical/sitemap | P1 nhưng domain P0 | PublicUrlPolicy/layout + pagedSQLsitemap,hostguard,unknown404; fullHTTPproof | PARTIAL | Livehost/cutoverauthority/SEOfulldata/brandingNEEDS_PO; sitemapkhôngprivate/draft |
| NFR-07:email/phone chỉ admin | P0 security gate | Private DPoriginal, reviewed public projection | PASS LOCAL lịch sử; lookup delta đang khóa | CAPTCHA không là xác minh danh tính |
| NFR-08/10:backup hằngngày/restore/bàn giao | P0 release gate | COPY_ONLY CHECKSUM+VERIFYONLY có; chưa restore thật/hạ tầng | BLOCKED | VERIFYONLY không phải RESTORE; chưa có lịch vận hành/owner xác nhận |

Các checkpoint V05.1–5 sẽ cập nhật theo bằng chứng thực, không tự PASS các yêu cầu còn [GĐ].

## Đối chiếu bộ kiểm chứng delta sau cùng

| ID | Bằng chứng thực / trạng thái |
|---|---|
| V05-01 | URLpolicySQLaliases2hosts/canonical/noopenredirect/unknown404/Host400 PASS LOCAL; live301/authority PARTIAL |
| V05-02 | CAPTCHA4flows/TTL/session/purpose/replay/missing/wrong/expiry/CSRF/rate5 SQL+HTTP PASS LOCAL; nângcaoanti-bot/NAT/retention chưa nghiệmthu |
| V05-03 | Lookupchi tiếtdeny GET/POST,khôngexistence/PII theo quyếtđịnhuser PASS gate; cơchếsecretrelease NEEDS_PO/ATTT |
| V05-04 | Contactprivate/notes/Excel;4flows/inbox/assignment/batch/outbox2C vàQApublicSQLpaging PASS LOCAL;searchchungQA cònMISSING |
| V05-05 | FR17group/two-step/revision/filter/no-comment PASS vùng chạy;fileExcel/PDFscanproof/menu PARTIAL |
| V05-06 | LịchmanualUI→SQL/privatepreview,v1/v2/restorev3,duplicateweek/invaliddate;DOCXtechnicalvMerge/warnings PASS FIXTURE;AC16.1 BLOCKED:NO_REAL_WORD_SAMPLES;fullWordflow PARTIAL |
| V05-07 | GenuineDefender ContactPDFClean/GET/Range/ACL/freshrevocation PASS;fullSCAN thậtEICARInfected riêng; CMSWord/FR17nativefileflow BLOCKED/PARTIAL |
| V05-08 | MigrationTest→Dev/backup/target/idempotency/0diff17 PASS;VERIFYONLYkhôngRESTORE |
| V05-09 |18UIchecks/2actions3901440/0JS-offorigin PASS emulation;UXbảnghẹp/WCAG/physicaldevices PARTIAL |
| V05-10 |Finalfull117/117,307paths0drift,5836guard0drift;source17/Identity/env/53NotScanned PASS preservation;diagnosticartifactincident PARTIAL |
| V05-11 |MemoryWatchracerootreproduced+narrowfix/native6iterations+fullgenuinevideoE2E PASS; không làm lại2D |
| V05-12 |Readiness/runbookdryrun PASS tài liệu;TLS/DNS/export/restore/UAT/fullcoverage/security RELEASE BLOCKED |

Checkpoint chính thức: V05.1PARTIAL, V05.2PARTIAL, V05.3PARTIAL, V05.4PARTIAL/AC16.1BLOCKED, V05.5tài liệuPASS/releaseBLOCKED. OverallPARTIAL, không tựkýduyệt[GĐ]. Xemphase-v05-delta-report.md cho run/ảnh/hash/lệnh/checkpoint tiếp. V04/D15/diagnosticv05 provenance cònPARTIAL;SQLUNKNOWNgiữnguyên.
