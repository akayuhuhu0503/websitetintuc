# SQL timeout — theo dõi14/10/2026

**REPRODUCED trên Test; giảm độ rộng truy vấn; MONITORING / ROOT CAUSE UNKNOWN.** Không tuyên bố FIXED vĩnh viễn. Không thay global command timeout, cấu hình SQL instance, database khác hoặc thêm index không có bằng chứng.

Baseline2B cuối13/10 từng không tái hiện. Baseline hôm nay02:05–02:09 UTC thực71/73, fail2 wrapper HTTP/UI; lỗi SqlException **-2** tại `PublicSiteService.HomeAsync`, thời gian khoảng30s. Lượt sau sửa delta đầu02:50–02:55 UTC82/83, fail CMS UI tại `/` Test5189. Log `.local/evidence/v04/regression-failed-memory.log` và HTTP web log ghi block `992921e8-a7d5-47dc-924b-2e5e8efcaa96`, correlation `0HNP9JO3QBRV9:00000003`, thời gian30059,5ms; Featured gây error500, không dữ liệu fallback giả.

Monitor có giới hạn6 phút, chỉ requests DB_ID(LandPortal_Test) và text chứa Contents; lưu **hash**, không statement/credentials. `.local/evidence/v04/sql-waits-final.json` có158 mẫu:151 `RESOURCE_SEMAPHORE`,7 `LCK_M_S`. Các mẫu semaphore có blocking_session_id0, granted_query_memory0, CPU0–1ms, query hash/plan hash thực. Có các mẫu chờ lên tới gần30s. Chờ khóa ngắn còn bao gồm kiểm thử timeout có chủ ý của scheduler; không dùng chúng để kết luận lỗi public do khóa. Monitor đầu tiên0 mẫu không phải bằng chứng không có lỗi.

Đã xác minh cơ chế chờ memory grant trong các request được lấy mẫu. **Chưa xác định nguồn áp lực bộ nhớ toàn instance** hoặc nguyên nhân gốc cho mọi timeout chuyên mục trước đây; không khảo sát CSDL khác. Cached-plan probe không trả plan matching; không bịa actual execution plan.

SHOWPLAN_XML ước lượng trên Test, cùng bộ dữ liệu và truy vấn category75, không thực thi DDL/DML: query cũ chọn/sort card rộng có `SerialDesiredMemory=46328KB`, sort row size9966byte; query page IDs có2704KB và52byte. File SQL/XML và `estimated-plan-summary.json` trong `.local/evidence/v04`. Đây là **estimated plan**, không phải actual grant/benchmark production.

`PublicContentQuery.PageRevisionIdsAsync/CardsPageAsync` nay lọc/sort/page IDs trong SQL trước, rồi lấy tối đa100 revision đang công khai theo chính IDs. Thứ tự/tieID giữ; public pointer kiểm tra lại ở bước lấy dữ liệu, revision gỡ/thay trong khoảng hai lần đọc bị bỏ, không trả cached/draft. Áp dụng home/category/search/related/fallback home và document lookup; document fetch không mang SearchText/Summary rộng. R23fixtures chứng minh pages10/10/3 cùng thứ tự/không trùng. Count cũng ở SQL.

Probe3 lần serial trước full cuối: query category cũ59,5/12,7/11,9ms; page card mới60,2/11,5/10,4ms. Homepage24blocks986,8/596,9/931,1ms,0 blockerror. Không đủ để tuyên bố tốc độ/CWV cải thiện, tải đại diện hoặc lỗi hết vĩnh viễn. Regression cuối83/83, public UI/smoke không còn timeout lỗi ngoài kỳ vọng trong lần này.

