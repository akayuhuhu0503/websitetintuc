# Delta SRS v0.5 — 15/10/2026

**V05 PARTIAL / CHƯA PRODUCTION READY.** Đã triển khai trực tiếp trên ASP.NET Core10/MVC-Razor/EF Core/SQL Server hiện hữu. Không dựng lại2D, không đổi DB, không nhập lại nguồn. Đối chiếu toàn bộ DOCX0.5 và prompt delta; [gap matrix](srs-v05-gap-matrix.md), [cutover readiness](gdla-cutover-readiness.md), [runbook](gdla-runbook.md). SRS vẫn là bản nháp; target đầu11/2026 chưa phải approvedrelease.

[Summary an toàn](evidence/v05/v05-20261015T073755Z-f680ab6e/summary.json) và [tham chiếu hashmanifest](evidence/v05/v05-20261015T073755Z-f680ab6e/manifest-reference.json) ghi riêng code/fullrun/sourcepreservation và thiếu hụt artifact.

## Kết quả thực sau sửa cuối

Full mới **117/117 nhóm,0FAIL,exit0**, run **`v05-tests-20261015T082521Z-51675950`**,08:25:27–08:33:04UTC15/10. Buildsolution/Web/Worker0warning/error; NuGetaudit7project không báo advisory từ nguồn đang cấu hình. **307đường dẫn mã/0drift,5836artifact được guard trước lượt cuối/0drift**. Không suy rộng guard này thành bảo toàn mọi artifact trong toàn lượt: helper chẩn đoán có incident riêng dưới.

Raw: [run/hash/exit](../.local/evidence/phase2d/v05-tests-20261015T082521Z-51675950/run.json), [117 riskgroups](../.local/evidence/phase2d/v05-tests-20261015T082521Z-51675950/sql-tests.json). V05HTTP có5checkpoint,UI18pagechecks/2actions3901440, native/worker/SMTPsink/2DHTTP/UI ghi riêng trongrun; không cộng các đơn vị thànhfullmới. 9/9focused`v05-tests-20261015T082337Z-e9736a0d` chạy trước sửa nhãn CAPTCHA cuối, không dùng nó thay117fullsaucuối.

Lịch sử được giữ: baseline **105/108** ở task`v05-20261015T073755Z-f680ab6e/baseline` (3FAILbrowser, cóSQLpost-login/executiontimeout); fullđầu **114/115** `v05-tests-20261015T080912Z-db33dbc7` (videoMemoryWatchrace); focused6/6 và7/7 là các lượt riêng trước các bổ sung cuối. Không sao chép108/1082D cũ hoặc cộng các focused. Chỉ SQLTest/khoTest/sinkTest dùng để kiểm thử nghiệp vụ.

## Mã đã triển khai và trạng thái từng checkpoint

