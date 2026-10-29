# Phase 3G — signature bridge và approved Apply

**APPLIED / VERIFIED LOCAL cho đúng batch3bài đã duyệt.** Import lần đầu và readback thành công, repeat idempotent, HTTP smoke đạt. Không coi30Blocked hoặc batch mở rộng210bài đã được nhập. **CHƯA PRODUCTION READY.**

Run `phase3g-bridge-20261021T032845Z-fb25abad`; evidence `.local/evidence/phase3f/phase3g-bridge-20261021T032845Z-fb25abad`; pointer `.local/phase3g-bridge-current.json`; `final-result.json`. Giữ nguyên raw/report/backup và sự cố lịch sử.

## Kết quả bàn giao

| Yêu cầu | Kết quả thực tế |
|---|---|
| Nguồn lỗi | Native PowerShell xác minh Valid/đúngthumb/exit0, nhưng progress khi load module đi vào stderr CLIXML; guard cũ từ chối any stderr. Fixture kỹ thuật lượt này tái hiện **76 byte stdout/852 byte stderr**, XML chỉ có progress, không Error record. Không lấy raw approval signature để log |
| Sửa ở đâu | `ApprovedBatchExecution.CreateAsync` gọi bridge mới `PowerShellSignatureBridge.VerifyAsync`; `Command` kiểm soát child, `ValidateResult` kiểm envelope. Không sửa importer/scanner/backup/signer/migration |
| Chữ ký sai còn chặn? | Có: **8/8 nhóm signature-focused PASS**; native tampered/unsigned/unpinned/wrongSHA; lỗi PowerShell thực; exit bất thường; mọi stderr kể cả CLIXMLprogress/Error/malformed; envelope thiếu/duplicate/mâu thuẫn/unknown/sai nonce/hash; certificate policy negatives. Chi tiết dưới đây |
| Backup/approval | Dùng lại backup đầy đủ còn<12h, hash SQL/header và source-file/dependency inventory khớp;37contents/47revisions/37SourceRecords vẫn đúng trạng thái backup. Approval hiện có AuthenticodeValid, scope/hash/SID/date đúng; không ký lại |
| Apply | `safe-apply` exit0, **3 CreateNeedsApproval +1 SourceComparisonOnly +6 Keep +30 Blocked**;10eligible committed/readback verified, không missing/error |
| Thêm thật | **3content +3SourceRecords +4revision +7JPEG/2.048.267byte +6alias**; revision thứ4 là comparison DraftDocument:5, không thay public/editorial pointer |
| Verify/idempotency | **0 mismatch**, repeat10AlreadyCommitted/30Blocked/0delta;17nhóm đối chiếu0unexpecteddifference sau khi loại delta được duyệt;16HTTP requestsPASS |
| Dev hiện tại | **40 nội dung nguồn thật/40SourceRecords/51revision/80alias/60media =48ảnh+12PDF**; **53originalNotScanned +7newClean**;70capture/68snapshot,8ngoạilệ,2video metadata/0video file |

## Bridge và fail-closed

Binary cũ bị giữ nguyên. Build riêng `.local/build/phase3g/phase3g-bridge-20261021T032845Z-fb25abad`, 0warning/0error. CLI mới SHA `e52a8f7ab85f0fe213036a4e2c0b77dcd492577e02f3c5c67d1f78fe14bdcad6`; oldSHA `45597b0d63d6a7feac1e00d5e8189442e1a32cfbd8eb4774a34c45ad812d5027`. `build-attestation.json` và `operator-upgrade-audit.json` liên kết thay đổi bridge được người dùng yêu cầu với packet **không đổi**; không chỉnh historical batch-review hay thay pin ngầm.

Bridge dùng PowerShell hệ thống bằng đường dẫn OS tuyệt đối, `-NoProfile/-NonInteractive`, module Security/Utility chính thức theo `$PSHOME`, module search path child giới hạn, không execute approval. Tắt progress **trước** import; giữ ErrorActionStop. Envelope1 cấu trúc duy nhất bind nonce ngẫu nhiên256bit và SHA exact bytes; kiểm protocol/type/đủ field/không duplicate/unknown, nativeSuccess/Valid, pin, chain local/CodeSigningEKU/current certificate dates. Giữ native Authenticode và local Dev chain policy; không thay trust store, mở quyền hoặc hạ scanner/signature policy. File approval vẫn bị lock đọc như guard trước; hash từ bytes đã lock phải khớp kết quả child.

