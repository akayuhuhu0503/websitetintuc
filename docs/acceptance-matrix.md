## SQL207 owner Inspect / backup preparation — 29/10/2026

| Hạng mục | Trạng thái |
|---|---|
| Exact packet Inspect | Owner receipt READ_ONLY_STATE_PREFLIGHT_PASS/31table prefix/2migration,0DDL; backupVerified=false |
| One-backup operator | PREPARED: fixedpath/job/tag/manifest/priorInspect binding; existingmatchingfile verify-only; Plan/syntax/negative-scope PASS; enginebackupverification NOT RUN |
| Backup receipt / Apply14 | NOT CREATED / NOT RUN, cần ownerduyệt, onefreshbackupPASS và freshguards; samepacket664de13e... khôngđổi |


## SQL207 DBA readback và resume — 29/10/2026, trạng thái mới nhất

| Mục | Bằng chứng / trạng thái |
|---|---|
| Trạng thái SQL thực | OwnerDBA13CSV:31tables/211columns/66indexes/98objects/2committed migrations; migration3 indicators ABSENT; snapshotDMV complete/no transaction rows |
| Đối chiếu prefix EF | PASS OFFLINE: table names/column types+nullability/PK/indexcolumns+unique/FKs so với migration2,0diff; businessrow estimates0 chưa thay actual EXISTS guard |
| Source fix/generator | EXEC backfill giữ semantics/transactions;4focusedofflinePASS, CLI range14migrations sinh thànhcông/2bad rangesDENIED; buildCLItest0warn/error, không WebWorkerbuild |
| Packet/hash | shared-sql-207-resume / `664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7`, PREPARED; exactprefix/hash/backup/rights/TX/readback guards; oldpacket nguyên |
| New Inspect / backup / Apply | NOT RUN / NEW PARTIAL-STATE BACKUP REQUIRED / NOT APPROVED; Inspect withoutbackup KHÔNG đồng nghĩa ApplyReady |


## SQL207 recovery — ghi đè trạng thái bootstrap-only trước đó, 29/10/2026

| Mục | Bằng chứng / trạng thái |
|---|---|
| Owner schema Apply | FAILED207 tại batch7/line446; DDL có thể đã commit, không retry packet bootstrap0history |
| Root cause và fix source | ApprovedByUserId/new-column compile binding; migration raw UPDATE được EXEC trì hoãn bind; build0warn/error+4focusedoffline PASS, engineDDL NOT RUN |
| Shared SQL actual counts/transactions | UNKNOWN: application credential thiếu VIEWDEFINITION/historySELECT/DMV; expected31tables/2history là dự kiến từ prefix, không phải live readback |
| DBA inspection | scripts/inspect-shared-sql-207.sql PREPARED/read-only; fullDBA execution NOT RUN; oldpacket manifest+4fileSHA unchanged |
| Recovery packet mới | BLOCKED/NOT CREATED đến khi biết exactactualstate; chỉ pending migrations sau reconcile và exactnewhash approval; no DROP/restore/history cleanup |


## SQL recovery 451 — trạng thái mới nhất 28/10/2026

| Hạng mục | Bằng chứng / trạng thái |
|---|---|
| PRECHECK 451 | REPRODUCED: Rights-Fingerprint/cột3 Detail; metadata collation khác nhau; original SELECT451, fixed SELECT PASS trên exact target |
| Fix và chẩn đoán | PASS FOCUSED: COLLATE query-local, PSsyntax/Plan/hashnegative/redaction/queryId+SHA; không thay collation/guard/quyền |
| Packet mới | shared-sql-1934-recovery-451, SHA `b57c8a64531f787d97975ac6c3c9b400c74db19985ef35e6d1f4478c6bf9cff5`; oldpacket/schema/structuralguard nguyên byte |
| Full DBA Inspect / schema Apply | NOT RUN / NOT APPROVED; cần Inspect mới PASS + exactnewhash schema-only approval; không role/import |
| Phạm vi lượt sửa | 4SELECT/1connection/0writes/0restore; không build/publish/fullsuite; không cập nhật Dev/LAN hay tái nghiệm thu SRS |


## Bootstrap-only recovery — 28/10, owner đã bổ sung tên đối tượng

| Hạng mục | Trạng thái |
|---|---|
| Residual objects | Owner DBA báo đúng dbo.__EFMigrationsHistory + dbo.PK___EFMigrationsHistory/0row; không phải agent live readback |
| Recovery guard/operator | PREPARED: giữ nguyên 2objects, exactstruct/0row/authority/transactions/backup/rights guard; Plan/hash-negative/syntax PASS; SQLengine NOT RUN |
| Exact packet | 8d13b555f8c3fa571ab3cdf29f3d6a126c059df8c4125c217ee02741f7b97f31, chưa approval/DDL; chỉ schema, không role/import/drop/restore/newbackup |

## SQL1934 incident — 28/10, ghi đè trạng thái schema chuẩn bị trước đó

| Hạng mục | Trạng thái hiện hành |
|---|---|
| Apply schema do owner chạy | FAILED1934/QUOTED_IDENTIFIER, backup PASS do owner báo; không phải agent verify |
| Generator required SET policy | FIXED OFFLINE, build0warning/error +7focusedSQLgroups +binder positive/stale-negative PASS; chưa có SQLengineDDLproof |
| Schema hiện tại / recovery | UNKNOWN / chờ DBA `scripts/inspect-shared-sql-1934.sql`; không giả định empty hoặc rollback toàn bộ; không retry/restore/drop/historydelete |
| Artifact | Bộ cũ/hash gốc giữ; bộ corrected `.local/publish/shared-sql-1934-fixed/` chưa duyệt/chưa thực thi; không import hay cấp quyền |

## Bổ sung 28/10 — SQL dùng chung (không đổi trạng thái SRS đã nghiệm thu)

| Hạng mục | Bằng chứng / trạng thái |
|---|---|
| SQL Authentication + endpoint/physical identity + approle | PASS OFFLINE, 6 focused C# groups + PS guards; runtime target chưa PASS |
| Kết nối server chung | BLOCKED_TLS: strict certificate validation -2146893019; chưa đọc metadata/ghi SQL, file private gốc không đổi |
| Schema/role | PREPARED OFFLINE, 16 migrations/70 bảng; template unbound/THROW, actual schema/grants/import NOT RUN |
| Data handoff | Ready33/Pending252/Excluded29 từ package đã kiểm hash; 53NotScanned giữ nguyên, targetscan/signing/approval/backup/import còn chờ |

# Đối chiếu mới — data handoff UAT offline, 27/10/2026

| Mục | Kết quả |
|---|---|
| Package thực | PREPARED:33Readycontent/33rev/0media/66alias;18catalog +publicsitedraftpayload;252pending/703media1.003GB;29content+53mediaexcluded |
| Verify/dry-run | OFFLINE PASS manifest `afc5f4eba1fbdf3dd3026f404e638bf51bd41d31fc7dfc6a0dc7a140bf43f3ae`/byteSHA/checksums/DTO;Ready33 vàreviewed285 plans;5negativeguards;build0warning/error |
| Import/scan/idempotency/rollback SQL UAT | IMPLEMENTED, **NOT RUN**;chờallocatedtarget/signer/exactapproval/backup/nativeengine;khôngmockPASS |
| Bảo toàn | Dev314/325/756/628/53NotScanned;LAN285/3BA/ExcelSAMPLE;selectedtablehash+env/config0mismatch;0Dev/LANwrites |
| Bàn giao source | GitHubc307344 giữ;toolsource+ops kèm tronggói,chưacommit/push;khôngZIP/publish mới |
| Vận hành | PARTIAL:targetTTDL/release/ATTT/UAT/restore cònchờ;CHƯA PRODUCTION READY |

Pointer `.local/uat-data-current.json`. Các mục dưới là lịch sử.

---

# Đối chiếu mới — bàn giao auth SRS v1.2 và GitHub, 27/10/2026

| Mục | Kết quả |
|---|---|
| Xác thực | PASS LOCAL: không MFA/TOTP; Identity password7 đồng bộ tạo/đổi/reset; cờ schema cũ không chặn; giữ CAPTCHA/CSRF/lockout/idle/audit/quyền |
| Kiểm tra cuối | 20focusedSQLTestHTTP PASS; Web/Worker/CLI/Setup/runner build PASS; compile fullrunner PASS nhưng fullregression NOT RUN |
| LAN bảo toàn | 11tableSHA0diff,285bài/3accounts/ExcelSAMPLE drafts nguyên;3rolechecks/2HTTPsmokePASS;không reset/grant/schemachange |
| FR14 | Owner BA xác nhận upload/scanner/preview34/lưu nháp trênLAN;không chạy lại,không thay nghiệmthuUAT |
| GitHub | PUSH SUCCESS main `c30734493d258116ffb0a426e5e61e4175c104ba`,49reviewedfiles+2parentlocal;HEAD/origin/remotemainkhớp;0secretfindings;localprivateartifacts ngoàiGit |
| Vận hành | Chờ TTDL cấp hạ tầng/UAT và ATTT xét bỏ MFA trướcInternet; CHƯA PRODUCTION READY |

Evidence `.local/auth-handoff-current.json`. Các mục dưới là lịch sử.

---

# Đối chiếu mới — LAN không bắt MFA, 27/10/2026

| Mục | Kết quả |
|---|---|
| LAN password-only | PASS LOCAL: RequireMfa=false/MfaEnabled=false; login và đổipassword vềadmin; ẩn/gate enrollment/TOTP, không vònglặp |
| Identity BA | PASS: 3 mật khẩu/first-change flags/roles/claims nguyên; admin flagfalse, pendingTOTP thu hồi quaIdentity; 0 accountLAN tạo mới |
| Kiểm tra | Web/Setup/runner build PASS; 11 focused HTTP Test PASS; 3 stored-role checks và2LAN smoke PASS; không fullregression/FR14 nghiệp vụ |
| UAT/Production | Không thay cấu hình hay tài khoản đích; UAT mẫu/default vẫn requiredtrue, TTDL chọn chính sách theo hướng dẫn |

Evidence `.local/mfa-lan-current.json`. CHƯA PRODUCTION READY. Các mục dưới là lịch sử.

---

# Đối chiếu mới — password policy LAN, 27/10/2026

| Mục | Kết quả |
|---|---|
| Policy LAN | PASS LOCAL:7 ký tự, không complexity; Identity + model + form + bootstrap đồng bộ; production LAN-profile guard fail-closed |
| Kiểm tra đúng phạm vi | 14 focused checks PASS, Web/Setup/runner build PASS; ba BA rows nguyên, fixture xóa; không full regression |
| Onboarding BA | LAN đã cập nhật; owner tự đổi mật khẩu/đăng ký MFA; FR14 authenticated vẫn chờ owner, chưa PASS |

Evidence `.local/lan-password-policy-current.json`. CHƯA PRODUCTION READY. Các mục dưới là lịch sử.

---

# Đối chiếu mới — đổi mật khẩu LAN, 27/10/2026

| Mục | Kết quả |
|---|---|
| Đổi mật khẩu lần đầu | PASS LOCAL: giữ validation khi xóa giá trị mật khẩu; lỗi tiếng Việt, cập nhật Identity/flag trong transaction, refresh cookie, redirect MFA |
| Kiểm tra | 13 focused SQL Test/HTTP assertions PASS; build Web/runner riêng PASS; 2 LAN smoke PASS; không full regression |
| Tài khoản BA | Password/stamp/flags/.env giữ nguyên; MustChangePassword=true/MFA=false; owner tự hoàn tất, không reset/cấp quyền |
| FR14 authenticated | Vẫn NOT RUN cho đến khi BA đổi mật khẩu và đăng ký MFA; không suy từ password fixture thành FR14 PASS |

Evidence `.local/password-fix-current.json`. CHƯA PRODUCTION READY. Các mục dưới là lịch sử.

---

# Đối chiếu mới — LAN sample scanner, Git local, TTDL, 27/10/2026

| Mục | Kết quả |
|---|---|
| FR14 LAN policy |IMPLEMENTED exactpublicsamplesonly/nativeMicrosoftSigned+pin/memory-before-disk/noofficialpublish;không globalAV/Prod/Test bypass |
| Native scanner/parser |9focusedPASS:2XLSXrealClean217,34rows/errorrow2C,wrongrole/unknownbyteDENY;malwarecontrolScanFailedblocked,không Infected giả |
| Admin E2E |PARTIAL_ACCOUNT_ONBOARDING:storedManageSite sẵn,owner tự đổi mật khẩu/MFA;authenticatedpreview/draftNOTRUN,chưaPASS |
| LAN HTTP |7hostsmokePASS,siteRUNNING;MFA/CSRF/auth giữ;BAclientverify riêng |
| Bảo toàn |Dev314/325/756/628/53NotScanned;LAN285/285/703Clean/570aliases/3users;12tableSHA và.env giữ;0officialstatistics/0newDevApply |
| Build/Git |Web/Worker/CLI/SetupPASS;commitlocal`fd03c5f7678a57fd4ca72e72b76a2267166bfb76`,62reviewedfiles;mainahead2,remote31cbb8…,NOTPUSHED;secret/artifactchecksPASS |
| TTDL |Publicguidesready;source khácdata;target/schema+published-source/media packet+scannerrelease+SMTP/MFA/UAT/restore cần cấp/phêduyệt;không deploy |

Pointer `.local/lan-fr14-current.json`. CHƯA PRODUCTION READY. Các mục dưới là lịch sử.

---

# Đối chiếu mới — SQL lookup fix và intro5 đã commit, 27/10/2026

| Hạng mục | Kết quả thực / bằng chứng |
|---|---|
| SQL recovery binding |CONFIRMED scan-cost FIXED: Testactualplan26693rows/1387reads→IndexSeek1row/6reads;guard thực ởTest,không bảngDevcontent |
| Causal limits |Old35s timeoutkhôngtái hiện trongcontrolledbefore2623ms;afterpreflight2122ms;historicalcausalityPARTIAL/AUTO_CLOSEON;không kết luận root duy nhất |
| Migration/safety |20261027025057_AuditRecoveryLookup;maxlengthprecondition/nontruncation;Test→Dev sauverifiedSQLbackup;Id/Actor/ActionSHA nguyên,guardwrongjob/hash vẫnDENY;0grants/config/timeoutchanges |
| Exact intro5 |1dee87ae… /619e3de… ownerapproved/signatureValid;5content5revision5PDF10alias APPLIED/VERIFIED LOCAL |
| Dev/readback |314/325/756/628;5receiptsCOMMITTED_VERIFIED;0mismatch/duplicate;old309/320/309allcolumns+17groups/Identity.env/53NotScanned nguyên |
| Backup |i5dda5e7b7:2SQLchecksumVERIFYONLY/header/SHA+828Devfiles125deps/file-restore-check PASS;SQLrestoreNOTRUN |
| Kiểm tra |OnlyaffectedCLIbuild/SQLplans/guards/2negativebindings/preflight/reconcile/verify;idempotencybyreceipt,no repeatedimport;UI/HTTP/fullregressionNOTRUN |
| Handover |LAN giữ285 vàFR14scannerblock;TTDL/UAT/dataofficial blockers giữ riêng;khôngpush/Production/DNS |

Pointers `.local/sql-fix-current.json`, `.local/apply-intro5-current.json`; PROJECT_STATE. CHƯA PRODUCTION READY. Các dòng BLOCKED bên dưới là lịch sử trước fix/commit.

---

# Đối chiếu mới — intro5 exact-approved nhưng SQL chặn, 27/10/2026

| Phạm vi | Kết quả thực |
|---|---|
| Exact approval5/5/5/10 |Owner confirmed1dee87ae…/619e3de…;unsigned review;signature NOT RUN |
| Defender/proof |Local5byteSHA/current217/TTL PASS;SQL inventory-binding verification BLOCKED_TIMEOUT |
| Preflight / stop-reconcile |2 CLI operations FAIL SQL-2 cùng guard/fingerprint089e3a4e…/transactionNONE;không retry preflight/bypass |
| Backup / Apply |NOT RUN;0newcontent/revision/media/alias;không gọi safe-apply |
| Đích/read-only readback |Dev309/320/751/618/53NotScanned;0batch ImportRun/0five source identities;không cần rollback |
| Verify/idempotency/HTTP |NOT RUN_NO_APPLY;không cộng kết quả lượt trước |
| Tiếp tục |SQLUNKNOWN/MONITORING;fresh guards sau khắc phục;approval exact vẫn giữ nếu packet không đổi;hash mới cần duyệt lại |

Pointer `.local/apply-intro5-current.json`, PROJECT_STATE. CHƯA PRODUCTION READY. Các bảng dưới là lịch sử.

---

# Đối chiếu mới — LAN và intro5, 27/10/2026

| Hạng mục | Trạng thái / bằng chứng |
|---|---|
| Intro5 | READY_FOR_EXACT_OWNER_APPROVAL; `1dee87ae-9856-42fc-9152-b14d1a8bc5af` / `619e3de20dd1dcd331269ce28d0df505b01358d73966f00524efedc1aac89c85`;5/5/5/10;strict native5Clean217/freshpreflight/reconcile PASS;0sign/DevApply. Old206proofstale, không tái dùng |
| FR08 LAN | HOST HTTP PASS: thống kê trống đúng, không số liệu giả |
| FR14 LAN | UI DEPLOYED; upload/preview BLOCKED_LAN_SCANNER_POLICY; chưa xác minh UI bằng MFA BA, không bypass |
| FR15 LAN | HOST HTTP PASS: popular7/30+related;8smoke riêng,không gọi fullsuite |
| Bảo toàn LAN |68bảng0diff/285content/3accounts/5introPublished/config/MFA/passwords/roles nguyên; SQL+706file backupPASS;SQLrestoreNOTRUN |
| Dev |309/320/751/618/53NotScanned nguyên;0newimport/approval/DDL |
| Handover | LAN đang chạy5188; BA client chưa test trong lượt này;Web+SetupbuildPASS;khôngpush/Production/firewallchange |

Pointer `.local/lan-update-current.json` và `.local/intro5-current.json`. CHƯA PRODUCTION READY. Các kết quả dưới là lịch sử.

---

# Đối chiếu hiện hành — SRS v1.2 bổ sung FR-08/14/15, 26/10/2026

| Tiêu chí | Kết quả hiện hành | Bằng chứng / điều kiện còn thiếu |
|---|---|---|
| FR-08 | IMPLEMENTED / LOCAL SQL-HTTP PASS; dữ liệu chính thức NOT PROVIDED | 34 tỉnh, năm/đơn vị/nguồn, lọc/sort/tổng; trang trống đúng khi chưa công bố. Không có số liệu giả trên Dev/LAN |
| FR-14 / AC14.1 / BR17–19 | LOCAL SQL TEST PASS; UAT nghiệp vụ NOT RUN | Native Defender206; 6 cột,34mã,lỗi dòng/cột,Block/Warn,0dataset rows khi lỗi; preview/confirm/publish exact revision, quyền service, thay kỳ transaction/history. Scanner Dev/Production vẫn không tự bật |
| FR-15 / AC15.1 | LOCAL SQL TEST + HTTP PASS | 7/30 ngày; 30phút chống lặp trên SQL hiện có; tag→category→recent,max5; gỡ bài loại ngay; home/sidebar/bottom. Không chứng minh chống mọi bot/cookie reset |
| FR-16 / AC16.1 | Giữ manual/version/restore đã có; AC16.1 PARTIAL | Không sửa/parser-rerun; tuần43 thật10/10 giữ nguyên, thiếu2Word tuần khác |
| Schema/backup | Test→Dev đúng1migration PASS; SQL restore mới NOT RUN | 20261026095148_LandAreaStatistics; backupstb031165a,828files+2SQL+2deps,checksum/VERIFYONLY/hash/file restore-check PASS; không cấp quyền/Identity |
| Bảo toàn nguồn | PASS focused17nhóm0diff | Dev309/320/751/618,53NotScanned nguyên; Test4912Contents gồm fixtures,314matched source ContentIds; 0new source import |
| Intro5 | READY_FOR_APPROVAL;0sign/Apply | b001278b-77a7-47c0-a05f-8c1cadd1a88f /03ced73570f218115e4aa3733846591f47351c01d1fb53dd51fa37f2a7beadc6;5/5/5/10;strict5nativeClean206,preflight/reconcile0commit |
| Kiểm tra cuối | 23focused PASS;7HTTP PASS riêng; full regression NOT RUN | `.local/completion-current.json`; không cộng các lượt sớm. Raw HTTP ban đầu cùng run bị ghi đè, chỉ final timestamped7 được dùng; incident lịch sử giữ nguyên |
| LAN/Git/TTDL | LAN cũ đang chạy; cập nhật LAN NOT RUN; UAT target NOT PROVIDED | Không áp giao diện mới/ZIP/push/deploy;1localcommit+WIP chưa bàn giao remote; cần cấu hình và nghiệm thu TTDL |

Chi tiết/checkpoint tiếp tục trong PROJECT_STATE. CHƯA PRODUCTION READY. Các bảng dưới là lịch sử.

---

# Đối chiếu hiện hành — approved6 Dev, 26/10/2026

| Phạm vi | Bằng chứng | Trạng thái |
|---|---|---|
|Exact batch6 |2293ce8f-690b-41e0-a000-e28d69a94e06 / 0bd8d697885c0bb1ead2e59ebd100212e889fcdd3e071b8ac6fa559797a774d0;thực6content6rev8asset12alias |APPLIED/VERIFIED LOCAL |
|Guard/approval/backup |Strict8current206/exactbytes;Authenticode/hash/scope;backup820files+2SQL+144deps CHECKSUM/VERIFYONLY/file-restore PASS |PASS LOCAL;SQLrestore backup mới NOT RUN |
|Readback/bảo toàn |Dev309/320/751/618;6COMMITTED_VERIFIED receipts;0mismatch/duplicate;allold303/314/303+17nhóm/Identity.env/53NotScanned nguyên |PASS;idempotency bằng receipt,không import lại |
|Smoke |Dev article200/source-unavailable404,host5399 đãdừng;không fullsuite |PASS focused |
|Ngoại lệ |3623 infographic cònBLOCKED;7link file chưa phục vụ và31URL/33ảnh phụ còn đối soát;5intro chưa DevApply |PARTIAL ngoài batch;UATTTDL còn chờ;SQLUNKNOWN/MONITORING |

Bằng chứng .local/apply6-current.json và PROJECT_STATE. CHƯA PRODUCTION READY. Các bảng dưới là lịch sử.

---

# Đối chiếu hiện hành — phục hồi bài với tệp chưa khả dụng, 26/10/2026

| Phạm vi | Bằng chứng | Trạng thái |
|---|---|---|
|7 hồ sơ |6 bài có HTML độc lập hoặc PDF thay thế hợp lệ;3623 cần infographic đạt policy |6 RESTORED Test/LAN;1 PARTIAL_SOURCE_CONTENT/BLOCKED |
|Tệp chưa phục vụ |Phân biệt nguồn404/budget/bytes chưa đạt định dạng; không fake asset/schema; route public revision và404/backlink |PASS LOCAL; không công khai file chưa đủ điều kiện |
|Scanner và import |11 hash native Clean;8 file hợp lệ có strict SQL proof;Test+6/6/8/12,verify0error |PASS LOCAL;8 media=3ảnh/5PDF;không53Devpromotion |
|Excel và ảnh phụ |Giữ7/31/33;2 bảng23 cột,filter/link,0formula;12 bài LAN không hotlink ảnh thiếu |PASS;không gọi là chuyển đủ mọi tệp |
|Bảo toàn/runtime |LAN285/285/703/570/3accounts;19bảng cũ0diff;Dev303/314/743/606/53NotScanned;buildcuối/HTTP PASS |PASS LOCAL;SQLUNKNOWN/MONITORING;CHƯA PRODUCTION READY |
|Packet Dev6 |2293ce8f-690b-41e0-a000-e28d69a94e06 / 0bd8d697885c0bb1ead2e59ebd100212e889fcdd3e071b8ac6fa559797a774d0;6/6/8/12;strict8/preflight6absent0error |READY_FOR_APPROVAL tại kiểm tra;chưa ký/Apply;phải freshguard/backup sau exactapproval |