| Checkpoint | Phần đã có bằng chứng | Trạng thái / còn thiếu |
|---|---|---|
| V05.1 | TrustedPublicUrlPolicy,canonical/OG/sitemap từconfig,Hostallowlist, fullURLalias phân biệt2hosts/cùngpath,unknown404/noopenredirect; inventoryqlddvsNukeViet tách riêng | **PARTIAL**: local vẫnloopback; chưaprodhostbinding/DNS/TLS/authority/backupNukeViet/fullURLinventory/redirectdryrun toànnguồn |
| V05.2 |4publicflows CAPTCHA→SQLPending/private, wrong/expired/missing/replay/CSRF/bypass/5attempt10min, receipt màn hình, outbox honest; Contact1file≤10MiB/notes/audit/Excel/ACL; hỏiđápcôngkhai lọc từkhóa/lĩnhvực+SQLpaging; privateLookupdeny | **PARTIAL**: adminloginCAPTCHA/MFA vàsearchchungQA cònthiếu; productionmail/abuse/retention chưa nghiệmthu. Lookup theo quyết định tạm thời vẫn khóa, không mởsecretaccess khi chưaPO/ATTT |
| V05.3 |3SQLgroups additive,group2NeedsNameApprovalkhôngpublic; Province/Code/Year/source/legalbasis/PIIreviewtheorevision; 2step+quyền riêng/exactapproval/no-comment server; publicfilters/paging/draftisolation | **PARTIAL**: Excel/privateCMSfile-scanproof/positivePDFrelease chưa có; site typedmenu catalog chưa nốiFR17; chưaofficialcontent/PIIpolicy |
| V05.4 |Mẫu lịch tuần trênCMSStaticPage/category76,nhập tay thật quaUI→SQL→privatepreview; releasev1/v2/restorev3 bấtbiến/raceguards; publiclatest/previousnextweek; boundedDOCXtechnicalreader/verticalmerges/warnings | **PARTIAL**: chưaWordupload→scanner→preview/edit→publish/fileoriginaldownload; **AC16.1 BLOCKED:NO_REAL_WORD_SAMPLES**, không dùng3fixturethay3Wordnghiệpvụ. Publichistory chưa bật |
| V05.5 |Readiness/owner-role/evidence/deadline/impact/waiver vàrunbookdryrun; source37mẫu/restore/security/cutovergates rõ | **PASS tài liệu dry-run / RELEASE BLOCKED**, chưadeploy/UAT/restore/authority |

Người dùng xác nhận15/10: giữ nguyên “Người nước ngoài định cư ở Việt Nam” chờPO/phápchế; CAPTCHA không xác minh danh tính; khóa privateLookup, đề xuất secret32byte/hash/revoke/rate-limit nhưng chưa bật; chưa có3Word thật, triển khaiparsertechnical vàmanualversions. [Decision](decision-required-private-lookup.md).

CAPTCHA arithmetic bằng chữ tự sinh offline, keyboard/screenreader; TTL/session/addressHMAC/purpose/targetrevision vàSQLone-use/app-lock/rate-limit; middleware chặn trước modelbinding+SQLrollingwindow chốngcrossprocessreplay. Không hứa chốngbot nângcao, cầnATTT/abuseassessment. `Captcha__TestScope` chỉDevelopment+LandPortal_Test, không bypass đáp án/production. FileHMACkey nằmprivate trongKeysRoot. Endpoint OTPsubmission cũ410, lookupPOST403; giữ bảng/OTP/audit/sessiondata cũ, SQLcompatibilitytests riêng. Mail transport vẫnDisabled hoặcTestSinktheoguard2C; “đã xếp hàng” không gọi “đã gửi”.

FR17 dùnggroup bổ sung cùng bài/categoryscope2A, không đổi14categorynguồn. Directpublish bịchặn; phải đúngapprovedrevision và`content.disclosure.publish`, đủsource/căn cứ/review. FileNotScanned không có ngoại lệFR17. Cán bộ vẫn được tiếpnhận/phâncông/xửlý/trảlời theo quyền2C; lookupgate không xóa hồ sơ. Contact reused cùngprivateattachment/scanworker, quyền list/PII/file/export tách; ExcelinlineString/formula-safe, freshrevocationcótest. Readbackopenpyxlriêng: minimal3cột/138cell,PII8cột/368cell,0formulas/15formula-like textcells, không cộng vào117.

Lịch tuần giữ workingdraft/immutableContentRevision/workflow2A, thêmrelease ledger chứ không làmCMSlịch độc lập. Một hồ sơ phát hành/tuần ISO, mỗi publish tạo versionmới; restore tạo draft/revisionmới rồi đăng tạo v3. Chưa publichistory/fileWordgốc. Parser8cột Technical-v05-1 cóZIP/XML/size/ratio/entries/path/external/macros/hidden/trackedchange guards, vMerge, cảnhbáogridSpan/duplicates; technicalfixtures và3Wordnghiệpvụ tách rõ. Chưa nghiệmthu security testmọi loạiWord/ZIP haymẫunghiệpvụthật.

