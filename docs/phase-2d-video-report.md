# Giai đoạn2D — bàn giao15/10/2026

**2D PARTIAL: chức năng/E2E đạt PASS LOCAL; bằng chứng lịch sử D15 còn PARTIAL. Toàn dự án CHƯA PRODUCTION READY.** Chưa nghiệm thu2D toàn bộ vì testcase legacy đã ghi đè raw worker cũ trước khi đường ghi được sửa. [Incident](phase-2d-artifact-incident.md) giữ nguyên thiếu hụt, không phục hồi giả. 2C overall và V04artifact vẫnPARTIAL riêng.

Lượt full cuối `video-tests-20261015T071425Z-1c567963` chạy trên SQL ServerTest thật: **108/108 nhóm đạt**,278đường dẫn mã không thay đổi sau test. HTTP video có7checkpoint riêng, UI12kiểm tra trang/kích thước +8thao tác riêng. [Raw runner](../.local/evidence/phase2d/video-tests-20261015T071425Z-1c567963/run.json), [HTTP](../.local/evidence/phase2d/video-tests-20261015T071425Z-1c567963/video-http/http-report.json), [byte/codec proof](../.local/evidence/phase2d/video-tests-20261015T071425Z-1c567963/video-http/video-sql-proof.json), [VFR/4:4:4](../.local/evidence/phase2d/video-tests-20261015T071425Z-1c567963/video-native-limits/format-proof.json). Không cộng các đơn vị này thành tổng test mới.

## Mã và schema thực

Giữ ASP.NET Core10/MVC-Razor/EF Core/SQL Server, Identity/workflow2A, site/search/scheduler2B, V04 và bốn luồng2C. Upload file máy có SQLsessions/receipts, streaming64KiB/chunk≤8MiB, manifest từngchunk+wholeSHA, owner/resource/fresh rights/CSRF, quota vật lý+reservation, pause/cancel/reselect/resume, TTL/reconcile crash. Worker SQL riêng lease/token/rowversion/3retry/backoff/cancel/revoke: **scan thật → probe → remux/encode → poster thật → verify → Ready**. Original/VTT private; chưaReady không preview/public/publish. GắnVideo/Subtitle/caption theo revision, exactapproval/scheduler, HTML5 MP4/track, GET/HEAD/Range/IfRange/ETag/no-store, shared ref và gỡ cuối thu hồi route.

File chính: `src/Domain/VideoEntities.cs`; Infrastructure`VideoModel/VideoOptions/VideoUploadService/VideoDiskBudget/VideoProcessor/VideoWorker/VideoSubtitleService/VideoReadiness.cs`; Web`VideoAdminController/VideoMediaController/VideoTrackController`, views/JS/CSS, `InternalVideosViewComponent`; metadata/editor/workflow/permissions vàWorkerProgram mở rộng đúng vùng. Scannerfactory giữ mặc định2C10MiB, riêngvideo có cap cấu hình≤1GiB. Testcode`VideoTests/VideoHttpTests`+browser; fixtureVFR vô danh thật14.131byte, không executable. `OutboxReconciliationService` mở đúng evidenceprefixphase2d **SQLTest only**, giữ quyền/receipt header/payload/hash/reparse guards. LegacySchedulerTest đổi sangrunpath/logsfinally và giữ deadline30s. Không viết lại2A/B/C.

Migrations additive: **20261015041226_VideoUploadPipeline** thêmVideoUploadSession/VideoChunk/VideoFile; **20261015044936_VideoSubtitles** thêmVideoSubtitle. Không ALTER/UPDATE/DELETE bảng cũ; Down không chạy. **Test đã áp dụng trước; Dev đã áp dụng cả hai sau backup mới130file lúc07:21UTC và xác minh target.** GeneratedSQL và review/hash ởtask`video-20261015T035659Z-8b55ff16`. Testbackup346file đầu/617file trướcVTT đãCOPY_ONLY CHECKSUM/VERIFYONLY/rehash0lỗi; Devbackup130file theo recordtask (nếu áp Dev, xempre-dev-migration-review.json/migrationlog). **Chưa RESTORE**, không coiVERIFYONLY làrestore drill. Không migrate lúc webstartup.

## Bảo toàn