Parent đọc raw bytes giới hạn32KiB/stream, strictUTF8, timeout15s; yêu cầu exit0 **và** kết quả mật định hợp lệ, không suy luận thành công từ stderr rỗng. **Mọi stderr không rỗng đều bị từ chối**, kể cả progressCLIXML; không strip/filter/whitelist CLIXML ở production. Untrusted file content không được chạy hoặc dùng như JSON kết quả. Child chỉ xuất metadata tối thiểu, lỗi trả failure/exit khác0, không exception text hoặc document bytes. Không log signature block/private key/connection string/credentials.

## Kiểm thử tập trung

Thêm `tests/LandPortal.SignatureTests/{LandPortal.SignatureTests.csproj,Program.cs,packages.lock.json}`. Đây là runner riêng không connectSQL, chỉ dùng bản sao fixture ký kỹ thuật đã có, không ký approval/test cert mới hoặc nhập fixture vào nguồn.

| Nhóm cuối | Kết quả |
|---|---|
| Fixture AuthenticodeValid+legacy CLIXMLprogress; bridge mới suppress tại nguồn | PASS native; rawstdout/stderr `.bin`, XML parser cấmDTD, chỉrootprogress/noError; native bridge mới thành công |
| Tệp ký bị sửa và tệp unsigned chứa JSON giả | PASS native reject |
| Certificate nativeValid nhưng không trong pin; byteSHA khác | PASS native reject |
| CertificateTrustedfalse/EKUfalse/expired/future dates | PASS envelope-policy reject; **không** tuyên bố có native Authenticode fixture expired/untrusted mới hoặc cài thêm cert |
| PowerShellWrite-Error thực và exit bất thường | PASS reject, rawerror của fixture công khai riêng |
| Stderrprogress/CLIXMLhỏng/Errorrecord/unknownbytes | PASS reject tất cả |
| Result thiếu/duplicate/unknown/contradictory/forged/wrongnonce/hash/status | PASS reject |
| Envelope hoàn chỉnh bound đúng | PASS, không suy luận từ exit/stderr đơn lẻ |

`signature-tests/signature-tests.json`: **8/8PASS,0FAIL** trên code cuối. Build/restore log riêng; không chạy full regression hoặc UI. Bằng chứng expired/trust structured là parser/policy negatives, không mock scanner hoặc fake business approval. Raw852byte progress của fixture có thêm Write-Progress chủ động; raw616byte từ real approval ở lượt trước giữ nguyên, không ghi đè/tái tạo thay thế.

## Các guard trước Apply và exact scope

- Batch **26afcb7a-b3c1-4708-9009-c3d296e0afdc**, canonical plan SHA **a6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f**.
- Approval hiện có `.local/approvals/phase3c/80ed3e4c-f2e9-4814-bc18-42f66c07f02f.ps1`, SHA **c8fa859ce10cea9c20433241e995244a13170a3d06f39d10e72ec63a4799a299**, actionApply; expiry21/10/2026 21:54:56VN còn hiệu lực lúc Apply/repeat. Thumb94797BD79E275B09DEF613036BC00DF4EA91345F/CurrentUser, không ký mới hoặc cài lại signer. Exact scope vẫn3content/4rev/7JPEG/6alias, metadata không bind binary SHA hoặc vị trí backup; signed plan/scope không đổi nên tái sử dụng theo chỉ thị người dùng.
- Target **MinhAnh\SQLEXPRESS / LandPortal_Dev**, configured `.\SQLEXPRESS`, DBGUIDb499750c-a0c8-4003-b7ee-dce4938f78dc. RegisteredTHANGPD giữ đúng xác nhận đổi tên cũ, không sửa metadata/guard. `.env`/Identity/quyền nguyên.
- Backup `.local/backups/LandPortal_Test-20261021T015310Z/gaae71ea6/`: dùng lại Dev/TestSQL +130files+132deps; không làm lại backup đã đạt. `backup-file-revalidation.json`, `backup-dependency-revalidation.json`, `backup-sql-state-revalidation.json` xác nhận hashes và trạng tháiSQL phù hợp. ActualApplyguard recheck backup receipt/date/hash/HEADER. File restore đãPASS lượt trước, SQLrestore **NOTRUN**, không gắn nhãn restorePASS.
- Strict proof-set vẫn SHA3ebcf7e9d9b93648147ec4874cc7bbf464162d6e892474ea990311a817498f68, engine1.1.26090.9/defs1.461.118.0/current policy, còn hạn; preflight và safeApply đều revalidate SQLreceipt/bytes/current identity/TTL. Không cần refresh/thaymanifest hoặc scan/promote53Devoriginals.
- Freshpreflight **ABSENT_CONFIRMED10expected/0committed/0errors**, rồi actualApply exit0 lúc03:32:43Z. SauApply reconcile **COMMITTED_VERIFIED10/10/0missing/0errors**. Receipt keys bind batch/planSHA/SourceIdentity.

