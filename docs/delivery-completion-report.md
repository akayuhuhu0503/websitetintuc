# Bàn giao SRS v1.0 — dữ liệu và UAT, 22/10/2026

**PARTIAL — CHƯA PRODUCTION READY.** Run `delivery-20261022T072954Z-e0ba61bb`; checkpoint `.local/delivery-current.json`. Không Apply Dev mới, không migration, cấp quyền, sửa .env, gửi email thật hoặc deploy. Phase3L batch9 đã commit được giữ nguyên.

## Đã làm

- Áp dụng đúng xác nhận của chủ dự án: PDF nguồn tối đa **48 MiB**, chỉ các URL ảnh đã kiểm kê trên bốn host được duyệt. Policy `068c52dfbc6ab870f9c60c9943da53510c1880c2af7484dc3da203001326990d` ràng buộc plan/recovery inventory; public upload vẫn 32 MiB PDF, không đổi giới hạn ảnh. 15 GET attempts, một kết nối/≥2,05s, reuse cache; không crawl lại toàn nguồn. Tải/validate 3 PDF lớn +2 JPEG, giữ byte gốc; một PDF vượt cả 48 MiB vẫn chặn.
- Test thêm **4 content/4 revision/8 asset (2 JPEG,6 PDF)/8 alias**: Article1465,3205,3238; LegalDocument240. Bài3075 đã có được giữ nguyên. Verify/readback **0 mismatch**; repeat **5 AlreadyCommitted/13 Blocked**, DataSHA sau repeat và test không đổi. Không tính fixture vào dữ liệu nguồn.
- UAT có profile **Staging/LandPortal_Uat** riêng, config/kho private ngoài code, SQL TLS hợp lệ, native Clamd, Web/Worker mode và MFA bắt buộc. Thêm TOTP theo Identity, replay fence SQL, recovery codes một lần, reset thiết bị của chính chủ và thu hồi phiên. Không bật MFA hoặc cấp quyền cho tài khoản Dev.
- Thêm SMTP STARTTLS/certificate verification/AUTH sau TLS, mặc định Disabled; giữ outbox Uncertain không tự retry. Chỉ chạy SMTP sink loopback Test. Upload ảnh CMS UAT quét Clamd trước dùng, lỗi/infected giữ quarantine; tài liệu/video dùng queue hiện có. Native Clamd tại UAT **chưa có để nghiệm thu**.
- Gói publish thực: `.local/publish/delivery-20261022T072954Z-e0ba61bb-operator`, **311 file có SHA**, manifest SHA `f2b6fd960d7d829a6e1bf5f846eb972fc7d0b366f07861c292fe02ff163c79a6`. Web/Worker Release trong gói giống byte các binary đã HTTP smoke. Có config mẫu không secret, web.config IIS, launcher foreground, runbook và SQL EF idempotent có **DB_NAME guard LandPortal_Uat**. Không cài IIS/service, không chạy SQL script.
- NukeViet: DNS/TCP nay kết nối được; HTTPS bị client từ chối chứng chỉ (key yếu). HTTP công khai robots/home200: **74 liên kết,36 news-ID ứng viên;22 snapshot200,14 redirect301**. Adapter sửa theo snapshot thật: root news route, canonical, news/detail evidence, headline thay cho h1 tên bộ, metadata gốc/ảnh nội dung. 22 trang được parse/stage private, đối chiếu duplicate với QLDD; **0 CMS import**, mapping/tính xác thực HTTP/tài nguyên còn chờ review. Không đưa HTTP public-only này thành chứng cứ transport Production.

## Dữ liệu và phần thiếu

Dev vẫn **259 source/270 revision/602 asset/518 alias**, 365 ảnh/237 PDF; **53 gốc NotScanned**,549 Clean. 70 capture/68 SQL snapshot/8 ngoại lệ/2 video metadata/0 video file. **17 nhóm0diff** và tất cả cột của259 content/270 revision/259SourceRecords khớp checkpoint trước; Identity/.env không đổi.