Pointer .local/source-unavailable-current.json; thông tin mới và thời hạn proof ở PROJECT_STATE.


---

# Đối chiếu trước — phản hồi BA/Giới thiệu, 26/10/2026

| Phạm vi | Kết quả tại lượt trước |
|---|---|
|Hai trang HTTP500 |Sửa explicit Article view;2URL200,đủ5 trang chức năng mới;chờ BA retest |
|Mapping5 |StaticPage3060–3064;LAN thêm5/5/5/10;readback0diff/duplicate;không DevApply |
|Menu ngoài |Mail đúngURL/tab/rel,TLS HEAD302;DVC menu động;ppd.gov.vn là link ngoài |
|Runtime lúc đó |LAN279/279/695/558/3accounts;Dev303/314/743/606;build và5HTTP PASS |
|Dev5 |Draft566bc345-08d6-437b-b6db-1c3638ee82fb;thiếu strict proof;không thuộc packet6 mới |

Các bảng phía dưới giữ nguyên lịch sử.


---

# Đối chiếu hiện hành — LAN demo BA SRS v1.2, 26/10/2026

`lan-20261026T035508Z-49ab82`, `.local/lan-demo/current.json`: **RUNNING / LAN HOST READY — CLIENT NOT VERIFIED**. Không phải UAT/Production.

| Phạm vi | Bằng chứng | Trạng thái |
|---|---|---|
| Kestrel LAN |192.168.0.122:5188 bind đúngIP; process nền độc lập, start/stop ownershipguard;home/admin200,3nhãnFR17/EnglishName/CAPTCHA-CSRF |PASS HOST;máyBA NOT VERIFIED |
| SQL/private files |DBLandPortal_LanDemo mới riêng,migration schema riêng;274public source revisions/690Clean copiedSHA,0unsafe;khôngIdentity nguồn/citizen/draft/OTP/keys/crawler |PASS LOCAL;không copywholeDevTest/53NotScanned |
|3vai trò |lan-ba-writer/reviewer/admin,mật khẩu thậtIdentitycheck PASS,roleboundaries PASS;passwordchange gate/lockout/MFArequired/CAPTCHA giữ;credential file ngoàiRepo ACLcurrentuseronly |PASS cấu hình;BA phải đổipass/enrollTOTP;không resetDev |
|Firewall/network |EthernetPublic giữ,khôngrulemới;readfilter AccessDenied |UNVERIFIED/BLOCKED operator inspection;CNTT duyệtnguồn IP/subnet vàTCP5188,noclaimremotePASS |
|Bảo toàn/giới hạn |Dev303/314/743/606,17groups0diff/Identity.envsame/53NotScanned;Web+SetupbuildPASS;2HTTP focused |PASS LOCAL;uploadscanner/SMTP/workers không bật,video/lịch/hỏiđáp dữliệu trống đượcghi rõ;no fullsuite |

Guide:docs/lan-demo-ba-guide.md;khôngmật khẩu trongtài liệu/ZIP/Git. SQLUNKNOWN/MONITORING;CHƯA PRODUCTION READY. Dưới là lịch sử.

---

# Đối chiếu hiện hành — approved QLDD12, 26/10/2026

Run `apply12-20261026T031931Z-5b87ad79`, `.local/apply12-current.json`. **APPLIED/VERIFIED LOCAL, CHƯA PRODUCTION READY.** Không replay batch đã commit.

| Phạm vi | Bằng chứng thật | Trạng thái |
|---|---|---|
| Exactapproved batch12 |`70686b95-15aa-4c83-883b-2c5b2b446826` / `6e0393ec6411ba9e600d8ae04ce77a00347f2f8808f9c332175518726b4d13b3`, targetDev;12content12revision8media(6ảnh2PDF)24alias thực commit | APPLIED/VERIFIED LOCAL,12durableCOMMITTED_VERIFIED receipts;0mismatch/duplicate;không reimport |
| Guard/backup |8nativeproofClean current206/bytes/SQLreceipts,0rescan;Authenticode exactapproval/cert/pin/EKU/SID PASS;freshpreflightPASS;backup812files+2SQL+147deps CHECKSUM/VERIFYONLY/HEADER/SHA/file restore0diff | PASS local;SQLrestorecủabackupnàyNOTRUN,restore291lịch sử giữ riêng |
| Readback/bảo toàn |Dev303/314/743/606;old291content302revision291sourceallcolumns+17groups0unexpecteddiff,Identity.envsame/catalogsame/53originalNotScanned;3HTTPsmokePASS,bài/301alias/mediaSHA | PASS local;host5399 đãdừng;no fullregression/unrelatedtests/push/Production |
| Ngoại lệ/UAT |33optionalrefs/31URLảnhthiếu reviewNON-BLOCKING giữ,7essentialBLOCKED,5intro chờBA/category60;SQLUNKNOWN/MONITORING/0timeout mới | PARTIAL ngoài batch;TTDL infrastructure/scannerSMTP/MFA/UAT/ATTT/RPO-RTO cònchờ,khôngProductionReady |

Các bảng dưới là lịch sử; không dùng READY cũ để Apply lần nữa.

---

# Đối chiếu hiện hành — strict proof candidate12, 26/10/2026

Run `strict-20261026T025042Z-957ede6b`, `.local/strict-current.json`; **READY_FOR_APPROVAL, chưa ký/chưa Apply; CHƯA PRODUCTION READY.**

| Phạm vi | Bằng chứng thật | Trạng thái/còn chờ |
|---|---|---|
| Pipeline optional/strict proof |4fileCLI sửa hẹp;review hash-bind inventory/proof+plan, reproduce raw/fingerprint/exactTestrevision assets;build0warning/error,6negativeguards PASS | PASS LOCAL vùng sửa;không nới scan/signer/omission |
|8media bắt buộc của candidate12 |8nativeDefenderClean/defs1.461.206.0/SQLreceipts/capture-Test-quarantineSHA0diff;reuse8/0native mới,0Dev53scan | PASS tại kiểm tra;TTL đến2026-10-27T02:51:28.9432537+00:00,definitionsdrift vô hiệu ngay;33ảnh phụ/31URL vẫn thiếu |
|Batch trình duyệt |`70686b95-15aa-4c83-883b-2c5b2b446826` / `6e0393ec6411ba9e600d8ae04ce77a00347f2f8808f9c332175518726b4d13b3`,12content12rev8ảnh24alias,fullrows giữ nguyên;fresh preflight/reconcile12ABSENT0commit0mismatch | READY_FOR_APPROVAL;unsigned review,sau exactapproval mới ký+freshbackup/guards/Apply;oldcandidate chưaApply |
|Bảo toàn |Dev291/302/735/582,53NotScanned,17nhóm0diff/Identity.envsame;Test không nhập lại12,8append-only scan receipts mới | PASS LOCAL;7essential/5catalog/UAT/SQLUNKNOWN/MONITORING/incident lịch sử giữ riêng |

Không fullregression/HTTPUI/crawl/restore/ZIP/push/DevApply; các bảng dưới là lịch sử.

---

# Đối chiếu hiện hành — dữ liệu, actual restore và triển khaiTTDL, 26/10/2026

Run `delivery-20261026T020220Z-1feeba76`, `.local/finish-current.json`. **PARTIAL toàn dự án / CHƯA PRODUCTION READY.** Không fullregression, DevApply/sign/push, ZIP/ảnh/newphase/deploy.

| Phạm vi | Bằng chứng và kết quả thật | Còn chờ |
|---|---|---|
| 5intro/category60 | Đối chiếu đủ5rawSnapshot/anchor/categoryTest, đề xuất Giới thiệu/Chức năng nhiệm vụ cho StaticPage3060–3064;0Devchange | BA/authority duyệt catalog/mappingDev; chưa coi Teststage là approval |
| 19staging | Test thêm12realcontent/12rev/8media/24alias;verify/receiptCOMMITTED_VERIFIED0mismatch/duplicate,8SHA/meaningfultext/date/source;563oldsourceRows/crawlerpointer/categories giữ |12case PARTIAL_MEDIA:33optional references/31URLs thiếu được reviewNON-BLOCKING;7essential vẫnBLOCKED, không fake/hotlink/omission unsafebytes |
| Quét/an toàn tài nguyên | Tái dùng16hashcachegenuineClean lịch sử,8vàoTest;0native rescan/0Dev53scan/promotion;0newvaliddownloadedfile | Không nhận historical proof là strictcurrent;53DevNotScanned giữ;8newDevassets chưa có currentproof/recoveryinventory phù hợp optional review |
| Devcandidate12 | Readonlyplan2d2b7b31-a6aa-4088-9ad7-f11f8786b232/SHAe52e18d4983d55c9585971ac81dedb6532c6fc07254cecd998e9af5321e3e8d3,12/12/8/24 | **BLOCKED_NOT_READY_FOR_APPROVAL**,proof=null,0sign/Apply;không legacy/no-proof bypass |
| ActualSQLrestore291 | Freshbackupf291178de53/812files+2SQL+17deps.1restore vàoLandPortal_Restore3J_5fef5a3eREAD_ONLY;DBCC/schema/PK-FK-index/Identitypermission/allcolumns0diff;291/302/735/582;735mediaSHA/68snapshotSHA/3PDFread PASS | **PASS LOCAL actual restore**, khôngđèDev/Test/khôngWebWorker;targetTTDL/RPO-RTO NOT RUN;DBgiữkhôngDROP |
| Git/source triển khai | SDKvsHostingBundle/ANCM/MigrationID1.2 rõ;IISscriptpinIntegrated/NoManagedCode/x64;20offlineprep checks PASS. Commitlocal36e8e230731fa737f30e8e5c8bab5feb66efc4c7 chỉ2opsfiles | Chưapush,mainremote31cbb8b7;IIS/service/SQLtarget chưacấp,actualinstall/UAT16 NOT RUN |
| Bảo toàn/độ phủ | Dev291/302/735/582/53NotScanned,17nhóm0diff/Identity.envsame;70capture68snapshot8exception2videometa0file. Test304knowncanonical publicsource revisions;raw4906contents gồmfixture |315known=291Dev+5catalogTest+12newTest+7essentialstaging, toànnguồnUNKNOWN;khônggọi12textimports làfullmedia migration. SQLUNKNOWN/MONITORING/0newtimeout, oldartifactPARTIAL giữ |

Danh sách5mapping,7essential và pointer/hash đầy đủ ở PROJECT_STATE/private run; không cần bộ báo cáo hoặc ảnh mới. Các bảng dưới chỉ là lịch sử.

---

# Đối chiếu trước — approved GitHub push và Dev27, SRS v1.2, 23/10/2026

Run `approved27-20261023T091454Z-42cb8ab4`, `.local/approved27-current.json`. **PUSH SUCCESS / 2batch APPLIED/VERIFIED LOCAL**, không nâng thành nghiệm thu UAT/PRODUCTION READY. Owner exact approval đã nhận, không xin lại hoặc mở rộng sang5/19trường hợp còn lại.

| Phạm vi được duyệt | Bằng chứng thực | Trạng thái / giới hạn |
|---|---|---|
| GitHub RB01 | NormalFF main từab8a56f9 lên31cbb8b7e31cf3e4d45f0d3845474ad200c7e689, blobsecret review0finding; fetch/HEAD/origin/main/lsremote khớp | PUSH SUCCESS, khôngforce/newcommit; đúngrepositoryakayuhuhu0503/websitetintuc, không.env/backup/privatefiles |
| Batch07cdc132 | Exact manifestba4c2bb44264322d7975f7e4f522146b3c2975ff4fdeddbfe02ece2a84457d14;13content/13revision/60asset/26alias;receipt13COMMITTED_VERIFIED/verify0error | APPLIED/VERIFIED LOCAL; Dev277/288/675/554 saubatch1 |
| Batchdb22acdb | Exact manifest50e7464c8a88e9721e4b5c9103b578213a2eb12e2e527996034bb16f6cddd0c7;14/14/60/28;receipt14COMMITTED_VERIFIED/verify0error | APPLIED/VERIFIED LOCAL; Dev291/302/735/582 saubatch2 |
| Approval/Defender/preflight | 2AuthentiCodeValid,chain/EKU/date/thumbpin/SID/hash/scope;120strictClean byte/SQLreceipt/currentdefs156/engine/TTL;4initial/freshpreflight0error | PASS,0rescan/0Dev53scan; giữ toàn bộ guard; targetMinhAnh\SQLEXPRESS/LandPortal_Dev |
| Recovery points | a271d30a575 state264:692files+2SQL+353deps; a27212831a9 state277:752files+2SQL+360deps;SQLCHECKSUMVERIFYONLYHEADER/hash,0missing/mismatch,fileRestorePASS | BACKUP PASS; actualSQLrestore hai bản này NOT RUN; rollbackNOT_NEEDED |
| Bảo toàn/nguồn thật/idempotency | Old264contents/275revisions/264SourceRecords allcolumns+17nhóm0diff; Testsource/hash/date/author/category/alias match;53gốcNotScanned;Identity.envsame;0duplicate | PASS; durable receipts/readback, không repeated import;70capture68snapshot8exception2videometa0file giữ |
| Kiểm tra gọn / tình trạng tổng thể | Verify2/independent2 vàHTTP6 bài/301/mediaSHA PASS;final17groups0diff;khôngfullsuite/ảnh/ZIP/newbuild/deploy | SQLrootUNKNOWN/MONITORING,0newtimeout;315known=291Dev+5catalogpending+19staging, toànnguồnUNKNOWN;TTDL/UAT/ATTT/restore/source release còn riêng |

Trưởng phòng có thể pull exact commit đã push và build theo README/scripts hiện hành; hạ tầng đích chưa cấp, chưa nghiệm thu16caseUAT. Approval này đã hoàn tất, không replay hai packet hoặc batch9unsigned cũ. Các bảng dưới là checkpoint lịch sử.

---

# Đối chiếu trước — bàn giao nguồn/SRS v1.2, 23/10/2026

Run `handoff-20261023T084039Z-a3146827`, `.local/handoff-current.json`; **không full regression, không push/ký/Apply Dev**. Những hàng dưới chỉ là phạm vi có bằng chứng mới, không nâng toàn dự án thành READY FOR UAT/PRODUCTION READY.

| Tiêu chí | Kết quả thực | Còn chờ |
|---|---|---|
| SQL bộ chọn nội dung | 6 actualEF Dev/Test16–123ms; exactcommand/parameter, actualplan/IO operator và own-SPID sampling;3HTTP authenticated200. Projection hiện có giữ,0index/migration/timeout/pooling change | ROOT CAUSE UNKNOWN/MONITORING; grant cũ212000KiB/used1440,GrantWait0 không chứng minh nguyên nhân lịch sử; query nhanh có samplegap |
| RB01 source handoff | PASS LOCAL: explicit140paths/492file secret review, commit31cbb8b7e31cf3e4d45f0d3845474ad200c7e689, main/đúngremote; historygiữ,trackedtreeclean | CHƯA PUSH; owner cần duyệt đúngrepo/branch/commit. Hồ sơ nội bộ untracked giữ ngoài commit |
| Build/publish nguồn riêng | PASS: export staged tree không.env, npmci/build, Web/Worker5mode/CLI/offlineschema/326hashfiles; README+UAT16 đúng1.2 | PUBLISHED_NOT_DEPLOYED, chưa cấp targetTTDL/SQL/HTTPS/service/scannerSMTP/MFA;16caseUAT NOT RUN |
| Coverage QLDD | 315knowncanonical=264Dev+32Testpending+19held; batch9unsigned cũ overlap9/0additional; không coi technicalFixture là nguồn | Toàn nguồn UNKNOWN;5/32 thiếuDevcategory60,19staging/41assetproblemreferences có overlap, chưa đóng |
| Consolidated approval | READY_FOR_EXACT_OWNER_APPROVAL: indexSHA7b5c988c078f70fe5bcdd976f0adbe8a70dea88bef466b471ae3d56959cbfcd4;2packet13+14content/27revision/120asset/54alias;2freshDevpreflight ABSENT/0error, pairwiseidentity/alias disjoint | Chưa ký/Apply; exactID/hash trong PROJECT_STATE và privateindex. Không reuse approval9; futurefreshproof/target/backupSQL+media+signedapproval/preflight bắt buộc |
| Strict source scan | 120nativeClean Test/0Infected/0ScanFailed, currentengine1.1.26090.9/definitions1.461.156.0/hash/TTL/SQLreceipt PASS; backupTest trước append proofaudit CHECKSUM/VERIFYONLY | Earliest24/10 08:51:52UTC hoặc definitionsdrift; không quét53Dev/không lấyTestproof làmProductionrelease; SQLrestore bản backupnày NOT RUN |
| Bảo toàn dữ liệu | PASS: Dev264/275/615/528,53gốcNotScanned,17nhóm0diff/Identity.envsame;Test4894/6965/1844/857 raw gồmfixture,sourcecounts không đổi |0sourceimport mới trong lượt;70capture68snapshot8exception2metadata0videofile giữ;V04/2Dartifact PARTIAL và restore/UAT/ATTT/source release vẫnriêng |

---

# Đối chiếu trước — SRS v1.2, 23/10/2026

SRS v1.2 là chuẩn chính thức, thay1.1. **Delta PASS LOCAL có bằng chứng; UAT PUBLISHED/NOT DEPLOYED, toàn dự án PARTIAL/CHƯA PRODUCTION READY.** Run `srs12-20261023T074035Z-b41b1efc`, `.local/srs12-current.json`. Không dùng kết quả137/117/108cũ cho code này.

| Tiêu chí | Hiện trạng thực | Bằng chứng / còn chờ |
|---|---|---|
| FR01 English/logo | PASS LOCAL: tên chính thức header/footer/title/meta/OG, fallback tập trung/configversioned; không tạo logo | `SitePresentation`, layout; `browser-readback.json`1440/390. Logo theo1.2 chưa cần |
| FR01 menu CRUD/AC01.1/01.3; FR17 navigation | PASS LOCAL/DEV APPLIED qua adminHTTP hợp lệ: mọi menu sửa/xóa/bật-tắt/cha-con/tab;3đích FR17, mobile dùng được; đủ14chuyên mục giữ | `test-picker/http-readback.json`,`dev-site-workflow.json`,`browser-readback.json`; không sửa SQLconfig/grantoldaccounts. Dịch vụ công WAITING_FOR_CONFIGURATION, khônglinkgiả. Thư điện tử live https://mail.mae.gov.vn, sửa/xóa được |
| FR11 / AC10.1 | PASS LOCAL: đúng3demo Dev+Test, writer khôngđăng/75scope; reviewer duyệt/đăng/gỡ,khôngadmin; admin đủquyềnmenu/account | `DevDemoAccounts`,scriptbootstrap/disable;randomDPAPICurrentUser/14ngày/forcedchange;oldIdentity7nhóm0diff;repeat0new. KhôngcopyUAT/Production, khôngtắtMFA/CAPTCHA |
| FR07 tra cứu; BR06/10,NFR07 | PASS LOCAL SQL/HTTP: receipt128bit+email+one-useCAPTCHA đúngpurpose,khôngOTP;genericmiss,SQLrate,answertext/noPII/file/cache;oldtoken410 | `InquiryCaptchaLookup`,controller/view;`test-picker`5nhóm (khôngfullsuite). ATTT/abuse UAT thật chưa nghiệm thu |
| RB01 source→Trưởng phòng build/install | PUBLISHED LOCAL / NOT DEPLOYED; schemaoffline không.env/SQL cóUNALLOCATEDguard,kit.NET10/Web/5worker/CLI | `offline-source-build-check.json`,`uat-package.json`;ZIP326files+manifest0hashdiff. Gitpathlist140candidates/chưastagecommitpush; cầnfinaldiffreview WIP và xác nhậnpush |
| Schema và dữ liệu | PASS LOCAL Test→Dev migration20261023074121;264/275/615/528,53NotScanned/17nhóm0diff;oldidentityenvpreserved | `backup-final`CHECKSUM/VERIFYONLY+692filerestore;SQLrestore bản này NOT RUN.0sourceApply,0scanpromote53 |
| SQLtimeout | **UNKNOWN/MONITORING, có tái diễn30.3s** ở bộ chọn nội dung; đã tránh truy vấn payload thừa tại cấu hình | rawweb.log/reconcilegeneration1;codepicker/Test/devworkflowđạt sau đó, không chứng minhrootcausekhác đã sửa;0timeoutincrease |
| Nghiệm thu toàn dự án | PARTIAL | TTDLtarget/scannerSMTP/MFA/ATTT/restore/UAT/sourcecoverage+release/approval còn riêng. V04/2D artifactPARTIAL giữ;AC16.1 chỉ1/3tuầnthật;FR08/14/15 tháng11 |

Các bảng lịch sử dưới đây là snapshot của phiên bản cũ; quyết định khóa tra cứu, chờ tên Anh/logo và khóa menu do thiếu tài khoản ở1.1 đã được delta1.2 thay thế trong phạm vi nêu trên. Không làm lại bộ hồ sơ tiến độ/ảnh hoặc full regression.

---

# Nghiệm thu hiện hành — P0 SRS v1.1 / gói UAT, 23/10/2026

> UI PDF kiểm tra thêm: Chrome155 accessibility tree có69trang/native controls với sandbox hiện có; không coi DOM trống là lỗi render. Thử nghiệm bỏ sandbox đã loại, không dùng làm kết quả/không triển khai. Host Test tạm đã dừng; sau browser17sourcegroups vẫn0diff. Không ảnh mới hoặc rerun fullsuite.

Run `uat-delivery-20261023T050804Z-0945989d`, `.local/uat-delivery-current.json`. **PASS LOCAL delta; PUBLISHED_NOT_DEPLOYED; PARTIAL toàn bộ / CHƯA PRODUCTION READY.** Không fullregression, không báo UAT16 đã chạy.

