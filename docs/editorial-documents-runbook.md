# Worker tài liệu và kiểm thử V05-Finish

Phạm vi được mở: Development, đúng `LandPortal_Test`, kho riêng `.local/tests/phase2a`. Web/worker không migrate lúc startup. Không cài service, mở firewall, đổi AV policy hoặc gửi tệp riêng tư lên dịch vụ ngoài. `DefenderTest` chỉ nhận SHA của fixture vô danh/công khai được duyệt; clamd adapter loopback giữ nguyên. Production scanner/PII policy chưa nghiệm thu.

## Chạy

Build/kiểm thử vùng mới rồi full trên code cuối, mỗi lệnh sinh run ID riêng và khóa SQL Test runner:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-v05-finish.ps1 -Region V05
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-v05-finish.ps1 -Region All
```

Web Test thủ công và worker (sau build, port phải rảnh):

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-documents-test.ps1 -Component Web -Port 5289
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-documents-test.ps1 -Component Worker -Provider Clamd -Health
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-documents-test.ps1 -Component Worker -Provider Clamd -Once -AssetId <GUID-Test>
```

Với engine Defender Test sẵn có, dùng `-Provider DefenderTest -ApprovedAnonymousHashes <sha256-public-fixture>`; có thể truyền danh sách SHA bằng dấu phẩy. Không có hash được phép thì chặn; không đổi sang Clean giả. Mã worker thực là `Worker.dll --documents --once --asset <GUID>` hoặc `--health`, không chọn nội dung nguồn. Dừng bằng Ctrl+C; không cài service. Không chạy unscoped worker với tệp chưa được chấp thuận.

Web Dev sau migrate đúng target và backup:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -V05Finish -NoBuild -Port 5288
```

Các preview cũ dùng build riêng, không dừng để giải phóng cổng. Không chạy web trên cùng thư mục binary đang được full runner rebuild; preview bàn giao dùng bản copy build đã kiểm chứng. Chưa tự cấp quyền tài khoản Dev hoặc chỉnh `.env`.

## Trạng thái và đối soát

`EditorialDocuments` giữ ContentId/OwnerId, ScanState, Attempts, AvailableUtc, LeaseToken/LeaseUntilUtc, rowversion, SHA đã quét, engine và thời gian. Hàng đợi dùng SQL transaction/applock để claim, tối đa3 lần; retry theo backoff2^attempt phút. Lease2 phút hết hạn được chuyển ScanFailed rồi claim lại nếu còn lượt. Completion phải còn đúng lease/token/rowversion; kết quả đến muộn không được ghi đè. Worker đọc lại quyền tài khoản và phạm vi bài; không tự đăng tài liệu.

Clean chỉ sau engine thật, kiểm hash trước/sau quét và kiểm định định dạng. Infected là kết luận cuối, không tự retry. ScanFailed không mở tải; hết3 lần cần người vận hành điều tra bằng chứng, không sửa SQL thành Clean. Hệ thống chưa cung cấp thao tác admin cưỡng bức requeue hoặc xóa hàng loạt. Tệp orphan do SQL commit chưa rõ được giữ private để đối soát, không xóa mù. Retention/orphan cleanup Production còn phải thiết kế và phê duyệt.

Tải/preview/publish đọc lại proof, mở handle chỉ đọc, hash/length và quyền/tham chiếu hiện tại; cache `no-store, private`, `nosniff`, CSP sandbox cho tài liệu. Byte thay đổi làm từ chối và cách ly/ghi audit. Nếu phát hiện trong transaction xuất bản bị rollback, kết luận từ chối vẫn có hiệu lực; lần đọc riêng tiếp theo lưu trạng thái cách ly. Không đổi trạng thái53 source originals.

Audit gồm `document.uploaded`, `document.scanned`, `document.tamper.denied` với asset/content/hash/bytes/lease/attempt/provider/engine/signature/output hash, không ghi mật khẩu hoặc file riêng tư vào log công khai. Kiểm thử EICAR chỉ mẫu chuẩn68 byte an toàn trong fixture Test, không phải malware thực.

PDF upload cũ `Origin=Uploaded` chưa có proof bị từ chối public/publish; không dùng cờ local để vượt. Thao tác có CSRF/quyền tại trang tài liệu tiếp nhận từng PDF NotScanned≤10MiB vào `EditorialDocuments`, kiểm hash/length, giữ file tại path private cũ và audit `document.legacy.queued`; không đổi nguồn53. PDF đang dùng chung nhiều hồ sơ hoặc ngoài giới hạn phải đối soát, không tự requeue/mở phạm vi. Word gốc luôn gắn đúng hồ sơ/revision; PDF/Excel Clean có thể dùng lại theo quyền/tham chiếu công khai hiện hành.

## Bằng chứng

Mỗi `test-v05-finish` lưu code hash trước/sau, raw log, exit, artifact hashes và bảo vệ raw cũ. Không sửa mã/script trong lúc run đang đóng băng. Không cộng các focused vào full; tách native scanner, HTTP, browser, worker. Tên file kỹ thuật và thông tin QA trong `.local` không phải dữ liệu nghiệp vụ hoặc mẫu Word thật. Lưu screenshot và báo cáo riêng từng kích thước.

SQL timeout vẫn MONITORING / ROOT CAUSE UNKNOWN; không nâng deadline để làm xanh test, không stress Dev. V04/D15/diagnostic v0.5 incident raw thiếu giữ nguyên. VERIFYONLY không thay thế restore drill; TLS/Production scanner/SMTP/UAT vẫn gate độc lập.