Inventory cache có **282 typed/277 canonical**,259 canonical có Dev;18 chưa có Dev gồm **5 ready và13 chặn**. Tổng nguồn/phân trang chưa xác minh hết nên **UNKNOWN**, không gọi 277 là toàn bộ website. [Theo loại/chuyên mục](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/known-coverage-final.json), [13 ngoại lệ từng identity/URL](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/remaining-cases-final.json). 13 gồm10 phụ thuộc host ảnh chưa qua TLS trong client hiện hành,1 PDF>48MiB,1 PDF không hợp lệ,1 ảnh vượt pixel policy; giữ staging, không bỏ file để tăng số nhập.

Audit offline1396URL/285candidate/282staged không phát hiện nội dung mới từ cache. Ba candidate còn thiếu detail thật: Article2024 và DraftDocument2024 cùng trả trang h1 trống/canonical2024; DraftDocument3 thiếu title/metadata. **3 typed/2 canonical**, không coi là3 bài đã xác minh. [Raw review](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/parser-pending-review.json). 34 gap Dev (scan/category/video/PDF, giao nhau) và52 dòng lịch sử/50 canonical vẫn giữ riêng; không cộng vào13 hoặc xóa incident.

Native Defender source **23 SHA Clean**, strict recovery **13 SHA là tập con**, engine1.1.26090.9/defs1.461.143.0, SQL receipts đúng byte. Clean/EICAR thật qua kiểm tra video; không dùng mock Clean, không scan/promote53 gốc Dev. Proof Dev13 hết sớm nhất `2026-10-23T08:00:42.7955688+00:00` hoặc invalid ngay khi definitions/policy đổi.

## Một batch Dev chờ duyệt

- Batch **`662f220c-82a7-4858-8786-92741003d857`**.
- Manifest SHA-256 **`21c9ce0500b40f798095de90d63cf0a3bd2cce21fc9d1facab2d86ef76497c84`**.
- Đích **MinhAnh\SQLEXPRESS / LandPortal_Dev**; tối đa **5 content/5 revision/13 asset (7 JPEG,6 PDF)/10 alias**. Source identities1465/3075/3205/3238/LegalDocument240; không sửa bài cũ.
- Preview + fresh preflight PASS5absent/0commit/0errors; **READY_FOR_APPROVAL tại thời điểm kiểm tra, chưa ký/chưa Apply**. [Packet + từng hash asset](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/dev-packet-final.json), [manifest](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/dev-preview/plan.json).
- Lượt tiếp theo cần exact owner confirmation batch/hash này, ký theo cơ chế đã có, backup mới gồm signed approval + strictproof/SQLTestreceipts/media, rồi revalidate target/defs/TTL/scope, Apply/verify/repeat. Reconcile trước retry; withdrawal chỉ theo batch và action authority riêng, không xóa dữ liệu người biên tập hoặc restore cả DB cho lỗi nhỏ. Tool packet đã pin ở `build-final.json`; không tự thay CLI khác trong approved execution.

## Kiểm tra thực, tách từng lượt

| Vùng | Kết quả |
|---|---|
| Build cuối | PASS0warning/0error; không đổi schema/migration |
| UAT/MFA/SMTP options/workflow2A/outboxTCP | 7 case PASS trong runner final9;2 video FAIL do dùng lại fixture đúng guard chống dùng byte của chủ khác. Raw runner giữ nguyên, không gọi9/9PASS |
| Video sau sửa harness | **2/2PASS riêng**, fixture Edge mới; genuine Clean/EICAR, FFmpeg/poster/remux/restart/failure; không phải video nguồn |
| F4 outbox sau sửa evidence root | **1/1PASS riêng**, lostACK/Uncertain/manual receipt/idempotent resend, sink Test thật |
| Nuke adapter cuối | **2/2PASS riêng**:22 snapshot thật/66 negative checks và fixture legacy; không có SQL/network trong tests |
| Release HTTP | **Dev7/7; Test31/31 riêng**, detail/category/search/documents,13 hash/length/MIME, PDF Range206, private preview302/store404 |
| UI Edge thật | Desktop1440×1000 và mobile390×844 PASS, không tràn ngang; mobile3075 đã scroll/decode đủ5ảnh. [Desktop](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/ui-final/desktop-dev.png), [mobile](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/ui-final/mobile-real-3075.png) |
| SQL/file backup | PASS sau resume deduplicate, `C:\TrungTamDuLieuVaThongTinDatDai\website_tin_tuc\.local\backups\LandPortal_Test-20261021T015310Z\dl8682a928`:2SQL COPY_ONLY/CHECKSUM/VERIFYONLY/HEADER/SHA,679file/44deps/file restore0diff; bổ sung SQLTest+72source/proof files trước import. Lỗi duplicate đầu giữ raw; không coi attempt lỗi là hoàn chỉnh |
| Launcher cuối | PASS parse, alias `-Home` → `DataHome`; chạy published runtime thật và từ chối config UAT sai trước SQL. Chỉ launcher đổi, Web/Worker giữ byte đã smoke |
| Actual SQL restore | Backup mới259 **NOT RUN**; actual restore250 trước đó vẫn PASS riêng. Không đánh đồng VERIFYONLY với restore |
| Full regression/Production/UAT hạ tầng | **NOT RUN/BLOCKED**, không cộng các lượt thành full suite; artifact lịch sử vẫn PARTIAL |

