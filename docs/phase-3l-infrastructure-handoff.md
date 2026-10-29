# Phase3L — hồ sơ bàn giao môi trường Bộ, chưa triển khai

Kế thừa [16caseUAT](phase-3k-uat-plan.md); chỉ có kiểm chứng kỹ thuật local trong3L, **chưa có biên bản UAT cán bộ**. Không cấp tài khoản/quyền, cài IIS/service, deploy, đổiDNS hoặc gửi email thật.

| Đầu việc | Cần đơn vị vận hành/ATTT cung cấp hoặc duyệt | Hiện trạng |
|---|---|---|
| IIS/Web | Server/OS/architecture, HostingBundle.NET10, site/app pool/serviceidentity, HTTPbackendloopback, lịch triển khai | BLOCKED target; local chưa cóIIS, không tự cài |
| SQL Server | Instance/database đích riêng, version/edition/capacity, DBA và quyền leastprivilege/migration có backup | BLOCKED target; không copySQLTest chứafixture |
| Media/keys | Roots ngoàiwebroot cho media/snapshot/temp/quarantine/DataProtectionkeys, ACL read/write theoWeb/Worker, quota/retention/offsite | NEEDS_TARGET_REVIEW; khoDev/Test không là khoProduction |
| Web/Worker profile | Delta phần mềm giữ target/host/SQL/worker/scanner/media ACL fail-closed choStaging/Production | BLOCKED softwareprofile; hiện cònlocalDevelopment/Testfences, không giảDevelopment đểdeploy |
| HTTPS/routing | Hostnamegdla.gov.vn, cert/renewal/alert, proxytrustedhops,301ownership từQLDD và mappingNukeViet | NEEDS_APPROVAL / NOTRUNtarget; không thayDNS |
| Scanner | Engine/provider được duyệt, serviceaccount, policyPII/cloudsubmission, quarantine/retention, proofexactbyte+Clean/EICAR/failure | BLOCKEDProduction;18CleanTest143 không cấpquyềnProduction,53originalDevNotScannedgiữnguyên |
| SMTP | Relayhost/port/TLS/from, allowlist/limits, secretstore và quyềntruycập dođơnvịcấp | BLOCKEDsecret/target; chỉsinkTest, khônggửithưthật |
| MFA | Phương thức ATTT duyệt, enrol/challenge/recovery/revoke/audit, hỗtrợ mấtthiếtbị | BLOCKEDsoftware;Identitytokenproviders/CAPTCHAkhôngthayMFA |
| Restore/rollback | BackupSQL+files/keys/config nhấtquán, RPO/RTO, offsite/encryption và người quyếtđịnhrollback | PASSLOCALactualbackup250restore;targetRPO/RTO/restoreNOTRUN |
| UAT | Cánbộ3vai trò, tài khoản đượcduyệt, lịch/môi trường/data/UATevidence và chữkýnghiệmthu | PREPARED16cases;businessUATNOTRUN,3Wordthật/privateLookupauthoritycònthiếu |

## Yêu cầu kỹ thuật NukeViet — bản nháp để chủ dự án chuyển đơn vị vận hành

Đề nghị kiểm tra quyền truy cập công khaigdla.gov.vn từ mạng được phép: DNS45.120.229.41 và dịch vụHTTP80/HTTPS443. Checkpoint3K ghiTCPrefused10061, TLSchưa bắt tay;3L không có thay đổi mạng nên **không retry kết nối**. Đề nghị thông báo endpoint/hostname/giao thức chính thức được hỗ trợ, maintenance window hoặc cấu hình access do đơn vị quản lý. Không yêu cầu tắtTLS, mởfirewall toànmáy hoặc đi vòng qua mạng chưa được phép.

Nếu mạng chưa sẵn sàng, bàn giao export module công khai +uploads/catalog/URL mapping theo [hướng dẫn export](phase-3k-nukeviet-export-handoff.md), kèm version/encoding/timezone/count/SHA manifest/người bàn giao. Loại accounts/password/session/private drafts/PII. Chưa có export/catalog thật:0NukeViet content được chấp nhận, mappingchờnghiệpvụ;giữadapter. Tài liệu này chưa được gửi email và không là approval đã ký.