| Phạm vi bắt buộc | Bằng chứng/kết quả | Phân loại phần còn thiếu |
|---|---|---|
| FR01/02 header/footer/home | Code/local PASS: giờ VN, slider từ live banner, English/legal/SEO draft-live/hashlegacy, stats SQL có index/cookie random không PII; Edge1440/390 PASS | CONFIG/DATA: logo, dữ liệu chính thức, banner/menu live; DVC chờ Trưởng phòng |
| FR03–05 | Giữ module và dữ liệu đã đạt, smoke bài/chuyên mục/search nguồn thật PASS; không làm lại | DATA/UAT: độ phủ nguồn chưa exhaustive; không lấy smoke làm full FR acceptance |
| FR06 PDF/counters | Local PASS: chỉ Clean được inline/frame same-origin sandbox; counter SQL đúng GET, bỏ HEAD/Range/unbound; nativePDF actualHTTP + SHA/private revoke | UAT: browser/target/ATTT thật chưa chạy; legacy53 trạng thái giữ, không được hiểu là Clean |
| FR07/09 | Giữ CAPTCHA/public moderation/private inbox/video direct đã có, không thay YouTube; không chạy lại module không đổi | CONFIG/UAT: scannerSMTP/MFA đích; private lookup khóa chờ phương án được duyệt |
| FR10 | Local PASS: mẫu bắt buộc khi submit/publish, draft vẫn lưu; covercaption/tags/featured revision; public không đổi, approval cũ bị chặn; normalized500 guard | UAT: cán bộ thực hiện U01–04; không tự cấp quyền |
| FR11–13/17 | Code workflow quyền/cấu hình giữ; mới có contactEnglish/legal/SEO inputs và preview. ActualDev0account đủ2site quyền,0Disclosurelive | CONFIG/RIGHTS BLOCKED: admin có thẩm quyền xác nhận catalog và áp dụng3menu; không xin PO đổi tên nhóm2 |
| FR16 vận hành | Manual/revisions giữ PASS LOCAL lịch sử, không rerun parser; Word43TTDL10/10 giữ private | DATA/UAT: AC16.1=1/3, còn2Word thật, không chặn phương án nhập tay; không coi lịch TTDL là lịch lãnh đạo Cục |
| Chuyển QLDD | Test mới18/18rev/68asset/36alias;2verify/repeat0delta; finalCLI readbackDataSHA stable.315canonical=264Dev+32Testpending+19held;322typed/1681URL | DATA/CODE/POLICY: whole source UNKNOWN;32chưa có executable freshDev approval packet;19held cần giải quyết, không gộp ảnh/PDF lỗi thành19bài hỏng |
| Bảo toàn NFR04 | Dev264/275/615/528,53NotScanned,70capture68SQL/8/2meta0file;17groups0diff/Identity.envsame;3additive EF migrations Test→Dev sau backup | UAT/RESTORE:692file restore PASS, SQLbackup checksum/verify PASS; actual SQLrestore264/schema mới NOT RUN, không dùng250 lịch sử thay |
| UAT deploy code | Kit326files/ZIP327entry0hashdiff;Web5worker/IIS/TLS/SQLCMD/config/preflight; UAT initializer exact target/authority/catalog transaction, Test no-op & config negatives PASS | INFRA/UAT: target UNALLOCATED, actual empty DB init/IIS install/worker E2E NOT RUN; không copySQLTest/Identity |
| Scanner NFR03 | Source70newhash nativeClean, no rescan oldClean/Dev53. NativeEICARInfected/allowlist denial thực. OnPrem Staging identity/pin/privacy fences + readonly probe | INFRA/POLICY/CODE: actual TTDL engine E2E BLOCKED; nguồn ngoàiDev còn release integration/policy, không lấy proofTest tự release |
| NFR01–04 tổng | Một phần LOCAL: HTTPS/private roots/SQL target/auth guards, ACL/source preservation/counters;3config groups+20ops riêng | UAT/ATTT: TLS/certalerts/MFA/scannerSMTP/performance/restore/cutover cần môi trường thật, chưa NFR toàn bộ PASS |
| Final relevant checks | Final build0warning/error+npm PASS;5risk groups các lượt riêng; sau sửa cuối template được rerun;Test9HTTP PASS+1NOTRUN(legacy asset absent),Dev8readonlyHTTP PASS riêng | Không fullregression/ảnh mới/video tests/không cộng chồng các run. Test unavailable được ghi NOTRUN, Dev actual legacy frameDENY riêng |

Migration mới: `20261023051752_EditorialArticleTemplate`, `20261023055032_AnonymousPublicTraffic`, `20261023060018_Srs11SitePresentation`. Backup/manifest và logs trong run root; gói cuối `.local/publish/uat-delivery-20261023T050804Z-0945989d-secure-uat.zip`. Full chi tiết/checkpoint/lệnh trong PROJECT_STATE và hướng dẫn hiện có; không lập lại hồ sơ báo cáo tiến độ.

SQL root UNKNOWN/MONITORING; V04/2D artifact incidents PARTIAL giữ nguyên.0newDevSourceApply/0sign/0env/grant/Production/DNS/push/email thật. Nuke và FR08/14/15 không mở rộng trong lượt này. Các PASS cũ bên dưới là lịch sử, không tự biến thành test code mới.

---

# Nghiệm thu delta BA/SRS v1.1 — 23/10/2026

Run `ba-feedback-20261023T040315Z-eae85c26`, `.local/ba-feedback-current.json`. SRS1.1 đã đọc từ DOCX thật, thay mâu thuẫn1.0; không tạo lại hồ sơ tiến độ. Trạng thái chỉ áp dụng phần có bằng chứng.

| Tiêu chí | Kết quả cuối / giới hạn |
|---|---|
| FR06 ngày Việt Nam | PASS LOCAL: nhập textdd/MM/yyyy, DateOnly binder/SQL;04/11 là4tháng11 kể cả browseren-US; ISO cũ vẫn đúng; sai ngày/range/query lặp400 |
| Admin tiếng Việt | PASS LOCAL delta: ánh xạ toàn bộ mã quyền/Workflow/Scan/Video, validation và thông báo Identity; không đổi mã/API/quyền. HTTP/browser dashboard/roles/site/video/scheduler/editor; không full kiểm tra mọi màn hình hoặc audit mọi lỗi runtime |
| FR07/09 biểu mẫu | PASS LOCAL: CSS/nút/chọn tệp VN/focus/loading, CAPTCHA thật POST→SQLprivate Pending; CSRF denied; không public PII; MailDisabled |
| FR01/17 menu | PASS LOCAL Test: đủ3tên/đích, empty page200, idempotent draft/live, nguồn cũ giữ nguyên. **Dev BLOCKED cấu hình/quyền**:0account đủManageSite+PublishSite, không tự grant hoặc viết menuDev |
| FR17 danh mục | PASS LOCAL negative: quyền bị thu hồi không kích hoạt bằng actor cũ; rowversion/audit/guard hồ sơ đang công khai. Tên thứ hai đã chốt, không hỏi lại PO; người có quyền vẫn cần thao tác áp dụng |
| FR16 mẫu tuần43 thật | PASS LOCAL parser10/10 và private Test preview10dòng sửa được; merged/multiline/TP/`*)`/provenance giữ; buổi Chưa xác định cần cán bộ chọn. GốcSHAf982f986…a2552e; không public lịch Trung tâm thành lịch Cục; không fake Clean |
| AC16.1 ba tuần | **BLOCKED:1/3tuần thật**. Quét/upload thật cho chính Word nghiệp vụ này NOT RUN; fixtures7/8cột chỉ kiểm tra kỹ thuật, không UAT |
| SRS1.1 RB01/FR09 | Config/hướng dẫn **TTDL**; giữ IIS/SQL/Web5worker. Direct upload/internal MP4 player/seek/Range206 và từ chối publish video không tệp PASS Test; scanner/FFmpeg engine không chạy lại; khôngYouTube mới/không xóa dữ liệu lịch sử |
| UI NFR02 delta | PASS LOCAL Edge/Chromium1440/390px, nooverflow/JSerror,2ảnhContact; Safari/Firefox/thiết bị vật lý NOT RUN |
| Final code/build | `validated-build`0warning/error; `validated-tests/ba-tests.json`5/5nhóm tập trung PASS. Giữ rawfail trước; **không full regression**, không cộng thành suite/UAT16 |
| Dữ liệu | Dev264/275/615/528,53originalNotScanned,70/68/8/2meta0file;17nhóm0diff/Identitydigestsame. Test fixture không tính chuyển dữ liệu;0sourceimports/0migration/0DevSQLwrite |
| Runtime/UAT | Final local5188loopback/6readonlysmoke200 riêng; Devmenu chưa áp dụng. Kit UAT lịch sử chưa chứa delta BA. Target/scannerSMTP/MFA/security/restore/UAT còn riêng; CHƯA PRODUCTION READY |

Các mục PASS lịch sử bên dưới không tự biến thành kết quả kiểm thử code hiện tại. Root SQL UNKNOWN/MONITORING và incident artifact PARTIAL giữ nguyên.

---

# Cập nhật nghiệm thu P0 local — 23/10/2026

Run `p0-ready-20261023T025552Z-35443a36`; [báo cáo](readiness-completion-report.md). Phần bên dưới giữ lịch sử; không sửa hồ sơ đã đóng gói.

| Tiêu chí | Trạng thái mới / bằng chứng |
|---|---|
| FR02 BR03 | PASS LOCAL delta: ẩn block lỗi/rỗng, không render correlation/stale card; operational log giữ lỗi |
| FR04 BR08 | PASS LOCAL delta: nhãn chính thức, real HTTP vẫn encode câu trả lời |
| FR12 ContentId filter | PASS LOCAL delta: SQL20+1/paging, category scope trước filter, invalid inputs400, không lộ bài ngoài scope |
| FR16 AC16.2 | PASS LOCAL mới: release v1/v2/v3, original DOCX hash/history/Range, unpublished version404, withdrawal GET/HEAD/Range404; tamper giữ ScanFailed |
| FR16 AC16.1 | BLOCKED riêng: thiếu3Word nghiệp vụ thật; fixtures không thay UAT, không nâng toàn FR16 thành PASS |
| FR11 login audit / viewer | PASS LOCAL delta: password outcomes SQL audit, global-admin-only metadata viewer, invalid date400/permission revoke403/PII Detail không SELECT |
| Bootstrap UAT policy | PASS LOCAL: chung Web/CLI≥14, missing/weak rejected before role/account writes; không bootstrap/reset/grant Dev |
| Admin SQL paging | Focused HTTP PASS sau narrow ID paging; timeout SQL-2 ở Test lần đầu là FAIL thật còn raw. Root UNKNOWN/MONITORING, không kết luận fixed |
| Dữ liệu | Dev264/275/615/528,53NotScanned;17groups0diff/Identity.envsame;0real Test/Dev import, migration unchanged |
| Final focused run | `checked-boundaries/p0-tests.json`3/3 PASS; Release HTTP11/11 và ops17 riêng, không full regression/ảnh mới/UAT16 PASS |
| UAT kit |319file ZIP/hash readback0diff, Web+5worker+ops+16case PREPARED; target UNALLOCATED, không deploy |
| Toàn P0 | OPEN_CODE FR10/FR06/header-footer/menu-slider-traffic/FR17activation; BLOCKED target/scannerSMTP/releasepolicy/privateLookup/ATTT/restore/UAT. Chưa READY FOR UAT toàn bộ, CHƯA PRODUCTION READY |

Phân biệt sửa code với cấu hình/dữ liệu/approval/nghiệm thu theo report. Tên FR17 thứ hai đã chốt SRS1.0, chỉ activation/catalog còn thiếu; không yêu cầu PO duyệt lại tên. New9 chưa có exact approval và proof143 stale156:0sign/0Apply. Historical artifact incident PARTIAL không phục dựng.

---

# Ghi chú audit SRS v1.0 — hồ sơ ngày 23/10/2026

Đã đối chiếu đủ17FR/NFR trong [bảng hồ sơ](bao-cao-tien-do/02-doi-chieu-SRS-v1.0.md):1FR hoàn thành LOCAL trong phạm vi riêng/14còn delta/2chưa làm(FR08,14). Không phải%tiến độ hay regression mới; không phủ nhận PASS LOCAL module lịch sử. Chưa UAT/Production. Dev/Test metadata counts0diff,17sourcegroups0diff/Identity.envsame;0SQLwrite. FR17 tên đã chốtSRS1 nhưng trạng thái Dev/menu vẫn chưa đồng bộ; DVCchờTrưởngphòng. Chi tiết/bằng chứng trong `.local/report-dossier-current.json`.

---

# Nghiệm thu hiện hành — UAT chuẩn bị, 23/10/2026

**PARTIAL — CHƯA PRODUCTION READY.** Run `uat-ready-20261022T170034Z-2524b742`, [báo cáo](fast-delivery-report.md). Không gộp các lượt kiểm tra sau thành full regression.

| Vùng | Trạng thái/bằng chứng cuối |
|---|---|
| Dev preservation | PASS264/275/615/528;17groups0diff cả trước/sau HTTP;53NotScanned;Identity.envsame;0 import/migration |
| UAT binding | LOCAL PASS3configuration groups; allocated DB/approval reference bắt buộc; no Dev/Test/restore/Prod; proposed name chưa cấp |
| Publish/ZIP | LOCAL PASS319files+manifest,ZIP320entry0hashdiff;Web/Worker/CLIRelease;NOTDEPLOYED |
| Operator scripts | LOCAL PASS17prep;11XML;7runtime guard/offlineCLI, các run riêng.2harness failures trước đã sửa; partial dirs retained |
| Release HTTP | PASS11/11 thực trên Dev264,source/imageSHA/PDFRange/privateACL; khôngbusinessUAT |
| Nuke cache | PASS22raw/14redirect/130negativeassert;22normalized private drafts;0CMSimport/0network;author/mapping/source acceptance pending;2missingassets |
| QLDD coverage | PARTIAL295knowncanonical=264Dev+14Test+17held;54pendingtyped;totalUNKNOWN;0new import this run |
| NewDev9 | UNSIGNED/UNAPPLIED, user says prepare only; old hashes unchanged; BLOCKED staleDefs143 vs145. Need new proof-bound packet before exact approval |
| Real UAT | BLOCKED target not allocated; installation/online SQL/IISHTTPS/Clamd/SMTP/MFAops/restart/restore/UAT16 NOTRUN |
| Further gates | Source-media target release/realrelay receipt integration/privateLookup/3Word/PO/ATTT/UAT/cutover still pending |
| SQL/artifacts | UNKNOWN/MONITORING/no newtimeout; historicalPARTIAL artifacts untouched |
| Not run | Full regression,full UI,realUAT,scan/EICAR new,realmail,newSQLrestore,DevApply/signing/service/task install |

---

# Nghiệm thu hiện hành — FAST DELIVERY, 22/10/2026

**APPROVED5 APPLIED/VERIFIED LOCAL; toàn dự án PARTIAL, CHƯA PRODUCTION READY.** [Báo cáo](fast-delivery-report.md), run `fast-delivery-20261022T091624Z-d69e1e85`. Không dùng kết quả lịch sử làm fullsuite mới.

| Vùng | Trạng thái/bằng chứng |
|---|---|
| ExactDev5 | PASS actualsignedscope5/5/13/10, đúngtarget/strict13proof/backup/freshpreflight;5committed0mismatch |
| Idempotency | PASS5AlreadyCommitted/0delta; actualreadbackDataSHAstable; no rollback needed |
| Bảo toànDev | PASS264/275/615/528,53originalNotScanned;259old/270oldrev allcolumns+17groups0diff/Identity.envsame;70capture68snapshot8exceptions2meta0file |
| Backup | PASS2SQLCOPYONLYCHECKSUMVERIFYHEADER/SHA+679files128deps/fileRestore0diff. PreApply259; thisSQLrestoreNOTRUN; old250restorePASS separate |
| QLDDTest | PASS14new14rev57media28alias;verify0/repeat14Already6Blocked0delta;old5TestDataSHAunchanged |
| Coverage | PARTIAL295canonical/264Dev/14Testawait/17held;302typed parsed;54typedpending(51noSnapshot+3parse);sourceTotalUNKNOWN, notfullsourceacceptance |
| Blocked rows | 6newtyped =2existingcanonical alias/protected +4newcanonical;13old/34Devgaps/52ledger separate, no unsafeomission |
| Native | PASSQLDD61uniqueClean/strict52subset/Nuke21Clean;actualreceipts;defs143;0source53scan/promote. Production/UATengine NOTRUN |
| NextDev9 | READY_FOR_APPROVAL `cbd41632-a662-45b1-8aea-81e447a66c90` / `2e0bf98ace1198e224648fbf8e623a3fe5577171de78fb44be16050a2132e436`;9/9/52/18,9absent0errors. UNSIGNED/UNAPPLIED;freshguard+backup requiredafterexactapproval |
| Intro5 | TestPASS sourceStatic3060–3064; DevBLOCKED category60authority/missingcatalog; no auto category/grant |
| Nuke | PASS22SQLprivateSnapshot/21assetsnative21audit/repeat0new;0CMS. Mapping6groups/author/sourceHTTP/CMSimport path stillpending;2externalassetURLsheld |
| HTTP | Dev35/35 and Test19/19 separatePASS realdata/mediaSHA/Range/privateACL/noimagehotlink; notbusinessUAT |
| UATpackage | PASSLOCAL314hashes/sameRelease; packagedexporter11/11+parsePASS,5taskXMLonly. Actualtargetinstall/crashrestart/IISSQLTLS/scannerSMTP/MFAops NOTRUN |
| UAT16 | PREPARED existing16; no businesscase autoclosed. Targetmediarelease/relayreceipt/privateLookup/3realWord/ATTT/restoreNFR/cutover remain |
| SQL/artifacts | UNKNOWN/MONITORING/no newtimeout; noSQLsettings/policybypass. HistoricalrawincidentPARTIALkept |
| NOTRUN | Fullregression,websitewideUI,Productiondeployment/realmail/realrestart/newSQLrestore. CodeunchangedCMS/video/outbox suites not rerun |

---

# Nghiệm thu hiện hành — dữ liệu và UAT, 22/10/2026

**PARTIAL — CHƯA PRODUCTION READY.** [Báo cáo](delivery-completion-report.md), run `delivery-20261022T072954Z-e0ba61bb`. Không lấy PASS của phase cũ làm bằng chứng code mới.

| Vùng | Trạng thái/bằng chứng hiện hành |
|---|---|
| Resource policy owner | CONFIRMED PDFsource48MiB/exact38URL; uploadPDFpublic32MiB giữ nguyên; policySHA bound plan+recovery |
| SQLTest nguồn | PASS+4content/+4rev/+8asset/+8alias;3075Keep,verify0;repeat5Already/13Blocked,DataSHAstable |
| Dev source/Identity/env | PASSpreserve259/270/602/518,53NotScanned,70capture68snapshot8exceptions2meta0video;17groups0diff/allcolumns0diff |
| Dev batch5 | READY_FOR_APPROVAL, unsigned/unapplied; `662f220c-82a7-4858-8786-92741003d857` / `21c9ce0500b40f798095de90d63cf0a3bd2cce21fc9d1facab2d86ef76497c84`;5/5/13/10,preflight5absent0error;revalidateTTL/defs/target+backup after exactownerapproval |
| Native scanner | PASS23uniqueClean/strict13subset/defs143;genuineEICARTestPASS. Production/UATengineACCEPTANCEBLOCKED;originalDev53unchanged |
| UAT profile/MFA/software | LOCALfocused7casesPASS;actualIdentityHTTPenroll/TOTP/replay/recovery/revoke. Not targetMFA/operationalapproval |
| Outbox | TCPUncertainPASS;F4final1/1PASS riêng,sinkonly;STARTTLSoptionsguardPASS. Realrelay/acceptedreceiptintegration NOTRUN/BLOCKED |
| Video regression | Final2/2PASS riênggenuineAV/FFmpeg afterfreshfixture;mixedrunner7PASS2FAIL fixtureReuse retained. KhôngcountTestvideosassource |
| Nuke adapter | Final2/2PASS riêng(22realraw+66negchecks+legacyfixture);22privateparsed,0SQL. HTTP38attempts inclrobots/home,14redirect;HTTPSclientcertFAIL/mapping/assets stillpending |
| Release HTTP/UI | Dev7/7,Test31/31,UI2PASS riêng;5inlineimagesdecoded;privateACL/PDFRange/hash. No businessUAT |
| Publish kit | PASSLOCAL311hashes;final launcher DataHome/Homealias parse+actualruntime badconfig deny beforeSQL PASS;exacttestedWebWorkerRelease,guardedofflineSQL/launcher/config/ops. ActualIIS/UATSQL/AV/SMTP/serviceaccounts NOTRUN |
| Backup/restore | Current2SQL+679files44deps/sourceSQLTestaddendum72files verified,fileRestorePASS;current259SQLrestoreNOTRUN,old250actualrestorePASSseparate |
| Completeness | PARTIAL277verifiedcanonical/259Dev/5ready/13held;3typedemptydetail(2canonical)needsource;sourceTotalUNKNOWN. 34existinggaps/52historicrows overlap,do notsum |
| SRS/operations | StillBLOCKED:targetsource-mediarelease,realSMTPacceptedreceipt,DirectiveDocumentsadapter/data,3realWord,UAT/restore/operationalrights/privateLookup/cutover. No later-stageautoclose |
| SQL/artifacts | UNKNOWN/MONITORING;noquerytimeoutincrease,nohistoricalartifactrecreation |
| Full regression | NOTRUN byFASTDELIVERY;allgroupcountsabove are separate runs,not a fullsuite |

---

# Nghiệm thu hiện hành — Phase3L approved Batch9, 22/10/2026

Finalparser evidence`srcset-tests-20261022T045614Z-3a188b6f`:4/4PASS,build0warning/error,0codeDrift/SQL/network qua`test-source-srcset.ps1`. Thay cho lượt4/4 trước khi nêu trạng tháicodecuối; không cộnghai lượt.

[Báo cáo](phase-3l-approved-batch9-report.md), run`phase3l-approved-20261022T043053Z-7004e491`, `.local/phase3l-approved-current.json`. **Batch9 APPLIED/VERIFIED LOCAL; toàn mốc cònPDFpolicy/gaps/Production, CHƯA PRODUCTION READY.** Nhãn chưaký/chưaApply phía dưới là lịch sử.

| Tiêu chí | Bằng chứng lượt này |
|---|---|
| Exact approval/signer/target | PASS batche02a8cb2/hash6e2374...9/9/18/18,1signedApplyValid/CurrentUser; Processpin, khôngtrust/env/grantschange |
| Strict18proof/freshpreflight | PASSengine1.1.26090.9/defs143/updated/TTL/byte/SQLreceipt,0rescan18;9absent/0error,2679ms |
| Backup | PASSnewla0494e318:2SQLchecksum/verify/header/SHA,661files166deps,0missing/hashdiff,fileRestorePASS;newSQLrestoreNOTRUN |
| DevApply/readback | PASS+9content/+9rev/+18media(8PNG10PDF)/+18alias;0mismatch. Dev259/270/602/518 |
| Idempotency | PASSsame signed safe-apply9AlreadyCommitted/0delta/DataSHAunchanged;9receipts/17groups0diff |
| OldDev | PASS250content/261revision allcolumns,17oldgroups0diff,Identity/envsame,53originalNotScanned/70capture68snapshot/8/2meta0file |
| HTTPDev |27/27PASS riêng:3articles/18fileSHA,MIME,length/category/search/301/privateACL |
| Parser3075 |4/4groupsPASS;282cache replay,only3075affected,5fullURLs/order/raw/metadata preserved;newcheckpoint/0crawl |
| 3075Test |PASSnative5Clean143+5SQLaudit;Test+1content/+1rev/+5ảnh/+2alias;verify0/repeatAlreadyCommitted;HTTP8/8riêng |
| 3075Dev |NOT APPLIED; cầnstrictpublicrecoveryproof/freshmanifest/exactownerapprovalriêng;TestproofkhôngcấpquyềnDev |
| PDF3205 |BLOCKED_POLICY_APPROVAL;HEAD39,192,764byte>32MiB,body/SHA/validate/scanNOTRUN;48MiBchưaapproved |
| SQLtimeout |0newobserved, historicalROOTCAUSEUNKNOWN/MONITORING giữ nguyên |
| Othergaps/readiness |17case resource khác/34Devgaps/52historicledger/53sourceapplicationauthority/Productiontarget,scannerSMTP,MFA,UAT,Word,lookup/NukeViet vẫnriêng; oldartifactPARTIAL |
| NOTRUN |Fullregression/websitewideUI/newEICARcontrols/newbackupSQLrestore/ProductionUAT; khôngcộngchồng27+8+4thànhfullsuite |

Test4795totalContents và527SourceRecords namespaceQLDD gồmtechnicalfixtures;khôngtínhlànội dung nguồn thật. LượtTest này chỉ1Article3075 có raw network+native5proof. Các lỗihelper chuẩn bị/build/path-prefix giữraw và tách kếtquảfinal, khôngbỏguard.

---

# Nghiệm thu hiện hành — Phase3L PARTIAL, 22/10/2026

