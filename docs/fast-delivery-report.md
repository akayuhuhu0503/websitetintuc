# FAST DELIVERY — bàn giao UAT và dữ liệu — 23/10/2026

**PARTIAL. CHƯA PRODUCTION READY.** Run `uat-ready-20261022T170034Z-2524b742`; checkpoint `.local/uat-ready-current.json`, evidence `.local/evidence/phase3f/uat-ready-20261022T170034Z-2524b742`. Chủ dự án chưa được cấp hạ tầng; `LandPortal_Uat` chưa được phân bổ. Không ký/Apply batch9 mới.

## Đã hoàn thành

- Publish Release Web/Worker/CLI; **319file** có hash. Gói ZIP `C:\TrungTamDuLieuVaThongTinDatDai\website_tin_tuc\.local\publish\uat-ready-20261022T170034Z-2524b742-UAT.zip` (**345,455,377byte**), SHA `7a389985b6fa0f3ed2c36cb9fe7c2713ccb5b5196e7c1103d1e0db5289cb3009`. Manifest SHA `e42f4c72418d9354f9f7c31232522599bf898f17384c9085c0f01b084e8e4ee8`; ZIP readback **0mismatch**. Chỉ chứa web/worker/tools/ops+manifest; không chứa build logs, SQL Test/Identity/fixture data, .env, private key, FFmpeg binary.
- Cấu hình bắt buộc database được cấp và reference phê duyệt đích, không mặc định đã cấp tên đề xuất; giữ guard Dev/Test/restore/Production, Staging/TLS/private stores/MFA/scanner. Không thay guard import/signing/scanner.
- Thêm `uat-common.ps1`, `uat-target.example.json`, `prepare-uat-target.ps1`, `install-uat-iis.ps1`, `test-uat-preparation.ps1`; cập nhật launcher/package/exporter/test-worker/template/START. Installer **PLAN ONLY** mặc định, actual Apply cần máy đích/certificate/credential/private config/hash/SQL checks và operator xác nhận; không tự cài Windows features hoặc cấp quyền.
- Thêm `UatPreflightCommand.cs` (offline hoặc SQL metadata read-only có opt-in), cập nhật `UatDeployment.cs`, `MigrationCli/Program.cs`; schema EF là template bị chặn, operator bind ra SQLCMD đúng instance/DB trước thực thi. **Không có migration mới; không chạy migration/DDL**.
- Nuke adapter thêm `NukeVietDraftCandidate.cs`, `NukeVietRedirect.cs`; cập nhật `Phase3FCommand.cs`: chuẩn hóa **22** snapshot thật đã cache thành private candidates, ngày UTC theo metadata ISO có offset, giữ raw/hash, không biến `lisadmin` thành tác giả. **14** chuyển hướng đã lưu chỉ thành ứng viên chờ snapshot, kiểm cùng source/news ID, cấm external/mismatch/TLS downgrade. Không coi chúng là14bài mới. **0 CMS import,0 request nguồn**;2asset ngoài host/mapping/sourceHTTP acceptance/import adapter vẫn chặn.

Hướng dẫn CNTT và lệnh thực hiện: **`scripts/uat-operator-steps.md` / kit `ops/START.md`**. Sau cấp target: `prepare-uat-target.ps1` → DBA schema/role/ACL → `uat-runtime.ps1 -Mode Preflight [-Online] -Report <new-private-report>` → `install-uat-iis.ps1` plan rồi operator Apply → foreground/health5worker → review/import XML. Không copy dữ liệu/tài khoản Test sang UAT. Runtime chưa được phép chạy thật trên hạ tầng chưa xác nhận.

## Kiểm chứng sau sửa cuối — các lần độc lập

| Kiểm tra | Kết quả |
|---|---|
| Final focused build + Release publish Web/Worker/CLI | PASS; build0warning/0error |
| UAT target/MFA/SMTP policy configuration |3group PASS, offline; không kết nối mail/SQL |
| Packaged preparation / worker XML / runtime guards |17PASS /11PASS /7PASS riêng; không phải fullsuite hoặc restart acceptance |
| Nuke real cached parser/normalization/redirect |22snapshot+14recorded301,130negative assertions PASS;0SQL/network |
| New Release HTTP/Dev |11/11PASS: home/category/article/search/doc/video/login/imageSHA/PDFRange/privateACL; listener5398 đã dừng |
| Dev preservation sau HTTP |17nhóm0diff; media/snapshot hash0mismatch; Identity/.env unchanged |
| ZIP integrity |319manifested files+manifest,0mismatch |

