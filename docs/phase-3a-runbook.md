# Vận hành kiểm kê Phase 3A

Phạm vi hiện mở là Development, SQL Server local `LandPortal_Test`, kho riêng `.local/phase3a/{session-id-không-dấu-gạch}`. Không chạy migration, không nhập vào CMS và không tự cấp quyền Dev. Đọc [kết quả hiện hành](phase-3a-crawl-report.md) trước khi tiếp tục. Không tạo session khác để lách hạn mức của session đang có.

## Tiếp tục đúng session

Session đã chạy thực: `9d95c26f-85a5-4a8a-a2df-61def03cf360`. Xem báo cáo mới, không gọi nguồn:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3a.ps1 -Action Report -RunId 9d95c26f-85a5-4a8a-a2df-61def03cf360 -NoBuild
```

Mỗi lần sinh thư mục `inventory-{UTC}-{id}` riêng trong `.local/evidence/phase3a`, có `inventory.json`, `urls.csv`, `inventory.html`. HTML là báo cáo local của dữ liệu SQL và kho riêng, không phải website được triển khai. Không public cả thư mục raw. CSV chống công thức; JSON giữ nguồn, hash, trạng thái và lỗi từng URL.

Chạy lại phần đã có snapshot, hoàn toàn không HTTP:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3a.ps1 -Action Resume -RunId 9d95c26f-85a5-4a8a-a2df-61def03cf360 -NoBuild -Offline -Requests 0 -Pages 2000 -Contents 10000 -Assets 10000 -Seconds 600
```

`-ReparseFailed` chỉ thử lại adapter lỗi với dữ liệu đã lưu. `-ReparseAll -Offline` xây lại tập dữ liệu suy ra và quan hệ từ raw đã có; không sửa raw, CMS hoặc lịch sử checkpoint. Không dùng reparse online để tải lại hàng loạt. Bộ đọc chấp nhận JSON checkpoint cũ của prototype và checkpoint Brotli mới, luôn kiểm SHA trước khi đọc.

## Online và quyền quét diện rộng

Chưa có hồ sơ PO/source-owner cho quét diện rộng. Session không có approval bị chặn ở **40 GET đã dành ngân sách cộng dồn**, kể cả restart; mỗi lượt online tối đa40 trang,12 nội dung,16 tệp,600 giây,256MiB body hoàn tất. Body mỗi trang tối đa8MiB, mỗi tệp32MiB. Các giới hạn không phải chứng nhận đủ dung lượng cho mọi video hoặc toàn corpus.

Mẫu đã dùng39 GET. Không chạy tiếp online chỉ để làm đầy con số. Khi đã có phê duyệt thật, người có thẩm quyền lưu bản ghi trong `.local/phase3a/approvals/` và truyền `-ApprovalFile`; công cụ không tự tạo phê duyệt. Bản ghi cần Source=`https://qldd.mae.gov.vn`, ApprovedBy, Reference tới quyết định thật, ExpiresUtc và các MaximumRequests/MaximumPages/MaximumContents/MaximumBytes. Hash bản ghi được lưu vào checkpoint. Đây là ghi nhận quyết định được người vận hành kiểm tra, không xác minh chữ ký pháp lý tự động. Không dùng tên mẫu hoặc ngày giả để mở quota.