Dev vẫn **37content/47revision/74alias/53assets(41ảnh12PDF),70capture/68SQLsnapshot,8ngoạilệ,2video metadata/0file**. **0 khác biệt/17 nhóm**, Identity/password/stamps/roles/claims và.env nguyên.53originals vẫnNotScanned; không applyscanTest, không gắnfixture vào source, không thêm/quét nguồn/crawl. TổngTest cófixture tích lũy, không phải37/53. Dev newtables0rows theoSQLstatus; không businessdemo/grant10quyền cũ/quyềninteraction/video/SMTPDev. Git ban đầu toànprojectuntracked: không reset/clean/stage/commit/push; baseline245frozenpathsSCAN0drift trước sửa vàfrozen code cuối kiểm riêng.

## Engine/binary và byte thật

Người dùng cung cấp FFmpeg Essentials, phát hiệnGet-Command sau refreshprocessPATH. **FFmpeg/ffprobe9.0.1-essentials_build-www.gyan.dev**, GCC16.1.0, --enable-gpl/version3/libx264, AACnative/MP4/JPEG; GPLv3+. Rawversion/build/license/caps ởtask`tools-*`, hash/path/license reference trongignored`.local/video-tools.json`; không binary vào repo/package, không download/càiAV/firewall/service. Build origin là self-reportwww.gyan.dev, chưa có chứng từ nguồn tải để xác nhận supplychain hay quyền phân phối Production. [Runbook/license](video-worker-runbook.md).

Final scanner **Defender/1.1.26090.9 sig/1.461.32.0 updated/2026-10-14T21:16:48.0000000+00:00**, quétUTC**2026-10-15T07:20:45.9026469+00:00**; definitions là giá trị thật từngrun, có thể khác baseline1.461.27.0 do hostupdate, agent không thaypolicy. Chỉanonymous/publicSHAallowlist Development/SQLTest/storageTest. Original**56037byte**, SHA`5e307e845481e6abb224a22bcf16829613dcd3399a86b7802e99cb89a6e5423d` →Clean; MP4**48709byte**, SHA`3a6ae6d974c5cf0555b8235594b015128830942eea10beaca94ed9d36a3a5eac`; poster**4314byte**, SHA`de098f28f0382a2e9b7acfa9622a18be32e34c7c47835e29d337c169f2b07020`; **H264/AAC, 2.654867s**, posterJPEG thật. VTTscan**Clean**; EICARZIP chứa mẫu68byte thật**Infected**, không mởallowlist upload nguy hiểm. Mocknegative chỉ orchestration, không tạoClean/Ready.

VFRfailure đã giữ raw: browserWebMavgFPS0/0/rFPS221/12 từng fail-closed; hiện dùngrFPS hữu hạn≤60 và encodefpsmax60, không đoánFPS. H264yuv444p/High444 được scan thật và encode thànhyuv420p/High; chỉ H264420/profilephù hợp/AAC-LC remux. NativeCPUtime thực có audit. Protectedoutputreceiptpolicy2 chỉ reuse cùngasset/inputSHA/binarySHA và actualoutputhash/reprobe, scanoriginal lại; không clone file hay overwrite revision khác. Limits512MiBworking-set monitor/64MiBperallocation/threads2/deadline/disk làhardeninglocal, chưa làOSsandbox/capacity chứng nhận Production.

## Kiểm thử sau sửa cuối

Full **108/108, 0FAIL, exit0 — PASS**, run **`video-tests-20261015T071425Z-1c567963`**, `2026-10-15T07:14:26.2852202+00:00` → `2026-10-15T07:21:10.3918181+00:00`; code**278paths/0drift**, legacyrawguard**520file/0drift**. Buildsolution/Web/Worker0warning/error; NuGet7project audit không advisory được báo nếulogfullhoàntất. Đây làrun mới, không cộng100SCANcũ/97beforeFinishfix/8after/focused2D.

RiêngvideoHTTP**7checkpoint**, UI**12page×viewport checks +8actions**,390/1440,0JS/offorigin theo các report; Edge desktopemulation, không iOSphysical/WCAGcertification. WorkerE2E **kill process thuộcTest sau quan sátleaseSQL**, chỉadvanceexpirySQLTest, restart→scan/MP4/poster, rerun không duplicateaudit; không claim đã đợi nguyên13phútleasewallclock. Native timeout/cancel/corrupt/hếtbudgetđĩa/ngắtclaim/race/missingposter/cache/noaudio/remux/VFR/444 và exactscheduledrevision giữ từngassert. Hếtđĩa testngưỡng dựa freebytes thật, không làm đầyổ. Smokehealth/scopedemptyjob làunit riêng, không thaypositiveworkerE2E. [UI/ảnh thật](ui-review-2d.md).