**Sau sửa cuối:** `new9-final-ready.json` ưu tiên; cùngplan/hash không đổi. CLI f13f2485...; reuse18/0native/0newreceiptPASS, stale140reuseFAILCLOSED,2AuditSELECTthay per-proof queries (khácworkload với20oldrefresh, khôngtimingbenchmark). Testpreflight2709ms/Dev2326msPASS9absent0commit/errors;4negativeguards rerunPASS riêng;9/18/18readback+17DevpreservationPASS sau sửa. Không cộng4preview/preflight trước sửa thànhfullsuite trên code cuối. SQLrootUNKNOWN/MONITORING còn;0sign/Apply.

[Báo cáo3L](phase-3l-report.md), [runbook](phase-3l-runbook.md); run `phase3l-20261022T031058Z-5a15a959`. **CHƯA PRODUCTION READY;0sign/0DevApply.**

| Tiêu chí | Kết quả thực3L |
|---|---|
| SQL operation/diagnostics | PASS xác địnhInventoryBindingEXISTS/fingerprint/session/types/stage; reproduced35s-2/NONE. WaitsamplePAGEIOLATCH_SH/0blocker không thay fulltimeline bị thiếu. Rootcause UNKNOWN/MONITORING |
| Plan/query cost | Actualoperatorplan14ms/24.927rowsread/1.221logicalreads;khác hanging-runtimecontext. Không sửaindex/timeout/NOLOCK/SQLsettings; connectiontrial không giúp đãrevert |
| SQL-readiness/preflight9 | SQLreadinessPASS,khôngscanner/noApplypermit;4finalTest/Devpreview-preflightPASS/9absent0commit0errors. Mộtreader25,7s: không tuyên bốSLAstable |
| Defender18 | PASS18nativeClean143/bytehash/quarantine/SQLreceipts/currentfinalidentity/TTL;job36receipts2/resource,limits3/180giữ.60legacyvẫnstale140;0Dev53scan-promote |
| Packet9 | READY_FOR_APPROVAL tại finalhealth: e02a8cb2-c7a4-4e9e-8884-737ee3b3cd01 /6e2374fd20fd20d74b48f1fd5c9d9c3d7437a119723f7a37b54514956d710eed /9-9-18-18;unsigned,0Apply;exactownerconfirmationcầnmới |
| Source/media/alias readback | PASS9identity/metadata/body/date,18realmediaSHA/scanstates,18alias;0mismatch. Không import Test mới hoặc sửa publicrevision |
| Backup | PASSll0f39ab66:2SQL/661Devfiles155deps;after-proof9 SQLTest+74deps/hash/readback. Không overwrite/ACLgrant;futureApplycần actualsignedapprovalbackup |
| Actualrestore newest | PASSLOCAL LandPortal_Restore3J_d9d16f42 READ_ONLY:250/261/584/500/68snapshot,DBCC0,allcolumns10tables/schema/byte/relations/3PDF/Identitypermissions0mismatch. Countsderive;targetRPO/RTO NOTRUN |
| Dev preservation | PASS250real source/261rev584media500alias/53NotScanned/70capture68snapshot/8/2meta0file;17groups0diff/Identityenv unchanged |
|18canonical/PDF/parser | PARTIAL0/18imported;1confirmed3075srcsetfindingchưafix. PDF3205cap32MiB ởcrawler vàimport/scanner;48MiBsource-onlyproposal NEEDS_APPROVAL.52/34/3parser chưađóng |
| Focusedchecks | BuildCLI/Infrastructure0warning/error;4negativeguardsPASS riêng;9/18/18readback vàrestoreresultsriêng. EarlierSQLfailures/cancelledobserver giữraw;khôngcombinedfullsuite |
| NOTRUN | Fullregression/websitewideUI/HTTP/newimport-idempotency/EICARnegativecontrols/businessUAT/Productiontarget |
| NukeViet/UAT/Production | Nuke giữBLOCKED_BY_NETWORK/noexport0real,0newrequest3L.16UATcasesPREPARED;targetSQL/IIS/ACL/HTTPS301/scannerSMTP/MFAflow/3Word/privateLookupauthoritycònthiếu. HistoricalartifactincidentsPARTIAL |

---

# Lịch sử nghiệm thu — Phase3K PARTIAL, 22/10/2026

**Final freshness FAIL/BLOCKED:** Defender definitions143khác78proof140;proofgiờSTALEtrướcTTL. Packet9 **BLOCKED_SQL_PREFLIGHT_AND_STALE_PROOF**. PASSnativebytes/receiptslàbằngchứnglịchsửtại140,khôngphảichophépApplyhiệnhành;`final-safety-gates.json`ưu tiênhơnTestpreflighttrướcđó.

[Báo cáo3K](phase-3k-report.md), [runbook](phase-3k-runbook.md); run `phase3k-20261022T020231Z-74a11e6d`. **CHƯA PRODUCTION READY**. Các kết quả3J/3I phía dưới là lịch sử, không tự thành kết quả3K.

| Tiêu chí | Trạng thái và bằng chứng3K |
|---|---|
| Devbaseline/bảo toàn | PASS:250nguồn/261revision/584media/500alias;53originalNotScanned/70capture68snapshot/8/2meta0file;17groups0diff/Identityenvsame |
| Phân loại34gap | PASS phân loại từngidentity,0Devrepair;0metadatafielddiff/6category59-60/29scan-related/2video/1badPDF giao nhau |
| Catalog/phục hồiTest | PARTIAL:2catalogrows59/60stagedsourceevidence;0comparisonrevisions doSQLtimeout. Staticreadback23match+1warning/10gaps **không** làworkflowPASS |
| 27canonical chưaDev | PARTIAL:9Testvaliddata/18mediaformatblocked;0newcontent3K;source-totalUNKNOWN |
| DefenderTest | PASS78nativeClean140/exactbytes/SQLreceipts tại thờiđiểmquét;**BLOCKEDfreshness**final143.0Dev53scan-promote;EICAR/failurecontrolsNOTRUN3K |
| Dev9packet | BLOCKED_SQL_PREFLIGHT;batch22cbffd4-db08-4401-8922-0c5458f082db/SHAb538321e547a406bd47b5c5dc7a6bd7437d80649c9d7c85034bc96e9b9704216/9-9-18-18;unsigned0Apply;TestpreflightPASSkhôngthayDevpreflight |
| SQLruntime | FAIL1Devpreflight+2Teststage,SQL-2/transactionNONEtrongTesttrace;reconciled1boundedretrythenSTOP.ROOTCAUSEUNKNOWN/MONITORING |
| 52exceptions/parser/PDF | CLASSIFIED,0resolvedDev;52typed50canonical/5aliasreview/2video;3parserheld,PDF3205HEAD39.192.764byte>32MiB0download |
| Backup | PASSnewDev250/Test4794COPYONLYCHECKSUMVERIFYONLY/661Devfiles156deps;before-stageTestSQL+200deps/hashPASS. Không phảiSQLrestore |
| Actualrestore | GiữPASSLOCAL235READ_ONLY3J;SQLrestorebackup250mớivàhạtầngđíchNOTRUN |
| Finalchecks | 9focusedreadback/integrityassertionsPASS;10PSparsePASSriêng;17DevpreservationPASSriêng. Fullregression/build/HTTP/UI/importrepeatNOTRUN3K;3SQLopsFAIL/remainingworkflowBLOCKED |
| NukeViet | BLOCKED_BY_NETWORK:DNSPASS45.120.229.41/TCP80-443refused/TLSNOTRUN;0realinventory/exportNOTPROVIDED;mappingdraftchưaduyệt |
| UAT/Production | PREPARED16cases,UATNOTRUN;targethosting/SQL/ACL/scannerSMTP/TLS301missing;localIISabsent,MFAflowmissing,Development/Testsoftwarefencescòn. AC16.1/privateLookup/Devpermissions/artifactincidentsgiữnguyên |

---

# Lịch sử nghiệm thu — Phase3J PARTIAL,21/10/2026

[Báo cáo](phase-3j-report.md),run`phase3j-20261021T092822Z-35b2612b`.CHƯAPRODUCTIONREADY;khôngsumtestthànhfullsuite.

|Tiêu chí|Trạng thái/bằng chứng thực|
|---|---|
|3Ibaseline/preservation|PASSDev250/261rev/584media/500alias;17groups0diff/Identityenvsame;53NotScanned/70capture68snapshot/8/2meta0file|
|QLDDđộđầyđủ|PARTIAL216đủtheocache/byte,34gap;1396URL/285candidate/282typed277canonical;source-totalUNKNOWN;18categorychưanghiệmthu100%|
|52blocked|CLASSIFIED,UNRESOLVED:52typed50canonical/33cóDev17chưa;30existing22staging5alias;18file31proof5alias2videogiao nhau,0autoclose|
|Boundedcollection|PASS39GETsamecheckpoint/1connection>=2s/+10parsed;0missingdetailtrongfrontier,3parsercòntreo;khôngfullcrawl|
|NativeTestscan|PASS18SHA/27URLCleanDefender1.1.26090.9/defs118;0Infected/ScanFailed;ProductionNOTRUN|
|Testsourcebatch|APPLIED/VERIFIEDLOCAL9content9rev18asset18alias;1newPDFblocked;oldrowsallcolumnunchanged/repeat0delta/0mismatch|
|Devbatchmới|NOTREADYFORAPPROVAL,cầnfreshstrictproof/Devpreview/exactbatchSHA;0DevApply|
|ActualSQLrestore|PASSLOCAL235/246rev/569media/470alias,DBriêngREAD_ONLY/DBCC0error/646files/byte/schema/Identity/permissions/PDFread;khôngđèDev/Test|
|Currentbackup250|PASS661Devfiles+2SQL+36deps;restoreSQLbộmới/targetRPORTONOTRUN;khôngđồngnhấtvớidrill235|
|Finalchecks|8focusedassertionsPASS;5negativeguards/parsePASSriêng;11HTTPPASSriêng/Test5395stopped;fullsuite/buildUI NOTRUN|
|ScannerProduction|BLOCKEDengine/policy/serviceprofile/softwarefences;53NotScannedvà531CleanbatchDevelopment-onlykhôngtựpromote|
|NukeViet|BLOCKEDTCP443/80refused,DNSPASS;0realinventory/noofficialexport/mappingSQLadapterNOTACCEPTED|
|Release/SRSUAT|BLOCKED/NEEDSAPPROVALtarget/IIS/TLS/SMTP/MFA/UAT/fullcoverage/cutover;lookupWord/quyềnDevgiữnguyên|
|SQL/artifact|ROOTCAUSEUNKNOWN/MONITORING;oldV04/2DrawPARTIAL,khôngtáitạo|

<!-- PHASE3J-ACCEPTANCE-END -->

# Nghiệm thu hiện hành — Phase3I bulk APPLIED, 21/10/2026

**PHASE3I:APPLIED/VERIFIEDLOCAL12/12;CHƯAPRODUCTIONREADY.** [Báo cáo](phase-3i-bulk-apply-report.md),run`phase3i-bulk-20261021T084908Z-f93fdf57`,indexSHA`19751c31c9911b7e92977c7c6d039a0bd1919211d2349a9910e7649dcbcc3b13`.

|Tiêu chí|Bằng chứng thực|
|---|---|
|Ownerauthority/exactindex+12plans|PASSactualattachedapproval,computedSHA/12scope/delta/target;noID/manifestchange|
|Signing/trust|12actualAuthentiCodeValid approvals,publicchain/pin/EKU/date/SID/batch/hash/actionsverified;0newcert/trust/policygrant|
|Backup/recoverypoints|12PASSfullcurrentDev+deps/24SQLbaksCHECKSUMVERIFYONLY/header+filehash/restorecheck;SQLrestoreNOTRUN|
|Proof|464exactnativeClean/current118/engine/TTL/SQLreceipt validated,0refresh/Dev53promotion|
|SequentialApply/reconcile|12/12actualcommittedreadback/0missing/errors;noout-of-order or blindretry|
|Actualdelta|190sourcecontents/190revisions/464asset/380alias,0fixture/blockedrows|
|Verify/uniqueness|0mismatch;190exactreceipts/identities464SHA380aliasunique;committedbatchesnotre-applied|
|Olddata|60Contents/71Revisions/60SourceRecordseverycolumnpreserved;17groups0unexpected;Identityenv/53NotScanned/52ledgerunchanged|
|Devfinal|250contents/261revisions/584assets357image227PDF/500alias;53NotScanned531Clean;70capture68snapshot/8/2meta0file|
|HTTPcombined|13/13actualrequestsPASS,localWeb5393stopped;notUI/fullsuite|
|Failures/rollback/SQL|0packetFAILED/BLOCKED,newSQLtimeouts0,rollbackNOTNEEDED;rootUNKNOWN/MONITORING|
|Remainingoverall|52blocked/NukeVietcoverage/MFA/ProductionScanner/SQLrestore/UAT/cutover and historicalartifactPARTIAL separate;notproductionready|

<!-- PHASE3I-BULK-APPLIED-ACCEPTANCE-END -->

# Nghiệm thu lịch sử — Phase3I preparation, 21/10/2026

**12READY_FOR_APPROVAL,NOTSIGNED/NOTAPPLIED.** [Báo cáo](phase-3i-report.md)/[bảnghash](phase-3i-packets.md),run`phase3i-20261021T080800Z-c4f2ee3b`. Đây lànghiệmthuchuẩnbị,khôngnghiệmthu190imports.

|Tiêu chí|Kết quả thực|
|---|---|
|Baseline/target/tool|PASSDev60/71rev/120media120alias;MinhAnh/LandPortal_Dev/GUID;CLI35624589...same;schema unchanged|
|12plans/reviews|PASSexistingSHA,b02-b13Pending/null/unsigned;0newmanifest/BatchId|
|FreshpreflightDev60|12/12PASS,190ABSENT/0commit/errors;noApply|
|Nativeproofstrict|464boundClean/currentdefs118engine/TTL/SQLreceipts/exactbytePASS;0rescan/refreshrequirednow|
|Refreshworklist|PREPARED464resources/correctjob/expiry;earliest22Oct11:06:37VN/defsdriftinvalidatesimmediately|
|CurrentTestsource/readback|190source/344auditrows(190Create+154Keep)/464assets380aliases/465relationsPASS0mismatch;sealedNetworkcohort,notTesttotal/fixture|
|Pairwise/Devconflicts|0source/alias/assetSHAoverlaps;sharedprerequisitesgated|
|Authority/signing|PENDINGOWNERCONFIRMATION;0signatures;existingpublicsignerValidreuse,notestsrerun|
|Backup|2107fileallowlistPREPARED_NOT_BACKED_UP;currentDev60backupmissingforfutureApply;oldDev40backupnotrelabeled|
|Preservation|17groups0diff;Identity/envsame;53originalNotScanned/52blockedunchanged;actualimport0|
|Build/fullsuite/HTTP/UI/scans|NOTRUN:noappcode/Applychange;12preflightsnotfullsuite|
|SQL/overall|UNKNOWN/MONITORING,nonewtimeout;oldartifactPARTIAL/Production/MFA/restore/UAT/cutoverremain|

<!-- PHASE3I-ACCEPTANCE-END -->

# Nghiệm thu lịch sử — Phase3H CNG / exact batch APPLIED, 21/10/2026

**APPLIED / VERIFIED LOCAL đúng20/20/60/40; CHƯA PRODUCTION READY.** [Báo cáo](phase-3h-cng-apply-report.md), run`phase3h-cng-20261021T073758Z-a48acba7`;batch`aa52120d-a6b9-41b9-b4b5-22ced4330113`/SHA`41c7bf061b472ec0d8aa5129eaa9384bb27e1966baa977fc74c82e78f63d6dfa`.

|Tiêu chí|Kết quả thực|
|---|---|
|CNG verification fix|PASS public-onlycertificate/GetRSAPublicKey;signing/provisioningprivatechecksriêng,khôngkeyACL/provider/trustchange|
|Focused CNG security|15/15PASS;actualapproval/nativeTestfixture/changedsignature/hash/batch/DB/pin/scope/signingwithoutprivatekey|
|Bridge regression riêng|8/8PASS existingrunner;nativeerrors+contracttrust/expiry/nonce/outputnegatives,khôngcertuntrustedmớigiả|
|Parse/build|3changedPSparsePASS;diagnosticprobe0error3warning;Web/CLIbuildNOTRUN/PS-onlychange|
|Backup/approval/proof|REVALIDATEDPASS/reused;137files215depsSQLDev/Testhash/header,currentDefender118/engine/TTL/SQLreceipts;0newbackup/resign/rescan|
|Fresh preflight/Apply|PASS ABSENT20/0errors→20CreateNeedsApproval;actualcommit/readback|
|Reconcile/verify|COMMITTED_VERIFIED20/20/0missing/errors;20source20rev60media40alias;0mismatch|
|Idempotency|20AlreadyCommitted/0dataDelta/20receipts;DataSHAfb97b03e...same;postrepeat17groups0diff|
|Olddata/private/media|40Contents/51Revisions/40SourceRecordsallcolumnsunchanged;17groups0unexpected;53originalNotScanned/52blocked/Identity/envunchanged|
|Actual HTTP smoke riêng|11/11PASS source/category/search/alias,imagePDFSHA/privatepreview/storageACL;temporaryWebstopped|
|Dev final|60realcontent/71revision/120alias/120media67image53PDF;53NotScanned+67Clean;70capture68snapshot/8/2meta0file|
|Remaining/rollback|190content12packetsnotauthorized;52blockedunchanged;rollbackNOTNEEDED|
|SQL root/overall|UNKNOWN/MONITORING;fullsuite/UI/SQLrestoreNOTRUN;oldartifactPARTIAL/Production/MFA/UAT/cutoverremain|

Khôngcộng15+8+HTTP11thànhfullsuite. ExistingmanagedprivatekeyAPIvẫncóCNGfailure;fixpublicverifierđúngmụcđích,nativesigningprovenkhôngcầnđổiWindowssecurity.

<!-- PHASE3H-CNG-APPLIED-ACCEPTANCE-END -->

# Nghiệm thu lịch sử — Phase3H exact approved Apply, 21/10/2026

**BLOCKED BEFORE APPLY; CHƯA PRODUCTION READY.** [Báo cáo](phase-3h-approved-apply-report.md), run`phase3h-apply-20261021T071528Z-4099b799`; batch`aa52120d-a6b9-41b9-b4b5-22ced4330113`/SHA`41c7bf061b472ec0d8aa5129eaa9384bb27e1966baa977fc74c82e78f63d6dfa`. Chỉ batch20/20/60/40 được duyệt.

|Tiêu chí|Kết quả thực|
|---|---|
|Exact packet/target/schema|PASS initial readonly; plan/dependencies SHA không đổi,SQLMinhAnh/LandPortal_Dev|
|Initial fresh strictproof preflight|PASS ABSENT20/0commit/0errors;currentengine/defs118/TTL;0rescan|
|Owner approval/signing|CONFIRMED + SIGNED actionApply đúng batch;initial Authenticode/certificate/exactbindingPASS|
|Full backup|PASS137Devfile+2SQL+215deps/0missing/hashdiff;VERIFYONLY+file restore-check;SQLrestoreNOTRUN;firstbackupINCOMPLETE retained|
|Final signature guard|BLOCKED GetRSAPrivateKey/CNG ephemeral key handle;publicsignatureValid không thayguard;0bypass/retryApply|
|Final preflight/Apply|NOTRUN do guard trước đó;actualdelta0content/0revision/0asset/0alias|
|Stopped reconciliation|PASS ABSENT20/0commit/20missing/0errors,không phải postApplyverify|
|Old Dev/Identity/env/52blocked|PASS17groups0diff,40/60,53NotScanned;Identity/envsame,ledgerhashunchanged|
|PostApplyverify/idempotency/HTTP|NOTRUN vì khôngApply;rollbackNOTNEEDED|
|Fullsuite/build/UI|NOTRUN;sourcekhôngđổi,khôngngoàiphạmvi|
|SQL root/Production|UNKNOWN/MONITORING;CHƯAPRODUCTIONREADY|

Các kết quả unsigned/READY_FOR_APPROVAL ở dưới là lịch sử. Approval mới không cấp scope cho packet khác. Resume cần xử lýCNG cófixture+failclosed, rồi revalidateexpiry/hash/proof/backup/target/preflight.

<!-- PHASE3H-APPROVED-APPLY-ACCEPTANCE-END -->

# Nghiệm thu lịch sử — Phase3H-Fix, 21/10/2026

**Workflow hiện tại PASS LOCAL; root-cause điều tra PARTIAL/UNKNOWN/MONITORING.13packet210READY_FOR_APPROVAL;0sign/ApplyDev;EXECUTION_GATED approval/backup/freshguards.** [Báo cáo](phase-3h-fix-report.md)/[packet](phase-3h-batches.md)/[runbook](phase-3h-fix-runbook.md), run`phase3h-fix-20261021T050440Z-e5776be2`.

|Tiêu chí|Kết quả thực|Bằng chứng trong run|
|---|---|---|
|Baseline live và sealed tool|PASS|CLI35624589.../selectedcode/envkhôngđổi;Dev40/60/17groups0difftrước|
|Controlled timeout reproduction|NOTREPRODUCED,preflightPASS|1lần/3.089ms,observer6sample;operationId/batchId/fingerprint/connection/session/stages thậtraw|
|SQL timings và row reads|MEASURED|84readers/86roleactivations,max25,7ms;2actualequivalentauditplans1/60returned24988/25564rowsread;khôngsummedparentoperators|
|Resource/blocking/coldpath root|UNKNOWN/MONITORING|AUTO_CLOSEON/SQLprocessmemorylowflagtrue;OSlowfalse;khôngblockingtrongsample;observerwarmbiasđượcnêurõ;khôngconfigchange|
|Query/index/transaction fix|NOTCHANGED|Chưađủbằngchứngcausal/slowness;poolfalse/role/timeout/securitygiữnguyên;khôngmigration|
|B01preflight khôngobserver|PASS|ABSENT_CONFIRMED20/0errors;2.846ms|
|Replay Test actualsafeApply|PASS20Keep|ExistingrealTestrecords,0sourcecontent/revision/assetmới;khônggọiKeepthànhCreate20|
|Testreconcile/verify/readback|PASS20/20/0missing/errors|20source20revision60media40alias;verify0mismatch|
|Testrepeat/idempotency|PASS20AlreadyCommitted|DataSHAtrước/sau691f5ce8...giống;0duplicateimportreceipt|
|15pending Test/Dev|PASS15/15|b13freshTestpreflight/Devpreview/preflight;0BLOCKING/NEEDS_REVIEW|
|13packet/52historical|READY_FOR_APPROVAL210;52remaining|15b13khônggiao52;210revision524asset420alias;newb01+b13,old11packetsgiữ/hashverify,khôngrerun11SQLpreview|
|NewB01 scope/authority|UNSIGNED/PENDINGUSER|aa52120d-a6b9-41b9-b4b5-22ced4330113/SHA41c7bf061b472ec0d8aa5129eaa9384bb27e1966baa977fc74c82e78f63d6dfa/20-20-60-40;0approval/DevApply|
|Scanner/signer liên quan|VALID,0newscan/sign/trust|b0160+b1315SQLstrictvalidate;524boundfileSHA/engine/TTLcurrent offline+3Hreceiptvalidationreuse;CurrentUser/nativefixtureValid|
|Testbackupreuse|PASS scope|Oldh3e716c4SQLSHA/headerfresh+206fileverify0diff;CHECKSUMVERIFYONLY3Hreused,khôngSQLrestoreclaim|
|Devdata/Identity/env sau|PASS17groups0diff|40/60/53originalNotScanned/70capture68SQLsnapshot/8/2meta0file;digestenvsame|
|Diagnostic scripts cuối|PASS2parse+4negativeguard,0SQL|script-checks;actualXMLcapturepositive2plansaftertypefix;khôngfullsuite|
|Build/fullregression/UI/DevApply/SQLrestore|NOTRUN|Không.NETsourcechange,norebuild/fullsuite/UI;Devexecutionchưađượcphêduyệt;restore/MFA/ProductionUAT/cutovervẫnriêng|