Hai lần thử ops đầu FAIL do adapter property SqlConnectionStringBuilder và XmlNode setter trên PowerShell5.1; đã sửa, giữ partial dirs `ops-checks`/`ops-checks2`, không ghi PASS cho chúng. Final ops artifact nằm `final/packaged-*`. Full regression/UI toàn website/realUAT/scannerEICAR mới/SQLrestore thật/workerrestart thật **NOT RUN**. Không cộng các lần kiểm tra thành fullsuite. Không tái tạo incident artifact lịch sử.

## Dữ liệu và batch đang chờ

Dev hiện **264nội dung nguồn /275revision /615media(372ảnh,243PDF) /528alias**;53gốcNotScanned+562Clean;70capture/68SQLsnapshot/8ngoại lệ/2video metadata0file. **Lượt này0import Test/Dev,0 ký**. Batch5 `662f220c.../21c9ce05...` đã Apply ở lượt trước, không chạy lại. 17nhóm/Identity/env bảo toàn sau HTTP.

QLDD theo inventory đã có **295canonical=264Dev+14Test+17held**;54typed pending(51thiếusnapshot+3parser/source), tổng nguồnUNKNOWN; không coi representation là đã chuyển đầy đủ. Các ledger52dòng/34gaps có giao nhau, không cộng thành bài.

New9 `cbd41632-a662-45b1-8aea-81e447a66c90` / SHA `2e0bf98ace1198e224648fbf8e623a3fe5577171de78fb44be16050a2132e436`,9content9revision52asset18alias: **UNSIGNED/UNAPPLIED/BLOCKED_STALE_PROOF**. Hash plan/proof/inventory/CLI/review cũ giữ nguyên; Defender hiện145, proof143 đã stale dù TTL chưa hết. `.local/evidence/phase3f/uat-ready-20261022T170034Z-2524b742/next9-current.json` và `next9-review-command.txt` chỉ chuẩn bị review, không chạy ký/Apply. Cần refresh đúng byte/scope Test rồi fresh proof-bound manifest/approval exact mới; không dùng packet cũ làm READY. Backup259 cũ không thay backup264 mới cho lần Apply tiếp theo.5introTest chờ category60 authority.

## Điều kiện còn chặn và bước ngắn nhất

1. **BLOCKED hạ tầng:** tên máy IIS/SQL instance+DB đã cấp/HTTPS certificate/private paths+ACL/service identity/Clamd policy/SMTP relay. `LandPortal_Uat` chỉ đề xuất. Source-media scan/release tại target và trusted SMTP receipt integration còn phải hoàn thiện theo policy được duyệt.
2. **NOT RUN trên target:** IIS/SQL TLS/runtime role,5worker/crashrestart,scanner Clean/EICAR/failure,SMTP,MFA/restore/UAT16. PrivateLookup/3Word nghiệp vụ/PO-ATTT/UAT/cutover chưa nghiệm thu; không suy thành PASS từ fixture.
3. **Dữ liệu:** new9 chưa owner approval+proof stale;5intro/category60;17heldQLDD/51missing snapshots;Nuke mapping/HTTP/source/files/import adapter. Không thử lại host lỗi TLS lặp đi lặp lại; không bypass.
4. SQL timeout vẫn **UNKNOWN/MONITORING**, không có timeout mới quan sát; không đổi timeout/index/pooling/settings. HistoricalartifactPARTIAL vẫn giữ. Không .env/grant/Production/DNS/realmail/Git push.

**Tiếp theo ngắn nhất:** CNTT điền thông tin không bí mật vào `uat-target.example.json`, cấp schema/identity/ACL/scanner/relay/certificate; operator dùng START chạy preflight rồi cài UAT và ghi nghiệm thu thật. Song song refresh Test proof để trình một packet exact mới, tiếp tục nguồn theo quyền công khai đã xác nhận, duyệt Nuke mapping. Không yêu cầu file approval crawl riêng làm blocker mặc định.