Lịch sử thực giữnguyên: full104/108 (V04HTTPtimeout +3receiptprefix);106/108 (VFR0/0 vàca phụ thuộcReady);107/108 (worker30s cancel). V04focused10/10 vàSchedulerfocused9/9 riêng, không ghép thànhfull. Timeoutkhôngtáihiện ởprobe≈0,4s vàrerun riêng; không tăngdeadline/SQLtimeout hay sửaquery/index khichưa cócause. [Risk](sql-category-timeout-observation.md).

| ID | Bằng chứng | Kết quả local / giới hạn |
|---|---|---|
| D01/D13 | Devbefore/after17groups,37/53/70/68/8/2meta0file;Testmigrationreview;53NotScanned | PASS bảo toàn |
| D02 | Nativefile→chunkSQL/disk/wholeSHA;MIME/path/size/hash/order/actual8MiB+1request413/quota | PASS fixtures nhẹ; chưa1GiB/IISload |
| D03/D04 | Browserabortchunk1/1024bytesconfirmed→restartweb/browser→reselectmanifest→resume;complete/chunkrace/CSRF/foreign/revoke | PASS |
| D05/D06 | GenuineDefenderbyteboundClean/EICARInfected; unavailable/fault/tamper khôngReady | PASS anonymousTest; privateProductionBLOCKED |
| D07 | Nativeprobe/pixelprofile/MP4/JPEG/SHA/duration;VFR/444/remux/noaudio | PASS |
| D08 | SQLlease/fence/retry/cancel/killownedworker/restart/disk/timeout/cache/missingfile | PASS cases; chưahostpowerloss/HA |
| D09 | Exactapproval/draftcaption/video/VTTprivate/parentbinding/schedulercurrentrights | PASS |
| D10/D11 | GET/HEAD200/206/416/IfRange/ETag/byteproof/realbrowserseek/shared2posts/lastrevoke | PASS |
| D12 | 12video viewportchecks/8actions390/1440,actualeditorcaption/remove/list/keyboard/HTML5/VTT | PASS emulation |
| D14 | `video-tests-20261015T071425Z-1c567963`108/108/exit0,code0drift | PASS; các wrapperHTTP/UI/native không cộngthêm |
| D15 | Rununique/hash/log/ảnh/manual; legacyrawguard0drift saufix;raw worker cũ mấtprovenance | **PARTIAL lịch sử**, không phục hồi giả |

## Chạy và checkpoint

Preview**http://127.0.0.1:5258** (`PASS`), `/admin/login`, `/video`; chỉ dữ liệuDev, uploadDevkhóa.5238preexisting giữ. TestE2E5249 tựdừngownedweb; launcherTest5259/workerforeground. Lệnh:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -Phase2D -NoBuild -Port 5258
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-video-test.ps1 -Component Web -NoBuild -Port 5259
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-video-test.ps1 -Component Worker -NoBuild -Once -AssetId <TestVideoGUID> -Provider DefenderTest -ApprovedAnonymousHashes <approved-videoSHA>,<approved-VTTSHA>
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2d.ps1 -Region All
```

Xem [admin](video-admin-guide.md), [worker](video-worker-runbook.md), [kiến trúc](video-media-architecture.md), [summary/hashmanifest](evidence/phase2d/video-20261015T035659Z-8b55ff16/summary.json). Raw unique`.local/evidence/phase2d/video-tests-20261015T071425Z-1c567963`, task`video-20261015T035659Z-8b55ff16`. Web/worker chỉlocal, không public/deploy/DNS/Git/email/AVpolicy/service. TệpNotScanned không gọiClean.

Bước tiếp: giữD15incident và đốisoát bản rawgốc nếu cónguồnbackupthật; không triểnkhailại code2D hoặc tự promotePASS. Production cần approvedengine/PIIpolicy/OSisolation/toollegalreview/SMTP/MFA/HTTPS/restorethật/UAT/capacity1GiB/IIS/monitoring-alerts/quyềnvậnhành; SQLtimeout vẫn**MONITORING / ROOT CAUSE UNKNOWN**, V04artifact vẫnPARTIAL.2E/2F/Phase3 và2video thiếufile ngoài mốc; không yêu cầu nhập lại từng bài.