Không gọi các test3H6/6,4/4 hoặc signature8/8 cũ là kết quả code mới. Historical raw incidents PARTIAL. **CHƯA PRODUCTION READY.**

<!-- PHASE3H-FIX-ACCEPTANCE-END -->

# Nghiệm thu lịch sử — Phase3H, 21/10/2026

**PARTIAL;12 unsigned packet READY_FOR_APPROVAL để xem xét, EXECUTION ON HOLD SQL. Không ký/Apply mở rộng.** [Báo cáo](phase-3h-report.md), [batch/delta/SHA](phase-3h-batches.md), [runbook](phase-3h-runbook.md), run`phase3h-20261021T035215Z-47db620c`.

|Tiêu chí|Kết quả thực|Bằng chứng trong run|
|---|---|---|
|Delta nguồn thật Test→Dev|PASS210NEW/0giao3G/0fixture|verified-test-cohort,delta-audit;169+41 Create córeceipt của2manifest|
|Native resource proof|PASS524Clean/0Infected/0ScanFailed|13jobscopebounded,defs118/currentengine;309ảnh215PDF;0Dev53scan/promote|
|Replay worker scan|PASS|b01stop2/resume58/reuse60/0newscan;durableSQLreceipts/proofexactSHA|
|Final build/focused|PASS0warning/error;6/6C#|final-build.log/focused-final-tests;khôngcộnglần5/5cũ|
|Final SQL proof negatives|PASS4/4 riêng|sql-proof-focused;realnative60/currentreceipts;incomplete/duplicate/unregistered/wrongscopeFAILCLOSED|
|Wrapper cuối|ParserPASS/3negativeguardsPASS|wrapper-checks;0SQL;positivewrapperSQLNOTRUNdoSQLtimeouts|
|Public fresh nonClean policy|PASS structuralguard|KhôngfallbackoldClean;complete nativeCleansetmớibắtbuộc;khôngmockClean|
|Test fresh preview/preflight|PASS13Keepplan/preflight|13foldertest;khôngnhậplạirealcontent|
|Dev packet/preflight|PASS12/12 packet;195new/195rev/509asset/390alias dựkiến|batches.json/review-binding;0signature/executableapproval|
|B13Devpreview|FAIL/BLOCKED_SQL_CONNECTION|postlogin14011ms;15contentcóproof,0Devplan/BatchId,preflightNOTRUN;dừngkhôngretry|
|TestKeep replay|BLOCKED|Freshrecovery-proof preflightExecutionTimeout-2/35.025ms/SPID68/transactionNONE;ApplyverifyrepeatNOTRUN|
|Controlled metadata reconciliation cuối|BLOCKED|ExecutionTimeout;khôngreconcilePASS/fakeraw;summarytoolobservation|
|Test backup trước plannedKeep|PASS scoped|FreshSQLTest197880320byte/CHECKSUMVERIFYONLY+206filehashsnapshot;SQLrestoreNOTRUN;khôngbackupDevexpanded|
|Media Test đọc thật|PASS582file0hashdiff|test-materialized-media-readback;bao gồmnguồncũ,khôngđếmthànhscan524mới|
|55blocker lịch sử|PARTIAL3resolved/52remainingtypedrows|3receiptArticleisolation;22staging+30Devprotected;18file31proof8alias2video giao nhau|
|Bảo toàn Dev/Identity/env|PASS17groups0diff|preservation-final/dev-after;40/60/53originalNotScanned;70capture68SQLsnapshot/8/2meta0file;Identity/envsame|
|Approval/ApplyDev|NOTRUN đúngscope|12reviewPendingUserApproval,approvedBynull;old3Gapprovalkhôngcoverexpanded|
|Expanded backup dependencies|PREPARED_NOT_BACKED_UP|Dùngcomplete-v2.json1850file/210raw,khôngv1enumhelperthiếunonArticle;freshDev/Testbackupcầnsauapproval|
|SQLtimeout|UNKNOWN/MONITORING, tái diễn|3readonlyoperations;traceSQL_FAIL+outerFAILcùngcommandkhôngđếmdouble;khôngtăngtimeout/config|
|HTTP/UI/fullregression/rollback/newcrawl|NOTRUN|Khôngweb/ACLchanges/newDevpublication;khôngfullsuite;0Applykhôngcầnrollback|
|NukeViet nguồn thứhai|PARTIAL adapter/mapping preparation|phase-3h-nukeviet-mapping;quyềnpublicnguồnđãcó,khôngreal-sourceacceptance/SQLwriteadaptergiả|

Historical artifact incidents PARTIAL;Production scanner/MFA/SQLrestore/UAT/cutoverchưanghiệmthu. **CHƯA PRODUCTION READY.**

<!-- PHASE3H-ACCEPTANCE-END -->

# Nghiệm thu lịch sử — Phase3G signature bridge / APPLIED, 21/10/2026

**APPLIED / VERIFIED LOCAL cho exact batch3bài; không phải toàn bộPhase3.** [Báo cáo](phase-3g-signature-bridge-report.md), run `phase3g-bridge-20261021T032845Z-fb25abad`. Batch26af.../planSHAa6b.../signedapproval80ed... nguyên, khôngresign. CHƯA PRODUCTION READY.

|Tiêu chí|Kết quả thực|Bằng chứng mới trong run|
|---|---|---|
|Legacybridge rawfixture/nativecertificate|REPRODUCED|76bytestdout/852stderr,XMLprogress/noError,nativeValid/exit0;khôngrawrealapprovalsignature|
|Bridgefix vàbuild|PASS|ApprovedBatchExecution.CreateAsync→PowerShellSignatureBridge;strictstructurednonce/SHA/status/trust/EKU/date/pin;nostderrfilter;0warning/error|
|Signaturefocused cuối|PASS8/8 riêng|signature-tests/signature-tests.json;nativevalid/tamper/unsigned/unpinned/wrongSHA/realPSerror;structuredexpiry/trust/forgery/malformed/error/nonzeronegatives;noSQL/fullsuite|
|Backup vàapprovalreuse|PASS|existingcompletebackupfile/deps130/132revalidated;SQL37/47same;approval-current.json/exactplanSHA/scope/expiry;noresign/rebackup|
|Target/scanner/preflight|PASS|sql-identity/currentDefender118/engine1.1.26090.9/strictproof;before-applyABSENT10/0errors;53originalunchanged|
|ActualsafeApply|APPLIED|applyexit0:3new/1comparison/6Keep/30Blocked;10eligiblecommitted;delta3content4rev7JPEG6alias|
|Verify/readback/sourceintegrity|PASS0mismatch|verify,after-apply-reconcileCOMMITTED_VERIFIED10/10;preservation-readbackdate/author/category/SHA/URL/publication|
|Olddata/privatepublicboundary|PASS|37oldContents/47oldrevsunchanged;17groups0unexpectedexcludingapproveddelta;53NotScanned/Identity.envsame;Doc5trackinghash/LastSeenexpectedonly|
|Idempotency|PASS|repeat10AlreadyCommitted30Blocked0new;verify-after-repeat0errors;postrepeat17groups0diff|
|HTTPsmoke|PASS16requests riêng|3articles7imagesexactSHA/category/search/alias/privatepreview302/storage404;webtemporary5387stopped|
|Devtotal|40realcontents/60media|40source/51rev/80alias/48images12PDF;53originalNotScanned+7newClean;70capture68SQLsnapshot/8/2metadata0file|
|Rollback/fullregression/UI/SQLrestore/newcrawl/expanded210|NOTRUN|Khôngcầnrollback;fullsuitekhôngchạy;SQLrootUNKNOWN/historicalartifactPARTIAL/CHƯAPRODUCTIONREADY|

Eligible10đãcommitđủtheoreceipts/reconcile;SQLlegacyImportRuns.StatusPhase3BPartialphảnánhplan30Blockedđượcgiữnguyên,khôngđổinhãnSQL. Batch210cầnfreshpreviewDev40/proof/aliasdedup/manifest/approvalscope riêng,khôngcopyTestDB.

<!-- PHASE3G-SIGNATURE-BRIDGE-CURRENT-END -->

# Nghiệm thu lịch sử — Phase3G backup fix / resume, 21/10/2026

**PARTIAL — BACKUP PASS, APPLY BLOCKED.** [Báo cáo](phase-3g-backup-resume-report.md), run `phase3g-resume-20261021T030612Z-9f553132`; packet26af.../a6b... nguyên.

|Tiêu chí|Trạng thái|Bằng chứng mới trong run|
|---|---|---|
|Shortconfigurablebackup/extendedpath/rootACL/disk|PASS|backup-location-audit.json;gaae71ea6 childprivate root;Devmaxdest226chars;noACLchanges|
|SQLDev/TestCOPYONLY/CHECKSUM/VERIFYONLY/HEADER/hash|PASS|sql-backups-verified.json,backup-complete.json;11.559.424/197.224.960byte|
|Fullfiles/dependencies/sourceinventory/hash|PASS|130Dev+132dependencies;0missing0hashdiff;finalstrictreadbacksource/targetJSON;backup-complete.json|
|Isolatedfile restore /SQLrestore|PASS130files /NOTRUNSQL|file-restore-check.json;khôngrestoreDev/Testhiệncó|
|Finalbackupfocusedchecks|PASS4/4 riêng +PSparse|backup-tests-final.json;longpath/emptydir/nooverwrite/tamper/scope;khôngfullsuite|
|Existingapproval/manifest/scope/standaloneAuthenticode|PASS|signature-current/signature-before-apply.json;unchangedPlanSHA/signerdates/expiry;backupPathnotSignedaudit;noresign|
|StrictDefender/target/schema/freshpreflight|PASS|defs118/engine1.1.26090.9;before-apply ABSENT10/0errors;53originalunscanneduntouched|
|Actualsafe-apply|FAILguard /BLOCKED|apply-exit1/apply.log;compositeSignatureguardreject616byteCLIXMLprogress despiteValid/nativethumbmatch;stopno retry|
|Readonlyguarddiagnosis|REPRODUCED|signature-native-locked/unlocked.json;progress-only-readonly-probeValid/stderr0;appunchanged|
|Reconcile/Devpreservation|PASS|after-denial-reconcile0commit0errors;0ImportRun/receipt/DevAuthorization;0diff17groups/Identity.envsame|
|PostApplyverify/idempotency/newpages/mediaACLsmoke|NOTRUN|0actualcontent/revision/asset/aliasimport;khôngcódata mới;khôngfakePASS|
|BuildC#/fullregression/UI/crawl/Production|NOTRUN /NOTREADY|Onlybackupscriptschanged;SQLrootUNKNOWN/historicalartifactPARTIAL|

Điểm chặn hiện tại là native signature bridge, không còn backup thiếu. Giữ securityguard; không autoresign hoặc cho616bytestderr đi qua. Cần sửa hẹp presentationprogress và test trực tiếp trước lầnApply mới; approval còn hiệu lực chỉ đúng3bài. CHƯA PRODUCTION READY.

<!-- PHASE3G-BACKUP-RESUME-CURRENT-END -->

# Nghiệm thu lịch sử — Phase3G exact approved execution, 21/10/2026

**BLOCKED_BACKUP, SIGNED / NOT APPLIED.** [Báo cáo](phase-3g-approved-apply-report.md), run `phase3g-apply-20261021T025312Z-e91d1bf1`; packet26af.../a6b... không thay đổi. Người dùng đã duyệt đúng phạm vi; các nhãn PENDING USER APPROVAL bên dưới là lịch sử.

|Tiêu chí|Trạng thái thực|Bằng chứng riêng trong run|
|---|---|---|
|Exact owner approval / Authenticode / cert / target binding|PASS|user-confirmation.json, signature.json; actualapproval80ed3e4c... actionApply; expires21/10 21:54:56VN|
|Current SQL identity/schema/service/config và127dependencySHA|PASS|initial-guards.json, sql-identity/schema/sql-service.json; không thay.env/quyền|
|Current Defender + exact packet preflight|PASS readonly|engine1.1.26090.9/defs1.461.118.0; initial-preflight-native/reconcile.json ABSENT_CONFIRMED10expected0committed0errors; không đổi53NotScanned|
|SQLDev backup COPY_ONLY/CHECKSUM/VERIFYONLY/HEADER/hash|PASS chỉ phầnSQL|partial-sql-backup-readback.json,11559424byte; khôngrestore drill|
|Full backup media/snapshots/keys|FAIL / INCOMPLETE|backup-error.json; snapshot path269ký tự, Copy-ItemDirectoryNotFoundException; nguồn còn nguyên; không cấp quyền hoặc tạo fullreceipt|
|SQLTest proof receipt/dependencybackup vàpre-Applyfinalpreflight|NOT RUN|Dừng khi backupguardkhôngđạt, theo chỉ thị người dùng|
|Actual Apply/verify/readback/idempotency/newpages smoke|NOT RUN / BLOCKED|0content0revision0asset0aliasimport; không tuyên bốApplied hoặc cầnrollback|
|OldDev17groups/Identity/env|PASS|dev-after-stopped.json0diff17groups;37/53NotScanned/70capture68snapshot/8/2metadata0file;identityPASS|
|Fullsuite/build/UItoànweb|NOT RUN|FAST DELIVERY, không thay source/migration; khôngsumtestcũ|

Tiếp tục cần backup mới khắc phục đường dẫn dài và verify đủ, kiểm tra lại approval/proof/target/config trướcApply; không bỏguard. SQLtimeoutROOTCAUSEUNKNOWN/MONITORING, historicalartifactPARTIAL, CHƯA PRODUCTION READY.

<!-- PHASE3G-APPROVED-APPLY-CURRENT-END -->

# Nghiệm thu lịch sử — Phase3G Stabilization, 21/10/2026

**READY_FOR_APPROVAL cho packet mới; chưa ký/Apply. SQL rootUNKNOWN/MONITORING; CHƯA PRODUCTION READY.** [Báo cáo](phase-3g-stabilization-report.md), [runbook](phase-3g-stabilization-runbook.md), run`phase3g-stabilize-20261021T021233Z-20e31fbb`.

|Tiêu chí mới|Trạng thái|Bằng chứng|
|---|---|---|
|SQL tracking/provenance/rollback/idempotency/negativeguard cuối|PASS scoped4/4|tracking-provenance-final/sql-tests.json;0FAIL0NOTRUN/codeDrift0; appbuild0warning/error. Khôngsumcáclượt|
|Timeout rootcause|UNKNOWN / MONITORING|KhôngtáihiệntrongcurrentTests/preview/preflight/receipt/readback;ConnectionID/SPID/fingerprint/transaction/timingtrace;khôngSQL/index/timeout/configchanges|
|Actualreceiptplan/readback|PASS bounded probe|24987ActualRowsRead/1228LogicalReads trả60,query32.61ms;3realTestSourceRecords đúng;khôngkhẳngđịnhrootcause|
|FreshTest/Devpreview+preflight|PASS readonly|Test37eligible3Blocked;Dev10eligible0receipt0errors/3new4rev7asset6alias;0commit|
|StrictB nativeproof|PASS Test scope|Reviewedregisteredjob2fb...60Clean/defs118;validate60/resume0newreceipt;oldjob180/new60;noStageDev53/policyA/versiontolerance|
|NativeClean/Infected/ScanFailed controls|PASS 3outcomes riêng|controls-compressed.json;EICARZIP detection/cancelhealthfailure thật,khôngmock/khôngoutagenaturalclaim|
|Cert/policy/manifestguards|PASS pre-sign scope|CurrentUsercert/nativeValid,actualprocessconfigpin;3badPlanSHA/ManifestSHA/Testtargetreject;realapprovalsignatureNOTRUN|
|Exactpacketreview|PENDING USER APPROVAL|Batch26afcb7a-b3c1-4708-9009-c3d296e0afdc;canonicalSHAa6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f;unsignedreview|
|Dev originals/Identity/env|PASS|37/53NotScanned/70capture68snapshot/8/2metadata0file;0diff17groups;0DevApply|
|FreshDev+postscanSQLTestbackup/dependencybackup,signature/Apply/verifyafterApply/ACLsmoke/rollback|NOT RUN|Sauexactownerconfirmation;127allowlistPREPARED,bkhTestcũkhôngchứaproofmới|
|Fullsuite/HTTP/UI/crawl/Productionreadiness|NOT RUN / NOT READY|FAST DELIVERY;khôngcộng4SQL+3scanner+3manifestthànhfullsuite;historicalartifactPARTIALgiữnguyên|

Proofminexpiry22/10/2026 09:18:45VN,nhưngdefinitionsđổilàinvalidngay;phảifreshrevalidatebeforeApply. Cácbảngcũdướiđâylàlịchsử.

<!-- PHASE3G-STABILIZATION-CURRENT-END -->

# Nghiệm thu lịch sử — tracking trước ký, 21/10/2026

**PARTIAL; không READY_FOR_APPROVAL, chưa ký/Apply.** [Báo cáo](phase-3g-tracking-fix-report.md), run`phase3g-tracking-20261021T015245Z-a42b32ea`. Người dùng chọn sửa tracking, không phê duyệt batch cũ.

|Tiêu chí mới|Trạng thái|Bằng chứng|
|---|---|---|
|Sửa nhãn missingCrawlRun + checkpoint hash|IMPLEMENTED / BUILD PASS|SourceBatch.cs: Phase3BCheckpointReplay, inventoryId/checkpointSHA; nhánh existing giữ nguyên;0migration; build cuối0warning/error|
|SQL tracking acceptance cuối|FAIL/BLOCKED timeout|0/1PASS,1FAILExecutionTimeout,3NOTRUN; tests-after-reconcile/sql-tests.json/codeDrift0; không gộp0/4timeoutlượtđầu|
|Backup Test/reconcile|PASS|COPY_ONLY/CHECKSUM/VERIFYONLY,2668file0diff; cảhaiattempts0fixturepartialcommit|
|Fresh preflight old packet/new CLI|FAIL/BLOCKED SQLtimeout|exit1, khôngreconcilePASS/khônggiảbáođãtớiguardscanner|
|Scanner freshness old packet|BLOCKED|live118vsproof112; job180receipts/3resourcekhôngnới; newjobreviewproposalonly/chưaregister/scan|
|Dev/Identity/.env preservation|PASS|37/53NotScanned/70capture68snapshot/8/2metadata0file;0diff17groups;Identityenvnguyên|
|Exact approval/real signing/Dev Apply|NOT APPROVED / NOT RUN|User yêu cầu sửa trước ký; khôngdùngREADY20/10nhưtrạngtháihiệntại|
|Fullsuite/HTTP/UI/scannerrefresh/rollbackDev|NOT RUN|FAST DELIVERY; khôngđếmtestlịchsử|

SQLrootUNKNOWN/incidentPARTIAL/CHƯAPRODUCTIONREADY. Sau SQLtests/recoveryreview đạt phải freshpreview và trình exactbatch/hash lại trước ký.

<!-- PHASE3G-TRACKING-FIX-CURRENT-END -->

# Nghiệm thu lịch sử — Phase 3G Final Approval, 20/10/2026

**READY_FOR_APPROVAL cho exact priority batch; NOT APPLIED.** [Báo cáo](phase-3g-final-approval-report.md), [lệnh](phase-3g-final-approval-runbook.md), run`phase3g-final-20261020T031249Z-93f55984`. Batch`14849e55-a85c-4d7f-94cf-1253ac5103ef`; manifestSHA`bad4234f01ff503762c966ccf675cdc9168ca945e5753a8eaa1b21ab85bcbafe`.

|Tiêu chí của lượt mới|Trạng thái|Bằng chứng|
|---|---|---|
|Exact cert + signer policy Development|PASS scoped|CurrentUser chain/EKU/expiry/nonexportable; native fixture Valid; actual PortalConfiguration.Load đọc process pin, không persistent env/Production grants|
|SQL target/schema/capacity|PASS readonly|MinhAnh\SQLEXPRESS/LandPortal_Dev đúngGUID/service/migration; registered THANGPD được chủ máy xác nhận rename, không sửa metadata|
|Fresh native proof|PASS scoped|60TestcopiesClean,defs112,SQLreceipts180/0missing,hash0; chỉ7ảnh mới overlayDev, original53NotScanned giữ nguyên; proof TTL/definition-bound|
|Test backup trước scan|PASS|COPY_ONLY/CHECKSUM/VERIFYONLY,2668filesreadback0diff; firstfailedbackupretained, projectbackupfolder SQLserviceNTFSonlyACL|
|Fresh priority preview/preflight|PASS readonly|3new/4rev/7asset/6alias,6Keep/30Blocked;10eligible0committed0errors; batch chưa Apply|
|Tracking metadata inherited|DISCLOSED / OWNER REVIEW|1 CrawlRun sẽ có fallback Phase3BTechnicalFixture không phản ánh replay nguồn thật; chi tiết trongreport/tracking-preview.json, không dùng làm acceptance nguồn giả|
|Dev originals/Identity/.env|PASS|0diff/17groups,37/53NotScanned/70capture68snapshot/8/2metadata0file|
|Exact batch approval/signature|PENDING_USER_APPROVAL / NOT RUN|Draft hash-bound unsigned; phải có xác nhận batch+SHA trước ký theo chính yêu cầu người dùng|
|Fresh Dev + postscan SQLTest/dependency backup, Apply/verify/repeat/ACL smoke/rollback|NOT RUN|Chỉ sau exact approval; chưa gọiApplied/PASSimport|
|Fullregression/UItoànsite/crawl/migration|NOT RUN|FAST DELIVERY, không appcode thay đổi;16/16 trước là lịch sử|

SQLtimeoutROOTCAUSEUNKNOWN/artifactPARTIAL còn nguyên; scannerProduction/MFA/restore/UAT/cutover chưa được nghiệm thu. **CHƯA PRODUCTION READY.**

<!-- PHASE3G-FINAL-APPROVAL-CURRENT-END -->

# Nghiệm thu lịch sử — Development Code Signing, 20/10/2026

**Cập nhật sau Yes: CodeSigningDev+fixture PASS LOCAL, actual Dev Apply NOT RUN/chưaREADY.** Run`dev-signing-confirmed-20261020T030517Z-a0e9ad77`, [báo cáo](dev-code-signing-report.md). CurrentUserRoot/chain/AuthenticodeValid thật;16/16focused signingchecksPASS0FAIL.0actualapproval/runtimepin;Dev37/53NotScanned/17groups0drift/Identityenvnguyên;LocalMachine/publisher/executionpolicykhôngđổi. Khôngfullregression/SQLbusinessmutation/Apply. BảngPARTIAL bên dưới là checkpoint lịch sử trước Yes; không còn dùng nó làm blockertrust hiện tại. WebsiteCHƯAPRODUCTIONREADY.

<!-- DEV-SIGNING-CONFIRMED-CURRENT-END -->

**PARTIAL, không Apply Dev; CHƯA PRODUCTION READY.** [Báo cáo](dev-code-signing-report.md), [setup/lệnh](dev-code-signing-setup.md), run`dev-signing-20261020T021606Z-c89a0a6f`.