`tracking-readback.json`: CrawlRun mới đúng **Phase3BCheckpointReplay**, inventory9d95c26f-85a5-4a8a-a2df-61def03cf360/checkpointSHAd41be...; ImportRun.Imported4/Unchanged6. Trường legacy `ImportRuns.Status=Phase3BPartial` vẫn giữ theo code hiện hành vì plan có30Blocked; kết quả10eligible đãcommit được chứng minh bằng receipts/reconcile/readback, không tự đổi nhãnSQL để trông hoàn tất.

## Bảo toàn và HTTP

**37 Content cũ và toàn bộ47revision cũ không bị ghi đè.** DraftDocument:5 có thêm source-comparison revision; chỉ SourceRecord.LastContentHash/LastSeenUtc đổi theo nguồn nhập đã duyệt. Public/current/editorial/approval/taxonomy của bài cũ giữ nguyên. `preservation-readback.json` kiểm17nhóm với delta3/4/7/6 và hai trường theo dõi nguồn được phép; **0unexpecteddifferences**, không gọi17rawhash trước/sau giống nhau. Original53 vẫnNotScanned, filehash đúng; Identity digest và `.env` nguyên. SourceIdentity/date/minute/timezone/author/category/fingerprint/URL mapping và SHA7ảnh được kiểm trực tiếp; không hotlink ảnh mới.

Repeat lúc03:36:24Z: **10AlreadyCommitted/30Blocked**, không content/revision/asset/alias mới, postrepeatverify0errors;17nhóm hiện trạng trước/sau repeat0difference. Không cần rollback. Không chạy Apply lần ba hoặc batch khác.

`http-smoke.json`: **16 requestsPASS riêng**, không gộp với8groups:3article200,7JPEG200 đúngSHA/length/MIME, category+search chứa bài mới, alias301, anonymousadminmedia/privatecomparisonpreview302 tới login, physicalstorage404. Web tạmhttp://127.0.0.1:5387, PID28956 đã dừng; không dừng preview khác, không public/deploy. Ba đường dẫn đã kiểm:

- `/bai-viet/b4a64a8c-6dcc-462c-aa10-43d15220f1db` — Article3666.
- `/bai-viet/8244ca23-458d-42f0-8d74-8ecd3059b80e` — Article3670.
- `/bai-viet/4b2fde89-8d6b-4534-a878-f3959a753dda` — Article3673.

## Handoff và phần chưa thực hiện

Các helper đọc kiểm kê có lỗi serialization so sánh array với wrapperPowerShell, chữ hoa GUID, định dạng `/Date(ms)/` và query metadata chưa quote keywordCheckpoint; đã ghi `harness-notes.json`, chuẩn hóa dữ liệu đọc và đối chiếu độc lập, không sửa nguồn/SQL để ép số. Không tính lỗi helper thành PASS hoặc timeout. Full regression/SQLrestore/Production/UAT/thu thập mới/batch210 **NOTRUN**; lịch sử SQLtimeout vẫn **UNKNOWN/MONITORING**, artifactincidents **PARTIAL**. No.env/ACL/grants/trustglobal/email/DNS/Production/Gitpush changes.

Tiếp theo: fresh preview theo Dev40, khử overlap/aliasconflict, xác minh byte/proof hiện hành của asset batch mở rộng, manifest/delta/backup và approval exactscope riêng. Không dùng approval3bài cho210bài hoặc tự scan53gốc. Các lệnh scoped và hướng dẫn tiếp tục ở [runbook bridge](phase-3g-signature-bridge-runbook.md).