## Migration và bảo toàn

Migration **`20261015075352_SrsV05PublicFormsAndContent`**:22cộtnullable ởDraftMetadata/RevisionMetadata,4bảng(PublicCaptchaChallenges/Attempts,DisclosureGroups,WeeklyScheduleReleases),indexes và3groupseeds. Up khôngUPDATE/DELETE nguồn, không đổi14category, khôngDown/reset. GeneratedSQLreview`migration-reviewed.sql`; file`migration.sql`3byte đầu do--no-buildchưa cómigrationcompiled được giữ nguyên, không coi đó làSQLreview thànhcông.

**Test trước:** backup2086file/410.576.637byte, COPY_ONLY/CHECKSUM/RESTOREVERIFYONLY, targetnamedlocal; migration/backfill0,0khácbiệt17nhóm. **Dev sau117full:** kiểm target/manifest lại, backup130file cùngSQL/media/snapshot/keys/config/privateACL, rồiapply;0backfill/0khácbiệt17nhóm. **Chưa RESTORE**; VERIFYONLY khôngthay restoredrill.

Devcuối: **37content/37source,47revision,74alias;53asset(41ảnh/12PDF),70capture/68SQLsnapshot,8ngoạilệ,2video metadata/0tệp**. **53/53nguồn vẫnNotScanned**,0khácbiệt17nhóm; Identity/password/stamps/roles/claims và`.env` nguyên. MớiDevchỉ3groupseeds; CaptchaChallenges/Attempts/WeeklyReleases/Interactions/VideoFile đều0. Khôngdemopost/fixtures/grant10quyềncũ hoặc quyềnmới/resetpassword/realemail/crawl. Test cóQA tíchlũy, không tính vào nguồnDev. [Privatepreservation/backup](../.local/evidence/phase2d/v05-20261015T073755Z-f680ab6e/preservation-summary.json).

Quyềncatalog mới chưa cấpDev: `content.disclosure.publish`, `interaction.contact.list`, `interaction.contact.file.read`, `interaction.contact.export.pii`; `interaction.export` có từ2C vẫn kiểmkèmread/PII. Không grant thôngqua migration Identity; SQLruntime role dùng đểschema/runtimetable theoquytrình không đổi tài khoản/quyềnngườidùng.

## Regression phát hiện lỗi và hồ sơ artifact

Full114/115 phát hiện worker đã quétCleanđúngbyte nhưng ởFailed/`VIDEO_InvalidOperationException`, khôngReady. Probe riêng tái hiện stack **Process.WorkingSet64 khi child vừaexit**; sửa hẹp `VideoProcessor.Run.MemoryWatch`: chỉbỏexception khi processđãexit, giữworking-setlimit/kill/deadline/exitcode/probe/MP4/poster/hash checks. Không làm lạiupload/worker/workflow2D. Nativecase6lần vàfinalVideoE2E thật đạt; không mockReady. Probe10PASSsaufix làdiagnostic riêng, không thayfull117.

**ArtifactPARTIALmới:** helperprobe đã ghiđè JSONraw đầu bằng10PASSsaufix. Stack/kết quả đầu còn tooltranscript, rawSQLFAIL/full114/115/workerlogs còn; khôngcóbảnraw đầu đểrestore nguyênbytes. [Incident](v05-diagnostic-artifact-incident.md) giữ sự cố, helper đã đổi tên timestamp+GUID. Không táitạorawgiả, không gọi mọiartifactv05bảotoàn. V04rawsmoke2B vàD15workerraw lịch sử vẫnPARTIAL độc lập. Recheck520rawlegacy từseal2D:0drift; finalguard5836/0drift chỉphạmvi đãchụptrướclượtcuối.

SQLtimeout vẫn **MONITORING / ROOT CAUSE UNKNOWN**. Baselinev05 ghi nhậnpost-login≈14.020ms, siteexecutiontimeout vàbrowsernavigationtimeout. Finalgreen khôngchứngminhSQLrootcauseFIXED; khôngtăngdeadline/commandtimeout, khôngstressDev/điềutravàoDBkhác. [Risk](sql-category-timeout-observation.md).