|Tiêu chí|Trạng thái|Bằng chứng|
|---|---|---|
|Dev certificate đúngmụcđích|PASS create/inspect|CurrentUserMy/RSA3072/SHA256/CodeSigningEKU/NonExportable/30days;thumb94797BD79E275B09DEF613036BC00DF4EA91345F;khôngprivatekeyexport|
|CurrentUser trust/AuthenticodeValid|PENDING/BLOCKED|WindowsSecurityWarning;Rootentrychưacó;fixtureUnknownErrorUntrustedRoot;khôngbypass/LocalMachine/globalpolicy|
|Fixture thực|SIGNED / POSITIVEVERIFYBLOCKED|Test-only/FixtureVerify,metadatahash/operator,khôngrealapproval;scope suite0caserununtilValid|
|Focused script checks|PASS scoped|6parse/5negativeguards;actualCLI safe-verify rejectsfixturebeforeSQL;khônggộpthànhfullsuite|
|Review/business/runtimeauthority|PENDING_USER_APPROVAL/NOTCONFIGURED|draftonly,ApprovedBy/reference null;khôngsignedDevapproval/appthumbpin/.env/grants|
|Target rename|USER_CONFIRMED|ChủmáyxácnhậnTHANGPD→MinhAnh/cùnginstance;khôngsửametadataSQL/guard|
|Apply/fullregression/Production|NOTRUN/NOTREADY|KhôngSQLbusinessmutation/DevApply;ProdMFA/scanner/restore/UAT/artifacts/SQLtimeoutgiữriêng|

<!-- DEV-SIGNING-CURRENT-END -->

# Nghiệm thu lịch sử — Phase3G Fix

**PARTIAL; Apply Dev BLOCKED; CHƯA PRODUCTION READY.** [Báo cáo](phase-3g-fix-report.md), run`phase3g-fix-20261019T095047Z-94f0e99e`.

|Tiêu chí|Trạng thái|Bằng chứng thực|
|---|---|---|
|Identity cùnginstance|PASS read-only / RECONFIRM tuple|2endpoint cùngPID20776/service/databaseGUID;MachineMinhAnh/@@SERVERNAMETHANGPD;khôngsửaSQLguard|
|Signer/trust/authority|BLOCKED|0CodeSigning trong2storeMy/0trustedthumbprint/0signedfile;businessapprovalđãUSER_CONFIRMED;guidekhôngtựtạocert|
|Recovery refresh scope60|PASS nativeTest|60Clean/0infected/0failed/hash0/receipt0missing;newdefinitions102/proofsetSHAe4e554...;Dev53NotScannednguyên|
|Engine controls sauharnesscuối|PASS riêng; firstFAIL giữraw|Clean/EICARZIPInfected/cancellationScanFailed;khôngmock hoặcAVpolicychange;khôngcoi60scanlàregression|
|Freshprioritypreview/preflight|PASS read-only|3new/4rev/7asset/6alias/30Blocked;ABSENT_CONFIRMED10eligible0receipt0errors|
|Expandedpreview/preflight|PASS read-only / NOTREADYAPPLY|210new/211rev/528asset/420alias/55Blocked;giao3priority+207Test3F;3Articlealiasblocked;521assetngoàirefreshscope|
|Capacity|PASS snapshot / recheckbeforeApply|Dev72MiBallocated/10.75MiBused,Cfree~163.66GiB;khôngđổiSQLExpresslimit/config|
|Bảo toàn|PASS|0diff17groups37/53NotScanned/70capture68snapshot/8/2metadata0file;Identity/env/appcode0drift|
|BackupDev/Apply/verify/repeat/rollback/smoke-new|NOTRUN/BLOCKED|chưaauthority/signature/freshDevbackup;khôngbáoPASSApply|
|Fullregression/UI/video/Production|NOTRUN/NOTREADY|chỉfocusedidentity/proof/preview;SQLtimeoutUNKNOWN/artifactPARTIALgiữriêng|

<!-- PHASE3G-FIX-CURRENT-END -->

# Nghiệm thu lịch sử — Phase3G

**BLOCKED trước Apply, không có PASS nghiệm thu Dev import. CHƯA PRODUCTION READY.** [Báo cáo](phase-3g-report.md), [runbook](phase-3g-runbook.md), run`phase3g-20261019T092316Z-24fe1477`.

| Tiêu chí | Trạng thái | Bằng chứng thực |
|---|---|---|
|Phê duyệt nghiệp vụ Apply Dev|USER_CONFIRMED|Chủ dự án duyệt batch manifest/backup/đích/bảo toàn; không thay signer gates|
|Exact target @@SERVERNAME|BLOCKED|THANGPD\\SQLEXPRESS khác MinhAnh\\SQLEXPRESS đã duyệt; SERVERPROPERTY/MachineName vẫnMinhAnh;DB/Expected đúngDev;không tự sửa|
|External signature/trust policy|BLOCKED|0configured signer/0signed file;không self-sign/pintrust/grants|
|Dry-run Dev ưu tiên|BLOCKED|CLIexit1 trước plan: recoveryproof definitions1.461.96.0 khác live1.461.102.0;TTLchưahết;rawloggiữ|
|Schema/metadata|PASS read-only|Dev/Test cùng10migration/latest20261015091551;0Phase3B/CreceiptDev;5SampleImported;không thấyactiveuserwritetransaction tại snapshot|
|Manifest/source readback|PASS read-only|65dependency và7ảnh0hashmismatch;exact210sourceContentIdTest/0Dev;khôngcopytechnicalfixtures|
|Fresh Dev batch count/manifest|NOT READY|3new/4revision/7asset/6alias/30Blocked là sốlịchsử;dry-run3Gchưaxuấtplan;210newcầnDevbindingriêng|
|Bảo toàn Dev/Identity/env|PASS|0khác17nhóm;37content/53NotScanned41ảnh12PDF/70capture68snapshot/8ngoạilệ/2metadata0file;digest/envnguyên|
|Backup mới/Apply/verify/repeat/rollback/smoke-new|NOT RUN|Dừng do gateđích/signature/proof;khônggọiNOTRUNlàPASS|
|Build/full137/UI/video/crawl|NOT RUN|Khôngsửacodeứngdụng,17targetedcodepath0drift;chỉkiểmtraliênquan|
|SQL timeout/artifact/Production|UNKNOWN/PARTIAL/NOTREADY|Khôngtựđóngrootcause/incident;giữriêngscannerProd/MFA/restore/UAT/Word/lookup/cutover|

<!-- PHASE3G-CURRENT-END -->

# Nghiệm thu lịch sử — Phase3F

**PARTIAL toàn nguồn; phần Test đủ điều kiện PASS LOCAL; CHƯA PRODUCTION READY.** [Báo cáo](phase-3f-report.md).

| Tiêu chí | Trạng thái | Bằng chứng |
|---|---|---|
|Public collectionauthority|USER_CONFIRMED|Người dùng xác nhậnQLDD+gdla; khôngapprovalđộc lậpmới;Devquyềnriêng|
|QLDD thu thật/offlineparser|PASS LOCAL phạm vi; tổngUNKNOWN|272typed/267canonical,11thiếusnapshot/3parse-review;819GETmới;khôngfake2024/3|
|ScannerpublicTest|PASS LOCAL thật|542SHAnewClean,0Infected/ScanFailed,engineDefender;PNGRejectedkhôngpublic;Dev53nguyên|
|NhậpSQLTest|PASS LOCAL eligible/PARTIALblocked|+210contents/+524assets;25blockedtypedrows,causeoverlap;aliasduplicatekhôngautomerge|
|Reconcile/repeat/verify|PASS LOCAL|partial197/53/0→planmới41new/206Keep;final247Already/25Blocked,0verifyerrors/DataSHAgiữ|
|Finalfocused/HTTP|PASS LOCAL9/9 và10HTTP riêng|`phase3f-tests-20261019T083607Z-ee927b17`;0codeDrift;full137NOTRUN;oldFAILrawgiữ|
|Bảo toàn|PASS LOCAL|Dev0diff17groups37/53NotScanned/70capture68snapshot/8/2metadata0file;Test40/60cũnguyên;Identity/envnguyên|
|Devpacket|READY_FOR_TECHNICAL_REVIEW/NEEDS_DEV_APPROVAL|freshplan3new7image1comparison;manifest/commands;signer/business/freshDevbackupchưacó;ApplyNOTRUN|
|NukeViet|PARTIAL / TRANSPORT_BLOCKED / MAPPING_PENDING|HTTPSrefused443;0realimport;offlinebundlekhôngphảiexportnghiệpvụ|
|Backup/restore|BACKUP PASS / RESTORE NOTRUN|Test7602datafilesVERIFYONLY+65proofdependency;khôngsealrepotoànbộ|
|SQLtimeout/artifacts/Production|UNKNOWN / PARTIAL / NOTREADY|khôngtăngtimeout/config;giữnguyênV04/D15/v05incidents,Prodscanner/MFA/UAT/Word/lookup/cutover|

<!-- PHASE3F-CURRENT-END -->

# Nghiệm thu hiện hành — Phase3E

**Pipeline SQLTest PASS LOCAL; rootcause lịch sử UNKNOWN / điều tra PARTIAL. CHƯA PRODUCTION READY.** [Báo cáo](phase-3e-sql-timeout-report.md).

| Tiêu chí | Trạng thái | Bằng chứng |
|---|---|---|
|Chẩn đoán connect/preflight/commit/readback|PASS LOCAL instrumentation; lịch sửUNKNOWN|operation/batch/fingerprint/session/timing/transaction;DMVrequests/sessions/locks/waits/resource aggregates scoped;khônglogsecret|
|Nguyênnhân timeout cũ|PARTIAL / NOT_REPRODUCED / UNKNOWN|khôngtái hiệntrước/saufix;khônggánSQLcũkhi thiếucommandtrace;categoryincident vẫnriêng|
|Giảmtruyvấnlặp proof|PASS LOCAL|61→2Auditqueries,60genuineproof,copy khôngreceipt bịchặn;estimatedplan scan23.316rowskhôngphảiactualIO|
|CommitbatchthậtTest|PASS LOCAL|batch7768b8bf-25ee-4b6e-99e8-94b117a72694;37Keep/3Blocked;37receipt;0sourcecontent/asset/revisionmới|
|Readback/verify/repeat|PASS LOCAL|37source/37revision/58media/79aliasbatch;whole40/60;0errors,repeat37Already/3Blocked/0delta,DataSHAgiữnguyên|
|COMMITuncertain/partial/stale/rollback|PASS LOCAL Test|SQLCOMMIT thật+clientfaulttiêmcónhãn;NeedsReconciliation→verified0dup;partialresume/hashconflict/stale/precommitrollback;khôngrollback37Keepnguồn|
|FinalfocusedSQL|PASS LOCAL5/5|phase3e-tests-20261019T055423Z-39200874;17code0drift,Web/CLI/Tests0warn/error;khôngfull137|
|FinalHTTPsmoke|PASS LOCAL riêng11checks|affectedrealpages/JPEG/PDFSHA/Range206/guestpreviewACL;owned5310stopped;khôngcộng5+11thànhsuite|
|Dev/source53/Identity/env|PASS LOCAL|0khác17nhóm37/53NotScanned/70capture68snapshot/8/2metadata0file;cp3A/seal3Dfilegiữ|
|Devbatchreview/apply|READY_FOR_TECHNICAL_REVIEW / NEEDS_APPROVAL;ApplyNOTRUN|3new/7ảnhđềnghịDev;chưafreshpreviewDev3E;external signer/backup/proof/authorityvẫncần|
|Migration/index/timeout/configchanges|NOT REQUIRED / NOT PERFORMED|khôngthaySQLserver/.env/quyền/CommandTimeout;khôngKILL/NOLOCK|
|Full/video/Worker/Production/UAT/restore/crawl|NOT RUN / NEEDS_APPROVAL / BLOCKED|scopeFAST;2parser/228snapshot/NukeVietkhôngkéodàiP0;historicalrawincidentsPARTIAL|

<!-- PHASE3E-CURRENT-END -->

# Nghiệm thu hiện hành — Phase3D / SRS1.0

**Software focused PASS LOCAL; tổng thể PARTIAL / CHƯA PRODUCTION READY.** [Báo cáo](phase-3d-report.md), [runbook](phase-3d-runbook.md).

| Tiêu chí | Trạng thái | Bằng chứng |
|---|---|---|
|SRS1.0/two sources/FR17name|READY nghiệp vụ; Test PASS LOCAL; Dev NEEDS_APPROVAL|380đoạn đọc đầyđủ; nameTestaudit/idempotent,Dev/menu không đổi|
|Audit512/270|PASS LOCAL offline; tổng nguồnUNKNOWN|40verified/228thiếudetail/2parse-review,0new,0GET; CSV từngidentity|
|NukeVietnamespace/parser/dedupe|PASS LOCAL kỹ thuật / PARTIAL nguồn|fixtuređúngnhãn/canonical/hash/title-onlynegative;0realNukeViet,categorymapping/writeadapterchưađủ|
|Plan targetstore và Dev package|PASS LOCAL binding; PRESENTABLE/NEEDS_APPROVAL|schema3; wrongstore3actionsbịchặn;3new/7images/1comparison/6Keep; external signer/backupfresh chưa có|
|301NukeViet .html/http/https|PASS LOCAL kỹ thuật|HTTP301 đúngpublished/collision404/withdraw404; khôngAC05.1hai nguồn50URLthật|
|SQLTest technical safeapply/rollback/media/scanner|PASS LOCAL|genuineClean/EICAR/failure,60reuse;fault/orphan/retry/verify/rollback/ACL,12PDFRange trong11|
|Final focused|PASS LOCAL 11/11|phase3d-tests-20261019T044754Z-39e2caf3;24code0drift;Web/CLI/Tests0warn/error;khôngfull137|
|Freshreal40 Testbatch commit/repeat/verify|BLOCKED SQLTIMEOUT|preview37Keep/3Blocked;commit+1retry trướcreportmkdir;không cóapply.json,khônggọiPASS|
|FinalTestSQLreadback|BLOCKED TIMEOUT|before có40/60;finalFS60hash0sai,không thay SQLreadback bằngmock|
|Dev/source/Identity/env|PASS LOCAL|0khác17nhóm;37/53NotScanned/70capture68snapshot/8/2metadata0file;Identityenvnguyên|
|PDF3233/Video40/41|BLOCKED / NEEDS_APPROVAL|3identity;PDFformat/derivedprovenance/scan,video thiếufile;gdla scopechốt nhưngchưa tải|
|SQLtimeout/artifacts|MONITORING/UNKNOWN; historicalPARTIAL|SLEEP_BPOOL_STEAL mộtquan sátTestkhông chứng minhrootcause;rawlịchsửkhông phục dựng|
|DevApply/full/Worker/video/restore/UAT/Production|NOT RUN/BLOCKED/NEEDS_APPROVAL|giữgate riêng;không tựgrant/.env/deploy/DNS/email/crawl|

<!-- PHASE3D-CURRENT-END -->

# Nghiệm thu hiện hành — Phase3C

**Test software PASS LOCAL có bằng chứng; tổng thể PARTIAL; Dev chưa Apply.** [Báo cáo](phase-3c-media-recovery-report.md), [runbook](phase-3c-runbook.md).

| Tiêu chí | Trạng thái | Bằng chứng |
|---|---|---|
|53/29 mapping,quétcopyTest+7refresh |PASS LOCAL|60Clean thật,engine/definitions/SHA/policy/TTL;CSV từngSHA→identity|
|Resume/ràng buộc/reuse |PASS LOCAL|2+58native;60reuse/0receipt mới;jobmanifestSQLbinding,policy sai bị từchối|
|StageTest |PASS LOCAL phạm vi được phép|53mirrorTestClean/audit;53DevNotScanned;0filehashđổi|
|Giải phóng30 |PARTIAL|27/30Test;3cònblock;Devvẫn30block,không tựáp copyproof|
|Import/repeat/verify |PASS LOCAL|0newcontent/assets,2comparison;35Keep/2Comparison;repeat37Already/3Blocked,0delta/0verifyerror|
|SafeApply/rollback/filesystem |PASS LOCAL Test|backupthật/quyềnoperator;SQLfault giữfileprivate/retry;withdrawgiữsource/rev/file/alias;privateClean404|
|NativeControl/HTTP |PASS LOCAL|PNGClean/EICARInfected/healthcancelScanFailed;60mediahash/12PDFRange/ACL/smoke|
|Focused codecuối |PASS LOCAL|6/6phase3c-tests-20261019T035806Z-8c569e1c;17code0drift,build0warn/error;khôngfull|
|PDF3233/video40/41 |BLOCKED / NEEDS_REVIEW|candidatePDFsignaturechưaverified;video40đúngIDchưa cófile,41gdla ngoài quyền|
|Devprepare |PREPARED/BLOCKED_EXTERNAL_APPROVAL_POLICY|manifest/lệnhthật,cầntrustedexternalapproval;positive kýDevNOTRUN;0Devapply|
|Bảo toànDev/Identity/env |PASS LOCAL|17nhóm0khác;37/53/70capture68snapshot/8ngoạilệ/2metadata0file|
|Full/video/Worker/restore/UAT/Production |NOT RUN/BLOCKED|giữgatesriêng;khôngcộngrun/tests/thôngsốthànhfull|
|SQL timeout/artifactlịchsử |UNKNOWN/PARTIAL|khôngtăngtimeout/khôngphục dựngrawgiả|

CHƯA PRODUCTION READY. Các checkpoint dưới là lịch sử.

<!-- PHASE3C-CURRENT-END -->

# Nghiệm thu trước — Phase3B Unblock

**PASS LOCAL importer có bằng chứng; tăng dữ liệu PARTIAL/BLOCKED; Dev NOT_READY_FOR_APPROVAL / NEEDS_APPROVAL.** [Báo cáo](phase-3b-unblock-report.md), [30 danh tính](phase-3b-unblock-analysis.md).

| Tiêu chí | Trạng thái | Bằng chứng |
|---|---|---|
| Phân tích30 và nguyên nhân giao nhau | PASS LOCAL |30 existing;29 mục/53NotScanned bằng đúng53gốc;1PDFformat;2videofilethiếu;1ảnhphụrỗng |
| Ảnh thiếu tùy chọn có review / metadata tùy chọn | PASS LOCAL kỹ thuật |Không bỏ tệp chưaquét; giữtext/caption; warningSQL đúngrevision/adminhistory; fixture không tính nguồn |
| Nhập thêm từ30 hiện tại | BLOCKED |0/30 đủ điều kiện;0 bài/asset thật mới;3681 bỏ ảnhrỗng vẫnscannerblock |
| Build / focused sau sửa cuối | PASS LOCAL |7/7 `phase3b-tests-20261019T031651Z-6aa5ff6f`;0warning/error;code347/raw12408,0drift |
| SQL Test idempotence/rollback/conflict/không ghi đè editor | PASS LOCAL |7 nhóm gồm fixture vàHTTP;repeatbatchgốc10AlreadyCommitted/30Blocked,0delta;verify0mismatch |
| HTTP bài/chuyên mục/7ảnh thật/guestACL | PASS LOCAL |Owned5301 đã dừng,logriêng;không cộng sốGET thành nhóm |
| Bảo toàn Dev/Identity/env và source40Test | PASS LOCAL |17nhóm0khác;53gốc cảDev/TestNotScanned;60fileliênquan0saihash;0source40drift |
| Dev manifest | PREPARED, NOT_READY_FOR_APPROVAL |3new/7JPEG/1comparison/6Keep/20mapping;chưa có entrypointApply/rollbackDev đượcduyệt |
| Full137/unrelatedCMS/video/scanner mới/UIadmin/restore | NOT RUN lượt này |FAST DELIVERY importer-only;137 là lịch sử;không dùngfixture/VERIFYONLY làmUAT/restore |
| SQL timeout/artifact lịch sử | UNKNOWN / PARTIAL |Không đổitimeout,khôngtái tạo raw |

CHƯA PRODUCTION READY. Mốc trước dưới đây là lịch sử.

<!-- PHASE3B-UNBLOCK-CURRENT-END -->

# Nghiệm thu trước — Phase3B

**Test batch PASS LOCAL; tổng thể PARTIAL; APPLY DEV NEEDS_APPROVAL.** [Báo cáo](phase-3b-import-report.md), [manifest](phase-3b-import-manifest.md), [runbook](phase-3b-runbook.md).

| Tiêu chí | Trạng thái | Bằng chứng |
|---|---|---|
| Baseline trước sửa | PASS LOCAL |2503 seal0 drift;17 nhómDev0 khác |
| Phân loại40/mapping/diff/preview | PASS LOCAL |37 existing +3 new;trục policy30 Blocked/10 đủ |
| Test Commit/transaction/checkpoint/resume/idempotent | PASS LOCAL |3 new/7 assets/1 comparison/6 Keep;repeat0 mới;verify0 mismatch |
| Source/editorial/public isolation/alias collision/concurrency | PASS LOCAL |SQL technical fixtures và regression trênSQLTest |
| File mới exact-byte Clean/ACL/HTTP/UI | PASS LOCAL phạm vi |7ảnh proof thật3A;3 bài public;6 alias301→200;14 desktop/mobile |
| Nhập PDF/video mới trong tập này | BLOCKED |cachePDFNotScanned;video chỉmetadata;0file |
| Full code cuối | PASS LOCAL |137/137 exit0,phase3b-tests-20261019T024059Z-ea19272e;không cộng counts |
| Dev preservation/backup/preview | PASS LOCAL đọc |17 nhóm0 khác;Identity/env nguyên;VERIFYONLY+hash |
| Apply Dev | NEEDS_APPROVAL |CLI từchốiDevwrites;chưanhậpDev |
| Mapping domain thật/full nguồn | NOT RUN / NEEDS_APPROVAL |85 đềxuất,chưaDNS/redirectProduction;tổngUNKNOWN |
| Processkill/restore/UAT/Production policies | NOT RUN / BLOCKED |không dùngfixture hoặcVERIFYONLY thayrestore |
| SQLtimeout/artifact lịch sử | UNKNOWN / PARTIAL |không tăngtimeout,khôngtái tạo raw |

CHƯA PRODUCTION READY. Các mốc dưới đây là lịch sử.

<!-- PHASE3B-CURRENT-END -->

# Nghiệm thu hiện hành — Phase3A

**PARTIAL tổng thể, không phải chuyển toàn bộ dữ liệu.** Full code cuối129/129 PASS tại `phase3a-tests-20261015T182634Z-0cf9b9da`. [Báo cáo](phase-3a-crawl-report.md), [inventory](phase-3a-source-inventory.md), [runbook](phase-3a-runbook.md). Không cộng123 cũ/focused/HTTP/UI/worker thành full mới.

| Mục | Trạng thái | Bằng chứng |
|---|---|---|
| Baseline trước sửa | PASS LOCAL |815 seal0 drift;17 nhóm0 khác |
| Discover/fetch/raw/extract/download/normalize/private stage | PASS LOCAL mẫu |512 URL/270 candidate/40 verified;62 private file;39 GET |
| Phân trang khác tập và terminal | PASS LOCAL phạm vi quan sát | Chuyên mục75 trang1–4, pháp luật1/2/10; phần còn lại chưa đầy đủ |
| SQL Test checkpoint/lock/dedupe/resume/retry/tamper/cutover/guard | PASS LOCAL |6 nhóm mới trong full129; lỗi mạng429/304 là fixture ghi nhãn |
| Scan file mới đúng bytes | PASS LOCAL |7 JPEG Defender thật;55 cache và53 original vẫnNotScanned |
| Regression code cuối | PASS LOCAL |129/129 exit0; HTTP/UI/worker là evidence con |
| Bảo toàn sau sửa | PASS LOCAL |17 nhóm0 khác, Identity/env nguyên;804/815 hash cũ còn giống,11 delta code/docs có chủ đích,0 drift raw ngoài dự kiến |
| Quét rộng | NEEDS_APPROVAL | Chưa có PO/source-owner approval |
| Tổng toàn nguồn/83/81/cơ cấu/chỉ đạo | PARTIAL / NEEDS_SOURCE | Không kết luận0 hay100%; browser nguồn PARTIAL |
| Video/DOC cũ/host gdla | PARTIAL / BLOCKED |0 video file, adapter/host chưa được phép |
| Kill collector giữa response; restore; batch CMS import/publish | NOT RUN | Không dùng new-context hoặc fixture thay thế |
| SQL timeout; artifact lịch sử | UNKNOWN / PARTIAL | Giữ nguyên incident, không tái tạo raw |