## Còn chặn và lệnh vận hành

1. Exact approval batch5 chưa có. Các host ảnh cần endpoint/chain/SAN được client xác minh hoặc bản xuất/tệp chính thức. Ba candidate trang trống cần nguồn phục hồi/mapping chính thức;13 resource cases/34Dev gaps còn riêng. NukeViet cần duyệt mapping/alias301, scanner và source acceptance, kiểm kê tiếp module văn bản/tài nguyên; chưa chuyển đủ.
2. Chưa có máy chủ/app pool/service identity, HTTPS/SQL TLS, LandPortal_Uat và quyền NTFS/approle/SMTP/scanner đích. **Source media Phase3BValidated vẫn có fence Development**: cần quy trình scan/release tại đích được review, không đem proofTest sang mở UAT. Đối soát accepted receipt SMTP thật vẫn Test-only; cần tích hợp bằng chứng relay được tin cậy. Không gọi mọi đường vận hành đã hoàn tất.
3. Chưa UAT đủ SRS,3 Word nghiệp vụ khác tuần, scanner Production, restore/RPO-RTO tại đích, vận hành MFA/recovery, quyền vận hành được phân công, cutover301/DNS/rollback. Private lookup vẫn khóa;10 quyềnDev chưa cấp. Trang Văn bản chỉ đạo điều hành còn chờ adapter/dữ liệu đúng loại. SQL timeout **UNKNOWN/MONITORING**, không thấy timeout mới, không đổi timeout/index/settings.

Dev: `rtk powershell -NoProfile -File scripts/run-dev.ps1 -Port 5188` → http://127.0.0.1:5188. Ports smoke5394/5395/5396 đã dừng đúng process do lượt này tạo; preview cũ giữ nguyên.

Gói UAT: xem `ops/START.md`; người vận hành điền private config ngoài code, provision đúng target theo quyền, rồi IIS trỏ vào `web`. Worker foreground: `powershell -File ops/uat-runtime.ps1 -Home <PRIVATE_HOME> -Config <PRIVATE_HOME>\uat.json -PublishRoot <KIT_ROOT> -Mode Scheduler|Documents|InquiryScan|Video|Mail`; `-Once`, `-Health` scanner/video. SMTP mặc định Disabled. Không chạy các lệnh ghi target khi chưa có authority/hạ tầng.

Code chính đổi: source policy/SafeSourceClient/SourceBatch+recovery binding; SourceIdentity Nuke root adapter; UatDeployment/config/public URL/Worker/MediaService/document+video profile; AdminMfaService/controller/models/views/flow/session; InteractionOutboxService; scripts package-uat/uat-runtime/config+START; focused tests. Không viết lại importer/scanner/CMS/video, không nâng package. Các lỗi build/privateharness/fixture/receipt/path/packaging ban đầu giữ artifact; các kết quả cuối chỉ ở đường dẫn run riêng.

Bằng chứng cuối: [handoff](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/handoff-final.json), [manifest SHA](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/evidence-manifest.json), [seal](../.local/evidence/phase3f/delivery-20261022T072954Z-e0ba61bb/evidence-seal.json). Manifest chỉ kiểm kê run này, các proof/checkpoint tham chiếu và file code liên quan; không phải manifest toàn repository. Các gói cũ giữ nguyên; gói `-operator` là bản hiện hành.