## File, giao diện và chạy local

Chính: Domain`PublicCaptcha/V05ContentEntities/ContentMetadata`; Infrastructure`PublicCaptchaModel/Service/Options,V05ContentModel/Policy,WeeklyDocxReader,InteractionCaptchaSubmission,PublicInquiryQuery,PublicUrlPolicy`, Workflow/Interaction/metadata/DbContext vàMemoryWatchfix; Webcontrollers/views/models/publicquery/canonical; Applicationpermissioncatalog. Migration+Designer+Snapshot; tests`V05Tests/WordTests/HttpTests/CaptchaHttp`, browser`v05-check`; Interaction/FinishHTTP đổi sang CAPTCHA vàdenylookup, vẫn giữSQLlegacyOTPtests. Scripts`test-v05.ps1`,testRegionV05,run-devV05. Diff/hashdanhmục tronghandoffmanifest; workspace vốnuntracked, không coi gitdifftrống làkhôngđổi.

UI mới18checks/2actions tại390/1440,0JS/offorigin. [UIreview/giới hạn](ui-review-v05.md), [adminlịch/FR17](v05-content-admin-guide.md), [CAPTCHA/hòmthư](v05-interaction-admin-guide.md). Ảnh thật tại [UIrun](../.local/evidence/phase2d/v05-tests-20261015T082521Z-51675950/v05-http/ui/ui-report.json). PreviewCMS khunglịch hẹp cònngắtdòng, chưa hoànthiệnUX/WCAG/physicalSafari.

**Preview v0.5: http://127.0.0.1:5268/**, `/admin/login`, `/lich-tuan`, `/cong-khai-thong-tin`, `/tra-cuu-ho-so`, `/sitemap.xml`. Dev7GETreadonly200; previewcũ5238/5258health200, khôngdừngprocesscũ. Previewmới chạy bảncopybinarytáchriêng đểkhôngkhóabuildtest; uploadvideoDevkhóa/mailDisabled/quyềnmớichưacấp. Không có nội dung lịch/FR17demoDev.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-v05.ps1 -Region V05
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-v05.ps1 -Region All
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -V05 -NoBuild -Port 5268
```

Port5268đãcópreview; chọnportloopbackkhác nếu chạy thêm. Workerhệthống/cài service khôngthực hiện; scheduler/mail/scan/video childworkers thật dofullrunner khởi chạy trênSQLTest. Lệnh2Dworker thật giữ trong [runbook2D](video-worker-runbook.md), SMTPsink/outbox trong [runbook2C](interaction-outbox-runbook.md); khôngchạyunscopedworkerDev đểdemo. Newbuildroot`.local/build/phase2d/v05-current`,evidencev05unique dưới boundaryphase2d đượcguard, khôngghiđèvideo-*cũ.

## Checkpoint tiếp theo

Hoàn thiệnuploadWordprivate/scanner/preview-edit/originalreview/downloadACL; chờ3Word thật/mẫuVănphòngCục vàpublichistorypolicy. Hoàn thiệnFR17Excel/PDFscanproof/nativeacceptance vàtypedmenu draft/live; khôngnớiNotScanned đểPASS. Bổ sungCAPTCHAadminlogin cùngregressionreauth/Identity, searchchungQA, scope/list-onlyContactnegative sâu hơn, UXbảnglịch, cácP0cònthiếu tronggapmatrix. PO/ATTTduyệtownershipsecret trước mớimởlookup; tênnhóm2vẫnchờ. CácgateProductionTLS/MFA/scannerprivate/SMTP/restorethật/fullmigration/UAT/monitoring/authority vẫnBLOCKED. Không tự làm2E/2F/Phase3/fullcrawl/deploy/DNS/Git/email/service/AVpolicy.