PO group2/private lookup/ba Word nghiệp vụ/Production scanner/MFA/SMTP/restore/UAT/cutover chưa được nghiệm thu thêm. Website CHƯA PRODUCTION READY. Lịch sử các mốc bên dưới giữ nguyên.


<!-- PHASE3A-CURRENT-END -->
---

# V05-Finish — checkpoint cuối, 15/10/2026

**PASS LOCAL phạm vi kỹ thuật đã kiểm chứng; nghiệm thu tổng thể PARTIAL / CHƯA PRODUCTION READY.** [Báo cáo](phase-v05-finish-report.md), [UI/ảnh thật](ui-review-v05-finish.md), [hướng dẫn admin](v05-finish-admin-guide.md), [runbook tài liệu](editorial-documents-runbook.md).

Full regression sau sửa code/scripts cuối: **123/123 PASS, 0 FAIL, exit0**, `v05-finish-tests-20261015T134049Z-dc3d22a1`; 2026-10-15T13:40:58.1496362+00:00–2026-10-15T13:50:24.6112382+00:00. Build Web/Worker, audit dependency và SQL/native/HTTP/UI đều nằm trong raw run; không cộng page/action/worker thành nhóm mới. 325 code paths/0 drift; 9104 artifact được bảo vệ/0 drift. Các raw FAIL trước vẫn giữ, 117/117 cũ không phải kết quả lượt này.

Đã hoàn thiện mẫu menu FR17 draft/live (giữ14 chuyên mục, lọc nhóm2 chưa duyệt), search chung chỉ hỏi đáp đã công khai có đồng ý/SQL paging/tiếng Việt, CAPTCHA login **và reauth**, DOCX/PDF/XLSX private quarantine/scan proof/ACL/exact bytes/worker SQL, Word upload→scan thật→preview sửa→nháp→duyệt đúng revision→publishv1/v2→restorev3. PDF Uploaded cũ phải enqueue/quét thật trước publish; không áp chính sách này để tự thay53 tài nguyên nguồn.

Migration **20261015091551_V05EditorialDocuments** đã Test trước, Dev sau verify đúng target/backup và application full123 run130537 (trước các sửa helper kiểm thử); full sau sửa cuối là run134049 ở trên. Additive, backfill0. COPY_ONLY/CHECKSUM/VERIFYONLY: kho Test2493file/Dev131file, đọc lại0sai hash; chưa restore drill. Đối chiếu sau Dev migration/smoke: **0 sai khác/17 nhóm**,37content/47revision/74alias,53source **NotScanned** (41ảnh/12PDF),70capture/68SQLsnapshot,8ngoại lệ,2video metadata/0file. Identity và.env nguyên;0EditorialDocuments/0Interactions Dev. GET login tạo nonce kỹ thuật, không tạo hồ sơ nghiệp vụ.

Menu FR17 **chưa Apply Dev**: không tự cấp10quyền vận hành và4quyền v0.5 còn thiếu. Upload/quét tài liệu mới chỉ Development + SQLTest + Teststorage; scanner thật Microsoft Defender chỉ SHA ẩn danh/công khai được cho phép, không phải scanner Production/PII. AC16.1 **BLOCKED:NO_REAL_WORD_SAMPLES**; nhóm2NEEDS_PO/pháp chế; lookup riêng tư khóa; public history lịch tuầnNEEDS_PO. MFA/SMTP thật/restore/UAT/domain/TLS/cutover/full migration chưa nghiệm thu. Incident V04/D15/diagnosticv05 vẫnPARTIAL; SQL timeout **MONITORING / ROOT CAUSE UNKNOWN**.

Preview hiện có **http://127.0.0.1:5288/**, `/admin/login`, `/lich-tuan`, `/cong-khai-thong-tin`, `/tim-kiem`, `/van-ban`;7GET200 riêng, không cộng full. Preview copy từ application code đã đạt123 trước sửa tham số health helper; code ứng dụng không đổi. Các port cũ5238/5258/5268 không có listener lúc kiểm tra; không xóa build/artifact hoặc dừng tiến trình cũ.

Tiếp đúng chỗ: chờ PO/tên/lookup/history và3Word nghiệp vụ để UAT; người có thẩm quyền duyệt cấp quyền/menu Dev và scanner Production trước khi mở upload ở môi trường khác. Không dựng lại, không nhập lại mẫu nguồn. Task `.local/evidence/phase2d/v05-finish-20261015T090456Z-b90982c8`, full `.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1`; xem manifest cuối và changed-code.json. Không.env/grantDev/emailthật/Git/deploy/DNS/service/fullcrawl.



| V05-Finish / vùng | Trạng thái hiện tại | Bằng chứng / giới hạn |
|---|---|---|
| A FR17 menu, nhóm2 pending,14 mục cũ | PASS LOCAL SQL/HTTP/UI; Apply Dev NOT RUN | Template immutable draft/live và filter pending; không tự cấp quyền |
| A search chung | PASS LOCAL | Public QA snapshot/consent; dấu/không dấu/đ-d/paging/filter; private/contact/rejected/stale không lọt |
| B CAPTCHA login/reauth | PASS LOCAL | SQL nonce purpose/session/TTL/one-use; wrong/expired/replay/missing/directCSRF; Identity vẫn bắt buộc; MFA Production chưa nghiệm thu |
| C PDF/DOCX/XLSX | PASS LOCAL Test; Production BLOCKED | Defender thật Clean3định dạng/EICAR Infected/ScanFailed/tamper/ACL; source53 nguyên NotScanned; oldUploadedPDF phải queue thật |
| D Word technical workflow | PASS LOCAL | Ô gộp,7/8cột,cảnh báo vị trí,preview sửa,originalhash,revisionv1/v2/restorev3,Range,privateACL; fixture Test |
| AC16.1 ba tuần nghiệp vụ | BLOCKED | NO_REAL_WORD_SAMPLES, không dùng fixture làm UAT |
| Bảo toàn 2A/B/C/D/V04/v05 | PASS LOCAL full123/123 | 0diff17nhóm, Identity/.env nguyên; không thay kết quả lịch sử |
| Tên nhóm2/lookup/history | NEEDS_PO / LOCKED | Không công bố danh mục chưa duyệt hoặc mở chi tiết hồ sơ riêng tư |
| SQL timeout, artifacts lịch sử | MONITORING/UNKNOWN; PARTIAL | Không tăng timeout; không dựng lại raw đã mất |
| Production scanner/MFA/SMTP/restore/UAT/cutover | BLOCKED / NOT RUN theo từng gate | Không tự triển khai; VERIFYONLY không phải RESTORE |

<!-- V05-FINISH-CURRENT-END -->

---

# Checkpoint SRS v0.5 — 15/10/2026

**V05 PARTIAL / CHƯA PRODUCTION READY.** Triển khai trực tiếp delta trên hệ hiện hữu;2D giữ chức năng/E2EPASSLOCAL nhưngoverallPARTIAL/D15. [Báo cáo](phase-v05-delta-report.md), [gap](srs-v05-gap-matrix.md), [readiness](gdla-cutover-readiness.md), [runbook](gdla-runbook.md), [UI/ảnh thật](ui-review-v05.md).

Full saucodecuối **117/117,0FAIL,exit0**, run`v05-tests-20261015T082521Z-51675950`,08:25:27–08:33:04UTC15/10; buildWeb/Worker0warning/error,audit7projects không báo advisory;307codepaths/0drift,5836artifactguard/0drift. V05UI18checks/2actions3901440,HTTP5checkpoint,worker/native/SMTPsink/video2D córeport riêng, khôngcộngthànhfullgiả. Focused9/9 trước sửa nhãncuối; baseline105/108(3FAIL) vàfull114/115(videoMemoryWatchrace)giữriêng.

Đã cóCAPTCHA4publicflows→SQLPending/private/receipt/outboxhonest; Contact1file10MiB/private/scanthật/ACL/notes/Excel; lookupchi tiếtkhóa; hỏiđápđãduyệt keyword/field/SQLpaging; trustedcanonical/host-awarealias/sitemap; FR17groups/metadatarevision/filter/two-step/no-comment; lịch tuầnmanual→SQL/privatepreview/releasev1-v2-restorev3; DOCXtechnicalreader. User xác nhận giữ nguyên nhóm2NeedsNameApproval/khôngpublic, lookupchờPO/ATTT, **AC16.1BLOCKED:NO_REAL_WORD_SAMPLES**. [Quyết định](decision-required-private-lookup.md), [admincontent](v05-content-admin-guide.md), [admininteraction](v05-interaction-admin-guide.md).

Migration **20261015075352_SrsV05PublicFormsAndContent** additive: Testbackup2086file/Dev130file COPY_ONLY CHECKSUM VERIFYONLY; Testtrước, Devsau117full/verifytarget/backup.0backfill,0khácbiệt17nhóm. Devgiữ **37content/47revision/74alias/53asset(41ảnh12PDF),70capture/68snapshot,8ngoạilệ,2video metadata/0file;53/53NotScanned**. Identity/password/stamps/roles/claims và.env nguyên. MớiDev3groupseeds,0CAPTCHA/attempt/release/interaction/videofile; khôngtestnghiệpvụDev/grantquyền/importdemo. ChưaRESTORE.

Video2D chỉ sửa hẹpMemoryWatchrace khi tiếntrìnhvừaexit (đã táihiệnWorkingSet64exception), giữlimit/exit/probe/MP4/poster/scan/quyền; fullvideoE2EsaufixPASS. **Incidentv05PARTIAL:** helperdiagnostic đãghiđèJSONrawđầu, khôngcóhash/copyđểkhôiphục; transcript/SQLFAIL/full114rawcòn. [Incident](v05-diagnostic-artifact-incident.md), khôngtáitạogiả. Recheck520legacyraw/0drift khôngđồngnghĩa mọiartifacttoànlượtgiữđủ. V04/D15PARTIAL riêng vẫnnguyên. SQLtimeout **MONITORING / ROOT CAUSE UNKNOWN**, khôngtăngtimeout/stressDev.

Previewmới **http://127.0.0.1:5268/** (bảncopybinaryriêng), `/admin/login`, `/lich-tuan`, `/cong-khai-thong-tin`, `/tra-cuu-ho-so`, `/sitemap.xml`;7GETreadonly200. Cũ5238/5258health200 giữnguyên. Chạy`rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-v05.ps1 -Region All`; chạyweb`... -File scripts/run-dev.ps1 -V05 -NoBuild -Port 5268` (portđangcópreview, chọnportkhác nếu chạy thêm). Build`.local/build/phase2d/v05-current`; task`.local/evidence/phase2d/v05-20261015T073755Z-f680ab6e`; raw117run/ảnh/hash/exit giữriêng. Khôngdeploy/DNS/.env/Git/emailthật/service/AVpolicy.

**Tiếp đúng chỗ:** Wordupload/scanner/preview-edit/fileoriginalreview/download +3Wordthật; FR17Excel/PDFscanproof/typedmenucatalog draft-live; CAPTCHAadminlogin/reauthregression, searchchungQA, Contactlist-onlynegative sâu hơn vàUXbảnglịch. PrivateLookupsecretonlyproposal, chưaPO/ATTTduyệtthìkhôngmở. P0/ReleasegatesTLS/MFA/scannerprivate/SMTP/restorethật/fullmigration/UAT/monitoring/domainauthority chưađóng;10quyềnDev cũ+4catalogquyềnmới chưagrant. Khôngtựsang2E/2F/Phase3/fullcrawl.

<!-- V05-CURRENT-END -->

---

# Checkpoint 2D — 15/10/2026

**2D PARTIAL vì bằng chứng lịch sử D15 bị thiếu; chức năng/E2E đạt PASS LOCAL. Toàn dự án CHƯA PRODUCTION READY.** [Báo cáo](phase-2d-video-report.md), [incident artifact](phase-2d-artifact-incident.md), [ảnh thật](ui-review-2d.md), [hướng dẫn biên tập](video-admin-guide.md), [runbook](video-worker-runbook.md), [kiến trúc](video-media-architecture.md), [summary](evidence/phase2d/video-20261015T035659Z-8b55ff16/summary.json).

Đã triển khai upload chunk/resume từ máy, SQL/private storage, kiểm quyền/hash/quota, worker có lease, quét Defender thật, FFmpeg/ffprobe9.0.1 tạo MP4 H.264/yuv420p/AAC và poster thật, phụ đề VTT, association theo revision, approval/scheduler, Range/seek và thu hồi tham chiếu dùng chung. Không làm lại 2A/2B/V04/2C.

Full cuối **108/108, 0 FAIL, exit0**, `video-tests-20261015T071425Z-1c567963`, **278 đường dẫn mã/0 drift**. Guard bảo vệ **520 artifact cũ/0 drift** trong lượt cuối. Video HTTP7 checkpoint; UI12 trang/kích thước ở390/1440 +8 thao tác, ghi riêng. Compiler Web/Worker0 warning/error. Giữ các lượt104/108,106/108,107/108 FAIL và các lượt focused riêng; không cộng hoặc sao chép100/100 SCAN cũ. Đã sửa WebM cóavgFPS0/0 bằng fallbackrFPS hữu hạn≤60; H.2644:4:4 phải encode sang4:2:0, không remux mù.

Dev giữ **37 nội dung,47 revision,74 alias,53 tài nguyên(41ảnh/12PDF),70capture/68SQLsnapshot,8ngoại lệ,2video metadata/0tệp**.53originals vẫnNotScanned; **0 khác biệt/17 nhóm**, Identity/password/stamps/roles/claims và.env nguyên. Test đã migrate trước, Dev đã áp hai migration additive `20261015041226_VideoUploadPipeline` và `20261015044936_VideoSubtitles` sau xác minh target/backup mới/VERIFYONLY/rehash. Test backup346/617file; Dev130file. Các bảng video Dev0hàng; chưa RESTORE, không ghi nghiệp vụ hoặc cấp quyền Dev.

**D15 PARTIAL:** testcase legacy dùng lại `.local/evidence/phase2b/worker-web-off.json`; chưa có bản raw trước2D để khôi phục có chứng cứ. Đã sửa đường dẫn run, logfinally và hash guard. Không tái tạo raw giả; V04rawsmoke2B là incident riêng vẫnPARTIAL. SQL timeout vẫn **MONITORING / ROOT CAUSE UNKNOWN**; hai quan sát HTTP/worker30s trong Test chưa đủ kết luận nguyên nhân, không tăng timeout.

Preview **http://127.0.0.1:5258/**, `/admin/login`, `/video`; `scripts/run-dev.ps1 -Phase2D -NoBuild -Port5258`. Preview5238 cũ cònhealth200. UploadDev khóa/quyền mới chưa cấp. TestWeb5259 dùng `run-video-test.ps1`; E2E5249 tự dừng ownedweb. Worker/Test đầy đủ lệnh trongrunbook; full `scripts/test-phase2d.ps1 -Region All`.

Bước tiếp: tiếp bàn giao D15 và đối soát raw gốc nếu có backup thật; không dựng lại2D. Production engine/PII policy, license/isolation, SMTP/MFA/HTTPS/restore/UAT/capacity/quyền vận hành còn riêng. Không tự làm2E/2F/Phase3;37/53 vẫn là mẫu và2video nguồn thiếu tệp giữ nguyên. Không.env/grantDev/Git/deploy/email/service/AV policy changes.

| ID | Điều kiện nghiệm thu2D | Bằng chứng / trạng thái |
|---|---|---|
| D01 | Baseline/migration | PASS: Test trước/Dev sau, backup/VERIFYONLY;0khác biệt17nhóm |
| D02 | Upload/quota | PASS: byte thật/SQL/hash;MIME/path/size/actualchunk413; chưa1GiB/IIS load |
| D03 | Resume | PASS: ngắt mạng, restart web/browser, chọn lại và đối chiếu manifest |
| D04 | Quyền/race/CSRF | PASS: owner/fresh rights, chunk/complete idempotent, foreign/CSRF bị chặn |
| D05 | Scan thật | PASS: Defender đúng SHA của video/VTT vô danh; ProductionPII BLOCKED |
| D06 | Infected/mismatch | PASS: EICAR thật Infected, pending/error/mismatch không Ready |
| D07 | Native processing | PASS: probe/MP4/poster/hash;VFR/H264444/remux/noaudio |
| D08 | Worker bền vững | PASS: lease/retry/race/kill/restart/cancel/timeout/disk/missingfile/cache |
| D09 | Revision/approval/scheduler | PASS: đúng revision, draft/caption/track không đổi public |
| D10 | HTTP media | PASS: GET/HEAD200/Range206/416/IfRange/ETag/seek;draft/private denied |
| D11 | Shared/revoke | PASS:2bài dùng chung, gỡ1còn phục vụ, gỡ cuối404 |
| D12 | UI/E2E | PASS:12video page/viewport checks +8actions,390/1440; chưa physicaliOS/WCAG |
| D13 | Metadata nguồn | PASS:2metadata/0tệp, không gắn fixture vào nguồn |
| D14 | Regression cuối | PASS:`video-tests-20261015T071425Z-1c567963`108/108/exit0;HTTP/UI/worker ghi riêng |
| D15 | Artifact/vận hành | **PARTIAL:** raw worker cũ bị ghi đè; đã sửa runpath/guard nhưng chưa phục hồi gốc |

<!-- PHASE2D-CURRENT-END -->

---

# Nghiệm thu delta 2C-SCAN — 15/10/2026

**2C-SCAN PASS LOCAL** với engine thật Test; **2C/Finish overall PARTIAL / CHƯA PRODUCTION READY**. [Báo cáo/T01–T10](phase-2c-scanner-report.md), [runbook](scanner-operations.md), [summary/hash](evidence/phase2c/scan-20261015T023338Z-f412ffbd/summary.json).

| Nhóm | Bằng chứng mới thực | Trạng thái / giới hạn |
|---|---|---|
| SCAN-1 engine | MicrosoftsignedMpCmdRun/Defender1.1.26090.9/signature1.461.27.0, nativeClean+EICAR, không AVpolicy change | PASS LOCAL Test, anonymous/publicSHAonly; ProductionPII BLOCKED |
| SCAN-2 pipeline/worker | SQLTest exacthash+size/audit/UTC/signature/claimrowversion/token/3retry/recovery/race; actualoffline/timeout/malformed/oversize | PASS LOCAL; negativefaultfixture không sinhClean |
| SCAN-3 serving | GenuineCleanPDF→GET/HEAD/Range/no-store/nosniff/attachment/currentACL; Infected/pending/private public404/423; tamper409→ScanFailed/1audit; sessionrevoked403 | PASS LOCAL Test; CMS2A/B regression riêng giữ |
| SCAN-4 nguồn | 53read-only copies53Clean/0Infected/0Error, perfilehash; Dev53NotScanned,0khác biệt17nhóm,37/53/70capture68SQL/8/2meta0file | PASS preservation/report; chưa Apply nguồn |
| SCAN-5 final regression | `scan-tests-20261015T030513Z-767f928e`100/100/0FAIL sau sửa cuối,245paths0drift; build0warn/error | PASS final full mới;97before2Finishfix/8after giữ lịch sử |
| HTTP/UI/worker/packages | scannerHTTP8/UI40+6actions/5viewport/0JS; real childworkers/sink; Releaseweb5/workerhealth; Dev7readonlyGET | PASS đơn vị riêng, không cộng vào100 |
| Schema/quyền/secrets | Không migration, latest20261014163306_InquiryFinish; Identity/env nguyên; không grantDev/SMTP thật | PASS preservation;10quyền vận hành/interaction chưaApply |
| Overall2C/Production/historical | Production engine/PIIpolicy/53apply/SMTP/MFA/restore/UAT/SLA-retention chưanghiệmthu; SQLUNKNOWN/V04artifactPARTIAL | Overall vẫnPARTIAL / CHƯA PRODUCTION READY |

---

Lịch sử Finish giữ nguyên dưới; SCAN-INTEGRATION BLOCKED của checkpoint cũ được thay bằng bằng chứng SCAN ở trên:

# Nghiệm thu delta 2C-Finish — 15/10/2026

**PARTIAL / CHƯA PRODUCTION READY**, [báo cáo](phase-2c-finish-report.md). Đây là delta mới; các bảng0–1/2A/2B/V04/core2C dưới là lịch sử.

| Nhóm | Bằng chứng thực | Trạng thái / tồn tại |
|---|---|---|
| F1 private/OTP/ACL/size/MIME/GC | SQL Test vàHTTP final `finish-tests-20261014T172716Z-9eab500d`, private file bound, guest/public/Range denied | PASS negative/API; **SCAN-INTEGRATION BLOCKED**, Clean/Infected realNOT RUN |
| F2 list/PII/assignment/lookup | freshrights/transfer/reply cancellation/Excel scope; list-only403PII; dedicated OTP/race/attempt/resend/session | PASS LOCAL testedcases; timing/abuse benchmark chưa chạy |
| F3 calendar/extension/Excel/batch | calendar ID frozen/temp policy, weekend/holiday/makeup/year/cutoff; concurrent extension1winner; openpyxl60rows/671text/6boundary; mixed peritem statuses/idempotency | PASS LOCAL tạm tính; policy pháp lý/roster paging/exportdue ngoài bản này |
| F4 Uncertain | TCP DATA accepted-store thenACKcut, noauto resend; receipt/auth403/unknown/not-sent/risk/idempotency/audit | PASS LOCAL sink; real SMTP/provider receipts/alerts chưa làm |
| Source/Identity/backup/migration | 0/17;37/53/70capture68SQL/8/2meta0file;53NotScanned; additiveTest→Dev,273/130backup files verified | PASS preservation local; restore drillNOT RUN; Devpermissions khônggrant |
| Regression | Full97/97 `finish-tests-20261014T171757Z-de2f722d` trước2paths sửa cuối; finalFinish8/8 `finish-tests-20261014T172716Z-9eab500d` sau2paths;0drift242final;build0warn/error | PASS các run cụ thể; không full97 sau sửa cuối/khôngcộng105/khôngdùng89cũ |
| UI/HTTP/worker/smoke | finalUI35/5actions/5viewports/0JS;HTTP6;childworkers/sink;Releaseweb7/workerboot;Dev8GET | Separate evidence; khôngcộng vào sốgroup runner |
| V04 artifact / SQL timeout / Production | rawsmoke2B lost unchanged;boundedTest probe không tái hiện | V04 historicalPARTIAL;SQL MONITORING/ROOTCAUSEUNKNOWN; CHƯA PRODUCTION READY |


---

Lịch sử checkpoint trước Finish (giữ nguyên):

# Ma trận nghiệm thu Giai đoạn 0–1

Ngày: 12/10/2026. Chỉ `PASS` sau khi test/bằng chứng thực đã tồn tại. `NOT RUN` là chưa chạy hoặc phần ngoài giai đoạn; không được tính là nghiệm thu production.