Lệnh dưới đây chỉ dùng **sau khi** được duyệt, với đường dẫn hồ sơ thật; bỏ `-NoBuild` nếu cần build source hiện tại:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3a.ps1 -Action Resume -RunId 9d95c26f-85a5-4a8a-a2df-61def03cf360 -NoBuild -ApprovalFile .local/phase3a/approvals/HO_SO_DA_DUYET.json -Requests 100 -Pages 80 -Contents 80 -Assets 100 -Seconds 600 -MiB 256
```

Một kết nối; HTTP client nghỉ ít nhất2.05 giây cộng jitter sau phản hồi. Checkpoint giữ lần nhận phản hồi để nghỉ thêm khi resume bằng tiến trình khác. Không tăng song song. Browser inspector cũng tuần tự nhưng có bộ đếm riêng, không thuộc39 GET của collector. Không chạy inspector song song collector để tăng tải.

Chỉ `-RefreshUrl` một URL đã xác minh khi cần kiểm tra cập nhật: URL đó được ưu tiên, gửi ETag/Last-Modified nếu nguồn từng cấp. HTTP304 dùng đúng byte cache đã kiểm hash; HTTP200 không có validator không được gọi là304. Lỗi404 không xóa nội dung cũ; lỗi429/5xx lưu AvailableUtc/backoff, tối đa3 attempt. Không ép retry trước thời điểm nguồn cho phép. Request reservation ghi trước GET nên nếu bị ngắt có thể chưa tới nguồn; báo cáo phân biệt reservation và response.

## Dừng, tiếp tục và đối soát

Ctrl+C yêu cầu hủy có kiểm soát, lưu `PausedByOperator`. Hết thời gian/quota lưu trạng thái tương ứng. Nếu bị kill, dùng lại RunId; URL đang Fetching được phục hồi thành Retry và ghi rõ lần GET trước chưa biết đã hoàn tất hay chưa. Kiểm thử có new-context/CLI resume thật; chưa nghiệm thu kill tiến trình collector ngay giữa một response đang truyền. Không gọi đó là kiểm thử restore.

File lock được lấy trước khi đọc checkpoint; SQL session applock và pointer check chặn hai người ghi cùng session. Checkpoint bytes có tên SHA, flush xuống đĩa trước khi transaction SQL cập nhật CrawlRun/CrawlItems/SourceSnapshots/ImportIssues và pointer. Tệp orphan khi ngắt vẫn private, không tự xóa. Kiểm hash sai thì dừng, không tự sửa hoặc lấy bản HTML lỗi làm nội dung. Dung lượng trống dưới512MiB chặn ghi; không tự dọn dữ liệu để chạy tiếp.

Đối chiếu SQL theo đúng CrawlRunId. Capture không bằng số hàng snapshot SQL: key chống trùng là URL + SHA. Cùng nội dung có nhiều URL; chỉ gộp sau bằng chứng canonical/loại/mã, không theo tiêu đề hoặc maxID. Trang tiếp lặp first-page bị ghi lỗi và dừng nhánh. `FrontierExhaustedCoverageUnknown` vẫn không phải100% toàn nguồn.

Cờ SyncEnabled/CutoverUtc được đọc lại từ SQL trước GET, kể cả redirect. Không bật lại khi cutover đã chốt. Công cụ không gửi POST, không đăng nhập nguồn, không dò ID tuần tự, không vượt CAPTCHA hoặc tự thêm host ngoài allowlist. Mục có menu# hoặc public adapter chưa biết phải giữ ngoại lệ để PO/source-owner cung cấp dữ liệu.

## Quét và kho tệp

Tệp lưu ở `quarantine/{SHA}.bin`, ngoài static root; trạng thái NotScanned không đồng nghĩa Clean. Không có route public phục vụ kho Phase3A và không tạo MediaAsset/CMS publication. PDF/ảnh/Office có kiểm định định dạng riêng; DOC/XLS cũ, ZIP hoặc video chưa có adapter phù hợp phải giữ lỗi, không tự chuyển đổi hoặc báo đã chuyển thành công.

`-Action Scan` chỉ xem các file tải mới qua network, đã kiểm định; không chọn55 cache copies hoặc53 original assets. Mặc định dùng clamd loopback đã cấu hình; nếu không có engine, ghi ScanFailed, không fallback Clean. DefenderTest chỉ Development/SQLTest/Test storage và danh sách SHA công khai/ẩn danh được phép. Không dùng cho file người dân hoặc tự thay đổi cloud/AV policy. Bảy ảnh mới của lần này có proof engine thật và hash tách khỏi53 original; xem `reviewed-public-image-scans.json` trong task evidence.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3a.ps1 -Action Scan -RunId 9d95c26f-85a5-4a8a-a2df-61def03cf360 -NoBuild -Scanner Clamd
```

Để dùng DefenderTest phải truyền `-Scanner DefenderTest -ApprovedAnonymousHashes <các-SHA-đã-được-phép>`. Không tự lập danh sách từ file riêng tư để vượt policy. Kết quả scan trong checkpoint staging không tự áp vào MediaAssets nguồn hoặc quyền public. Trước3B phải xác minh lại byte/proof/định dạng/quyền và materialize batch theo quy trình được duyệt.

## Kiểm thử và phần chưa nghiệm thu

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3a.ps1 -Region Inventory
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3a.ps1 -Region All
```

Build riêng `.local/build/phase3a/current`; raw `phase3a-tests-*` không ghi đè. Đợi run đóng băng kết thúc mới sửa code/scripts. SQL Test có mutex chung với manual collector; không chạy song song các bộ Test. Source fault429/network/304 trong unit/integration dùng FixtureTransport và phải ghi nhãn; không thay bằng chứng nguồn thật hoặc scanner thật. Full không crawl nguồn; browser source chỉ chạy thủ công với `rtk proxy node tests/browser/source-inventory-check.mjs --live-source-inspection` khi scope đã cho phép.

Chưa có backup/restore drill cho Stage3A. Backup tương lai phải gồm SQL và `.local/phase3a` ngoài các kho media/snapshot/keys/config hiện có; không chỉ sao lưu các root cũ rồi cho rằng đã đủ inventory. Chưa import/publish batch, redirect50URL, UAT hoặc cutover. SQL timeout vẫn UNKNOWN, ba incident raw lịch sử vẫn PARTIAL. Giữ riêng các gate Word nghiệp vụ/PO/Production scanner/MFA/SMTP/restore. Website CHƯA PRODUCTION READY.