Tái kiểm tra có giới hạn, không crawl hoặc đổi timeout:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/v04-probe.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/v04-monitor-final.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -IsolatedBuild -Region HttpNoUi -SkipAudit
```

Trigger: SqlException-2/public500 hoặc block-error/correlation, chờ semaphore/lock kéo dài hay category/home p95 vượt ngân sách được PO chốt. Lấy plan/grant/wait trong đúng Test tại thời điểm tái hiện, kiểm tra paging/ước lượng/khoảng query, pool/transaction/worker. Chỉ điều chỉnh index/plan sau chứng cứ; benchmark và regression lại. Production giám sát/tải/RPO-RTO ở Phase4 chưa nghiệm thu.

## Theo dõi 2C — 14/10/2026

Giữ **MONITORING / ROOT CAUSE UNKNOWN**. Regression cuối2C `tests-20261014T045727Z-8e402a5c`89/89 thực trên SQL Test; không lỗi public timeout ngoài test scheduler có chủ ý. Dev chỉ7GETtuần tự (home/category/sourcearticle/documents/contact/inquiry/login),200, số thời gian ở task`dev-readonly-http.json`; không stress, không tăng command timeout/refactor category/khảo sátDB khác. Đây không chứng minh rootcause hay tải/CWV. Corequeries interaction mới cần profile trên corpus đại diện sau khi hoàn thiện inbox/search/attachments; telemetry không chứa PII. Source17nhóm/Identity/env nguyên sau additive migration2C.


## Theo dõi 2C-Finish — 15/10/2026

Giữ **MONITORING / ROOT CAUSE UNKNOWN**. Probe SQL Test serial3 category14,5–70,5ms/10rows;3bounded26,5–98,1ms/10rows;3home640–961ms/24blocks/0error. Có SQL raw/probeJSON tại task Finish/sql-probe/v04. Không tái hiện trong mẫu hạn chế này không chứng minh rootcause đã hết, không đại diện tải/CWV. Command timeout không tăng, không stressDev/DB khác. Dev8GET200 readonly ở smokeverified, nguồn17groups0drift; full97 và focused8 riêng, scheduler timeout intentional không là sự cố public mới. V04 rawsmoke2B lost vẫnPARTIAL, không tái tạo.

## Theo dõi 2C-SCAN — 15/10/2026

Giữ **MONITORING / ROOT CAUSE UNKNOWN**. Finalfull SQLTest100/100 `scan-tests-20261015T030513Z-767f928e`; scheduler intentional timeout là ca riêng. Dev chỉ7GETtuần tự200readonly, thời gian từngroute trong taskSCAN`dev-readonly-http.json`; không stress hoặc query plan/cấu hìnhDB khác, không tăng command timeout. Boundedscanadapter timeout không thay SQL timeout. Không tái hiện trong smoke giới hạn không chứng minh đã hết nguyên nhân hoặc tải đại diện. V04 rawsmoke2B historicalPARTIAL vẫn nguyên.

## Theo dõi 2D — 15/10/2026

Giữ **MONITORING / ROOT CAUSE UNKNOWN** cho sự cố chuyên mục V04. Full đầu `video-tests-20261015T050021Z-e34542e9`104/108 có một **HTTP timeout30s ở `/admin?q=<Test-random>`** trong V04 scope check: edit/preview/history/media trước đó403/404 nhanh, request danh sách admin chưa kết thúc khi client hủy. Đây là quan sát trên SQLTest, chưa đủ chứng cứ đồng nhất với nguyên nhân timeout chuyên mục cũ, không phải phép thử scheduler timeout có chủ ý.

Probe read-only đúng actor/query Test sau run ở task`video-20261015T035659Z-8b55ff16/v04-diagnostic-20261015T050802Z-ce1a9738/commands.json`: controller Index ra ViewResult khoảng0,4s,9SQLcommand2–48ms, command timeout vẫn30s. Không tái hiện ở probe đơn; không sửa query, index, plan hoặc timeout vì chưa có căn cứ. Focused `video-tests-20261015T050826Z-9375fa09`V04**10/10** với6HTTPcheckpoint thật đạt; không dùng10để thay kết quả full trước. Ba lỗi receipt khác ở full đầu có nguyên nhân riêng: validator kho SMTPTest thiếu prefixphase2d; đã mở đúng prefixproject-local, giữ ACL/header/payload/hash/reparse/SQLTest guard và chạy full lại. Không stressDev hoặc khảo sátDB khác; V04rawartifactlost vẫnPARTIAL.

Full `video-tests-20261015T070352Z-e84e60ef`107/108 có một child scheduler worker vượt deadline30s (TaskCanceledException); không đủ log xác định Development hayStaging ở phiên bản testcase đó. Đã bổ sung log/PID/UTC/exit trongfinally và đường dẫnrun riêng, giữ nguyên deadline. FocusedScheduler `video-tests-20261015T071241Z-170e1600`9/9 đạt; **full cuối `video-tests-20261015T071425Z-1c567963`108/108**, không tái hiện. Không gán nguyên nhân query/SQL/network/AV khi chưa có chứng cứ; không coi việc rerun đạt là đã sửa rootcause. WebDev5258 chỉ6GETtuần tự200 sau migration,5238health200 giữ; khôngload/stress. Hai timeoutTest mới là quan sát riêng, riskchuyên mục V04 vẫnMONITORING/UNKNOWN. Incidentlegacyworkerrawpath được ghi riêng ở[artifactincident2D](phase-2d-artifact-incident.md).
# Quan sát v0.5 — 15/10/2026

Giữ **MONITORING / ROOT CAUSE UNKNOWN**, không tăng timeout hoặc thay cấu hình SQL để che lỗi. Baseline mới trước delta, task`v05-20261015T073755Z-f680ab6e`, chạy binary trước sửa trongbuildriêng:105/108. BaFAILwrapper đều ởbrowser/HTTP; search1024 trả500 với **connection post-login timeout** (~14.020ms theoSqlClient), siteeditmenus có **execution timeout**, scannerbrowser cónavigation30s timeout nhưng chưa cóSQLexception trongweblog để gán cùngnguyênnhân. Không gọi lỗi1024 làreflowlỗiCSS; screenshotreportthực cóstatus500/scrollWidth1024/unlabelled0.

Raw mới: `.local/evidence/phase2d/v05-20261015T073755Z-f680ab6e/baseline/{phase2a/http-web.log,phase2b/site/web.log,scan-http/ui/ui-report.json}`. Test trênSQLTest/khoTest; khôngstressDev, không đổiindex/queryplan/connectiondeadline. Fullv05 sau sửa ghi riêng trongbáocáo; rerunPASSkhôngchứngminhrootcauseđãFIXED.

---

## V05-Finish — giữ MONITORING / ROOT CAUSE UNKNOWN

Focused `v05-finish-tests-20261015T092315Z-4c77c520` có execution timeout trong truy vấn search chung mới; raw SQL test giữ nguyên. Một probe chỉ đọc `sys.dm_exec_requests` với `database_id=DB_ID()` trên **LandPortal_Test** ghi nhận SELECT đang `RESOURCE_SEMAPHORE`, wait924ms/elapsed929ms/blocking0 trong tool transcript. Đây là quan sát tại thời điểm đó, chưa là nguyên nhân gốc đã chứng minh của sự cố chuyên mục V04 hoặc toàn hệ thống; không tái tạo transcript thành raw DMV lịch sử.

Đã thu hẹp phần sort/page search mới thành ID/kind/date/rank, rồi tải tối đa10 summary sau phân trang, tương tự chiến lược ID-first có sẵn. SQL sinh thực nằm trong `v05-finish-search/{count-query.sql,sql-paging.sql}` từng run riêng. Focused15/15 sau sửa đã qua vùng này; kết quả full ghi riêng trong báo cáo Finish. Không tăng SQL/HTTP deadline, không đổi memory/config/index của server, không stress Dev và không khảo sát DB khác. Các timeout cũ vẫn UNKNOWN; xanh ở một lượt không đóng risk.