| Yêu cầu | Triển khai / bằng chứng dự kiến | Test / bằng chứng thực | Trạng thái | Còn thiếu |
|---|---|---|---|---|
| SDK ổn định, pin phiên bản và package lock | global.json, Directory.Packages.props | install-sdk SHA512 PASS; preflight SDK10.0.112/runtime10.0.12; scripts/test.ps1 locked restore/full build PASS0 warning/error; NuGet audit không báo vulnerable | PASS | Quill1 advisory low được ghi riêng; license transitive inventory trước production |
| SQL Server thật, database dự án riêng | LandPortal_Dev, LandPortal_Test | setup-db tạo đúng 2 DB; preflight Express16.0.1000.6; sql-tests.json migration rerun/application-role PASS | PASS | Hạ tầng deployment chưa chốt |
| Inventory đủ hạng mục, pagination có tập ID khác | source-inventory.md, feature-parity.csv, snapshots | Inventory giữ 14 chuyên mục và các mục/banner bổ sung; snapshot/category75 trang2 tập ID khác; legal JSON/Page2 được kiểm chứng | PASS | Cơ cấu tổ chức href=#; nhiều URL nguồn404/ngoại lệ vẫn giữ, chưa tuyên bố tổng nguồn |
| 30–50 nội dung thật nhiều loại, không gọi là toàn nguồn | MigrationCli, snapshots/manifest | verify-report.json: 37 nguồn thật Verified; legal3/draft2 cùng bài/thông báo/giới thiệu/video metadata | PASS | Video chỉ metadata/poster, chưa chuyển file video; full corpus chưa biết |
| Tài nguyên nội dung/ảnh/PDF local, hash đúng | MediaAssets và kho tệp .local | verify-report.json: 55 tệp tải, 70 snapshot, 37 nội dung đối chiếu text NFC/hashes, Errors=[]; SQL53 media; UI source-block và PDF200 đã chạy | PASS | 2 tài nguyên nguồn chưa chuyển được; scanner production chưa có |
| Chạy nhập lại không trùng và không ghi đè editor | SQL unique + revisions | import-rerun-report.json: Imported0/Unchanged37/Failed0; counts Dev giữ nguyên; SQL test comparison revision giữ editor/public | PASS | Lịch/full-source sync giai đoạn 3 |
| Rowversion và transaction bảo vệ sửa nội dung | Contents/Revisions | sql-tests.json: 2 context stale rowversion bị từ chối; workflow stale-save không tạo revision; HTTP stale POST409; transaction rollback giữ revision/count | PASS | Kiểm duyệt/lịch đầy đủ giai đoạn 2 |
| Public trang chủ/chuyên mục/bài/tra cứu/search | MVC controllers/views | tests-summary.json23/23: real-source SQL→HTTP; source canonical alias301 đúng bài; draft search không lộ, published search tìm được | PASS | Tìm kiếm đầy đủ/bộ lọc ngày/sitemap/RSS giai đoạn sau |
| Admin Identity, lưu nháp/gửi duyệt/đăng theo quyền | Admin controller + policy | tests-summary.json23/23: SQL draft/submit/publish; HTTP đăng nhập Identity/CSRF/editor publish denial/publisher với bài nguồn thật | PASS | Trả duyệt/lịch/gỡ/khôi phục/preview/quản trị loại khác chưa có |
| Bản nháp không thay revision công khai | PublishedRevisionId | SQL và HTTP E2E trên bài nguồn thật: nháp không thay public/search; publish hiển thị đúng revision; audit action | PASS | CMS đầy đủ giai đoạn 2 |
| Search Vietnamese đ/d và Unicode tổ hợp | Search normalization + SQL parameterized | tests-summary.json: Unicode tổ hợp/có-không dấu SQL PASS, title giữ Unicode; HTTP published/draft search PASS | PASS | Full-text/benchmark lớn chưa thực hiện |
| HTML sanitize, chặn script/hotlink trái phép | Sanitizer/crawler/media route | sql-tests.json script/event/javascript/external-img removal và table/local-img retention; SSRF allowlist/private IPv4/IPv6; draft media và raw store HTTP404 PASS; UI chặn host nguồn vẫn hoạt động | PASS | Chưa nghiệm thu an ninh production |
| Desktop/mobile 360/390/768/1024/1440 | CSS/menu/views | ui-report.json30/30 PASS: 25 trang giữ bằng chứng, 5 editor kiểm lại sau sửa summary; menu14/skiplink/bảng mobile cuộn và focus tải/PDF200; không alert rỗng, POST invalid hiển thị lỗi thật | PASS | Edge desktop mô phỏng viewport; chưa thiết bị iOS/Safari hoặc chứng nhận WCAG2.2AA |
| Publish package local | scripts/publish.ps1 | scripts/publish.ps1 đã chạy PASS Release tại .local/publish/web, không deploy | PASS | Chưa nghiệm thu triển khai máy chủ |
| Restart web/publish-runtime và kho private | run-dev.ps1, publish-smoke.json | WebTest5189 start/stop; gói Release được chạy thật5190; root kiểm84 file/85.993.909byte không .env/media/snapshot/keys/node_modules | PASS | Worker chức năng đầy đủ và backup/restore giai đoạn 2–4 |
| Cutover cấm crawler kể cả restart | SQL SiteSetting + config | sql-tests.json: persisted CutoverUtc chặn context SQL mới dù config enabled, khôi phục setting test sau chạy | PASS | Cutover Dev không chạy trong lượt này; vận hành worker đầy đủ giai đoạn 2 |
| Upload video mới/chunk/resume/worker/seek | Giai đoạn 2 | Chưa triển khai trong lượt 0–1 | NOT RUN | Container/codec/scanner/FFmpeg/Range |
| Comment/câu hỏi/liên hệ/góp ý pending/PII/spam | Giai đoạn 2 | Chưa triển khai trong lượt 0–1 | NOT RUN | Tiếp nhận/kiểm duyệt/quyền/hạn/mail sink |
| CMS/worker đầy đủ: lịch/gỡ/khôi phục/audit/menu | Giai đoạn 2 | Chưa triển khai trong lượt 0–1 | NOT RUN | Kiểm thử từng nghiệp vụ |
| Full migration và alias toàn nguồn | Giai đoạn 3 | Chưa chạy full source | NOT RUN | Độ bao phủ có tổng đối chứng |
| MFA/scan/backup-restore/UAT/accessibility/performance | Giai đoạn 4 | Chưa thực hiện | NOT RUN | Blocker trước production |

## Giai đoạn 2A — bằng chứng mới, 12/10/2026

Các dòng Giai đoạn 0–1 ở trên là lịch sử nghiệm thu lúc bàn giao. Baseline lượt 2A đã thực chạy 23/23 trên Test từ binary trước thay đổi; không tự kế thừa PASS cũ. **Giai đoạn 2A PASS ở local; CHƯA PRODUCTION READY**. Bằng chứng chi tiết và các lần focused run ở `phase-2a-report.md`.

| Nhóm | Yêu cầu / triển khai | Bằng chứng thực | Trạng thái | Còn thiếu |
|---|---|---|---|---|
| A | Backup/manifest trước migration; bảo toàn source/public payload/mapping/files | source-preservation.ps1: before/after Test và Dev 0 mismatches/17 nhóm; actual native COPY_ONLY CHECKSUM + RESTORE VERIFYONLY cho Dev/Test; backup gồm SQL/media/snapshot/keys/config | PASS | Chưa thực hiện restore, VERIFYONLY không phải restore |
| A | Giữ đúng tập nguồn 37/53/70 và 8 ngoại lệ | Dev 37 contents/source,47 revision,74alias;53media41ảnh12PDF hash nguyên;70manifest snapshot,68SQL URL+hash unique;8 ngoại lệ riêng;2metadata video,0filevideo | PASS | Toàn bộ dữ liệu/ngoại lệ thuộc Giai đoạn 3 |
| B | SQL save/submit/approve/exact publish/direct permissions | Typed SQL workflow và HTTP 3 cấu hình quyền SQL Test; UI writer→reviewer→public thực PASS | PASS | Lịch worker/quản trị loại khác ở 2B |
| C | Draft metadata/body/category/cover tách bản đang công khai | SQL toàn metadata; HTTP positive home/category/search card + canonical body/title/summary/images giữ bản cũ khi pending; chỉ approved publish đổi bản. SourceDate NULL mới đăng có mặt ở home | PASS | Public cache dùng no-store local, chưa benchmark production |
| D | Quyền resource/actions, live DB permissions, CSRF/mass assignment | SQL owner/action denial; HTTP forged direct requests/foreign draft/CSRF/mass assignment/live DB role revocation PASS | PASS | Quản trị tài khoản/nhóm quyền UI ở 2B, MFA trước production |
| E | Concurrency/autosave/approval invalidation/idempotency | SQL hai session stale-save; working autosave không tăng history; delayed autosave submit/publish; old approval; sequential và concurrent duplicate publish command one audit; HTTP stale409 PASS | PASS | Không hardcode số PASS; double/parallel probes chỉ Test |
| F | Media validation/scan truthfulness/private access/shared references | Media 9 riskcases JPEG/PDF/PNG thực PASS; private upload/preview và missing-original publish block PASS. Focused HTTP ảnh cũ public404/historical200; bỏ PDF khỏi panel không khôi phục qua hidden metadata; sharedPDF200, hết last live reference404. Typed SQL prune stale Inline/Cover/Thumbnail, giữ selectedPDF, chặn forgedimageAttachment và giữ immutable history PASS | PASS | **Scanner production chưa có**, Development exception tường minh giữ NotScanned |
| G | History/unpublish/trash/restore keeps history and does not republish | SQL restore revision creates draft/clone no unique identity/trash restore không republish; HTTP404/no-store sau gỡ; focused SQL audit gỡ/trash đúng old published ID khác current draft; history localized/private links PASS | PASS | Không hard-delete hoặc hứa thu hồi bản người đọc đã tải |
| H | Offline importer rerun/preserve editorial and metadata-only distinction | Parser37/fixtures70; rerun zero duplicate; source edited comparison; same-hash category/cover changes preserved and rerun idempotent; historical missing-category compatibility vs new/blank rejection PASS | PASS | Không phải toàn nguồn;8ngoại lệ chưa đóng;2video metadata/0filevideo |
| I | SQL-backed UI/E2E editor/list/review/preview 390/1440 | **40/40 page checks PASS**, 10 CMS screens +30 old-breakpoint smoke; Word paste event/table grid keyboard/media picker/caption/PDF/submit/approve/publish actual; root+QA visual review PNG PASS | PASS | Edge viewport emulation, không chứng nhận WCAG/iOS/Safari |
| J | Chặn host nguồn trong profile test, local media/PDF hoạt động | Browser abort tất cả off-origin requests;0 external attempts/0 pageerrors; PDF200/%PDF-/2.764.932byte actual; original local images decode PASS | PASS | Link ngoài giữ có chủ ý; nguồn còn8ngoại lệ |
| Ops | Full locked build/test/publish local và lệnh chạy | Full suite35/35 riêng; focused SQL2/2; final SQL/media37PASS, HTTPNOTRUN; final HTTP9checkpoints, BrowserNOTRUN; UI40/40 riêng trước DTOfix; build0warn/error; backup130hash/envunchanged; NuGet7project audit; final Release smoke11/11 sau media fix | PASS | Restore chưa chạy, scanner/MFA/hạ tầng/UAT/performance và 2B–4 chưa nghiệm thu |

## Giai đoạn 2B — 13/10/2026

**2B.1–2B.5 PASS LOCAL, CHƯA PRODUCTION READY.** Baseline thực38/38 + UI40 riêng; full cuối run`final-20261013-042438`73/73, exit0, build0warn/error. 173 path/SHA không đổi; canonical code SHA`40064a776dca01277b4a78687b42974823cdc565365e8b32b7d4e25befd63cc8`. [Báo cáo](phase-2b-report.md)/[summary](evidence/phase2b-tests-summary.json) giữ command/time/failures/giới hạn. Các đơn vị SQL/HTTP/UI/package không cộng thành suite giả.

| Nhóm2B | Triển khai và bằng chứng thực | Trạng thái local | Giới hạn |
|---|---|---|---|
| A — baseline/bảo toàn | Migration2A giữ nguyên; backup native/file Dev130/Test173; Test migration trước Dev; cuối Dev37/53/70capture68SQL/8ngoại lệ/2metadata0video,0mismatch17nhóm; Identity/env không đổi | PASS LOCAL | VERIFYONLY không phải restore; không toàn nguồn |
| B — template/revision | 4 loại typed metadata, nhiều PDF có nhãn; SQL6, HTTP4loại, UI14/7thao tác; approval exact snapshot | PASS LOCAL | Hiệu lực thiếu căn cứ giữ chưa xác định; nguồn unmapped nguyên |
| C — draft/workflow | Save/submit/approve/publish/gỡ/restore2A trên các loại; metadata/card/title/cover/categories không đổi trước publish; stale approval/autosave/concurrency blocked | PASS LOCAL | Không hard-delete/history sửa |
| D — endpoint/phiên | Accounts SQL10/HTTP5/UI18; currentSQLpermissions, CSRF/DTO/owner, disabled/tempMustChange/stamp/sharedrole revocation | PASS LOCAL | MFA production chưa làm |
| E — delegation/last-admin | Catalog allowlist, không sửa nhóm mình để nâng quyền, privileged grant riêng; 2 concurrent reciprocal locks/role reductions giữ adminpath bằngSQLtransaction/applock | PASS LOCAL | Quyền vận hành chưa cấp |
| F — cấu hình | Site SQL7/HTTP4/UI34; immutable draft/live/history/restore, rowversion, exact atomic apply, source14 protected, cycles/slugs/internal/external targets | PASS LOCAL | Source tổ chức/directive unresolved chưa tự bịa adapter |
| G — media/privacy | Draft banner/logo/upload/privatepreview404cho khách, livegrant/sharedreference/expiry/withdraw; source/ảnh/PDF giữ bytes; snippets/count/publicprojection chỉ published | PASS LOCAL | Chưa scanner, không hứa thu hồi bytes đã tải |
| H — search/lookup | Có/không dấu/đ-d/tổ hợp/slash/literalwildcards/combofilters; pages23fixtures10/10/3 SQL OFFSET/countunique/tieID; withdraw/private result blocked | PASS LOCAL | HTML+metadata, khôngPDF/OCR; substring/FullText0, chưa tải đại diện |
| I — hẹn giờ | Exactrevision/hash, approved/direct quyền đúng, UTC+7/UTC, trước giờ private; SQL durable độc lập browser/web | PASS LOCAL | Local catchup15min không phải SLA |
| J — lịch/race/quyền | Generation/leasefence sửa/hủy/đổi giờ/reject/gỡ/manualpublish; fresh requester/reviewer active permissions; NeedsReview không bypass | PASS LOCAL | Operator phải xử lý NeedsReview qua workflow |
| K — restart/idempotency | Scheduler SQL/process9; two claims/expiry/recovery/commit reconciliation; actual SQLtimeout retry bounded; hai childworker1effect/audit | PASS LOCAL | Không tuyên bố distributed exactly-once toàn hệ |
| L — cross-process/cache | Web đã đọc trước worker thấy revision/detail/search mới; web tắt worker vẫn đăng; requestSQL/no-store, một configrevision/request; banner expiry mỗi request | PASS LOCAL | Chưa benchmark/cache production |
| M — scanner/env | NotScanned nguyên khiexplicitDevloopback; Production/Staging/source/publication policy block; worker Stagingbypass startup denied thực | PASS LOCAL | Scanner thật BLOCKER production |
| N — import/regression | Savedsource snapshots/parser/offline rerun/metadata-only/category/cover chốngtrùng, giữ editor; 2Aworkflow/media/sourcehost-abort hồi quy trongrun73 | PASS LOCAL | Không crawl/reimportDev; ngoại lệ8giữ mở |
| O — UI/package | CMS40/typed14/accounts18/site34/scheduler10 report riêng; keyboard/form/table/picker/labels3901440+smoke3607681024; Devreadonly15; web121files11URL+5privacy; worker81filesboot0; owned stopped | PASS LOCAL | Edge viewport, chưaWCAG/iOS/Safari/UAT/IIS |
| Ops quyền mới | Dev identity/stamps/password/roleclaims SHA nguyên; granttoolTestdry-run không đổi; thiếu10effective permissions ghi rõ trongreport | CHƯA CẤP | Không tự ApplyDev/resetpassword |
| Sau2B | Bình luận dưới bài/câu hỏi/liên hệ/góp ý kiểm duyệt2C; video uploadmáy2D; fullautomigration3; scanner/MFA/restore/UAT/cutover | NOT RUN | Không tự đánh dấu hoàn thành hoặc PRODUCTION READY |

## Mốc V04 — 14/10/2026, sau2B

Giữ lịch sử2B PASS LOCAL. V04 tổng thể **PARTIAL về hồ sơ artifact**, không do giả lập dữ liệu/test; runtime hiện tại83/83PASS LOCAL. [Báo cáo](phase-v04-after-2b-report.md)/[audit32CH](srs-v04-gap.md)/[UI](ui-review-v04.md)/[SQL risk](sql-category-timeout-observation.md). CHƯA PRODUCTION READY.

| Mốc/vùng | Bằng chứng mới | Trạng thái | Giới hạn/checkpoint |
|---|---|---|---|
| V04-A | Đọc đầy đủ v0.4/prompt;32CH có status/code/test/backlog;baseline verified173paths/36parity/14categories/0mismatch17 | PASS audit | Không đóng32CH hoặc làm lại2B;Q04/Q09/Q12 theo gap |
| V04-B |4trang dùng SQL nguồn;before/after20pagechecks×5width;keyboard/14đích200/3actions,0JS/offorigin/mutation;ảnh đã xem | PASS LOCAL | CHỜ DUYỆT THIẾT KẾ;chưa thiết bị/WCAG/CWV/UAT |
| V04-C runtime | Cuối10:19–10:23 VN14/10 `test.ps1 -IsolatedBuild`83/83 exit0 build0warn/error;UI/HTTP/worker separate;publish11URL/5privacy;actualPkcs10.0.13;NuGetaudit7projects no advisory | PASS LOCAL | Baseline71/73 vàfail82/83 giữ, không cộng các lượt;83gồmwrapper |
| V04-C artifact | Summary73/report/code/final-run2B giữ;rawsmoke2B bị outputV04 ghi đè, đã tách tênV04 vàghi note | PARTIAL | Raw gốc chưa phục hồi;không giả tạo. Không gọi mọi artifact cũ bảo toàn |
| V04-D | Testtimeout-2 tái hiện;158wait samples/memory grant;estimated sort46328→2704KB,SQLpagingIDs/publicpointerrecheck,regression83pass | PASS LOCAL điều tra | MONITORING/ROOTCAUSEUNKNOWN,không tăng timeout hoặcclaimFIXED |
| Scope/phiên | R/H:all primary+secondary;denyclone/replay/privateMedia;SQLgrantself/expansion/stamp;requester+reviewerfresh;idle31ticket401/GETnoRenew/browserreauthsaveSQL | PASS LOCAL | LegacyDevclaims nguyên;không tự cấp scope/quyền;tab đóng trước lưu có thể mất prose |
| Legal/SEO |4nullablecolumns Test trước/Dev sau backup;rule/evidence UTC+7/hash/publicisolation;exactnumber/newest;canonicalID/privatepreviewOGabsent/withdraw404 | PASS LOCAL | Unknown giữ;0expiryDev mới;không pháp lý/FTSPDF/sitemap/UAT |
| Dữ liệu/migration | Dev37/53(41ảnh12PDF)/70capture68SQL/8/2meta0video/47rev74alias;0mismatch17,identity/env nguyên;backupDev130/Test219VERIFY/hash | PASS bảo toàn nguồn | Không toàn nguồn;VERIFYONLY không RESTORE |
| SauV04/quyền |10Devpermissions chưaApply;2C/2D/2E/2F/Phase3/4 NOTRUN;PKCsapp-localpatchkhôngvátoànSDK/infra | CHƯA CẤP / NOT RUN | Không tựDeploy/DNS/Git/mail/service/crawl;2Cphảikiểmduyệt,2Duploadmáy |

## Giai đoạn 2C — 14/10/2026

**2C PARTIAL; CHƯA PRODUCTION READY.** [Báo cáo](phase-2c-report.md), [summary run mới](evidence/phase2c/20261014T042052Z-fb5949aa/summary.json). Final run`tests-20261014T045727Z-8e402a5c`:89/89groups,0failure,exit0 sau sửa cuối;7HTTP/40UI/worker là đơn vị riêng.83V04 cũ không dùng làm PASS2C.

| Mã/vùng2C | Bằng chứng mới thực | Trạng thái | Chưa hoàn tất |
|---|---|---|---|
|A nguồn/migration|Test backup254/Dev130/native CHECKSUM VERIFYONLY/hash;5bảng migration20261014043339 Test→Dev;17nhóm0mismatch;37/53/70capture68SQL/8/2meta0video;Identity/env nguyên|PASS bảo toàn|ChưaRESTORE/fullmigration; nguồn53NotScanned nguyên|
|2C.1 OTP/outbox|HMAC/random6digit/expiry5min/5attempts/resend60s/5emailhour;bindingpayload+purpose+session+target+revision;wrong/tamper/expiry/resend/retry/noSMTP;TCPsinkthật|PASS core / PARTIAL mốc|SMTPthật/retention/opsUncertain/alert;HTTPthrottleexhaust/race đồngthờichưa đủ|
|2C.2 bình luận|Article+site/bài toggles;form→childworkerOTP→SQLPending→scope inbox→snapshotduyệt→publicencoded→hide;agencylabel;revokedpermission403/stale409/CSRF|PASS vertical slice / PARTIAL mốc|Batch/find/filters/history-reviewUI;receptionendpointnegative/race và simultaneouscommand cònthiếu|
|2C.3 hỏi đáp|Form/OTP/receiptSQL/private/inbox/answerprotected;TCPfail giữAnswered→retryrealsink;consent+reviewedquestionanswerpublicsnapshot|PARTIAL|Attachment10MB/scanner,assignment,OTPlookup,SLA/calendar/extension,Excel/export,publicsearch/cache,pagingUI|
|2C.3 liên hệ|Form riêng/OTP/SQL/inbox/process;khôngcopyhỏiđáp;publishdeny;privateguestblocked|PASS core / PARTIAL vùng admin|Find/export/assignmentnếuápdụng/ops/securitycomprehensive|
|2C.4 góp ý|Form/OTP/SQLpublishedrevision/deadline;VNenddayexclusive;draftdeadlinekhôngđổi;boundary/gỡdeny;khôngpublic|PASS core / PARTIAL mốc|Concurrentraceclose/withdraw,formdraftlabel/window,filterskỳ/draft|
|2C.5 quyền/riêng tư|FreshSQLread/action/categorychính+mọiphụ,privateoriginalDP,publicsnapshotimmutable+runtimeDENY,CSRF/DTO/rowversion/idempotency,0Devfixtures/grants|PASS core / PARTIAL mốc|Revocationmailworkernegative/scope endpoints sâu;file/range/Excelmodule chưa có;privacy policy/MFA/scanner|
|Hworker|Actual4childmailworker/sink;claimrace/backoff/refusedTCP/ACKlostUncertain/expiredleaserestart;replybusinesssurvives|PASS cases chạy|Operatorreconciliation/requeue/alerts/Smtp thật;khôngexactly-onceSMTPclaim|
|IUI|New40checks8routes×5width/keyboard1/0JS/offorigin;finalảnhmobileform/consultation/inbox+desktopdetailđãxem;oldCMS40/typed14/account18/site34/scheduler10 riêng|PASS LOCAL kiểm tra đã chạy|Edgeheadless;chưaSafari/iOS/WCAG/UAT/fulladminUX|
|Jregression/package|Final89/89 lockedbuild0warn/error/audit7noadvisory;Releaseweb6routes200/workerbootTestfilter0effects;Dev7serialGET200|PASS run đã chạy|Khôngproductionperformance;bootstrap-smoke không đủtoànworker;livepreviewlogkhôngfrozen|
|Risk/history|SQL timeout MONITORING/ROOTCAUSEUNKNOWN;V04rawsmoke2BmissingPARTIALkhônggiảtái tạo;raw2Cuniquerun+manifesthash|Giữ risk|Không đóngincident/gọiFIXED;10Devquyềncũ+newinteractionchưa cấp|

Checkpoint tiếp:2C.3 hỏi đáp nâng cao rồi2C.2/2C.4/2C.5/ops cònthiếu,khôngdựnglại.2Duploadvideomáy,2E/2F/Phase3/4NOTRUN.
