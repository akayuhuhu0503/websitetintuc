# Bàn giao UAT cho Trưởng phòng / CNTT TTDL

**Đã hoàn thành:** schema SQL `WIN-S1IBJB84OOO/website_tin_tuc` 70 bảng/17 migration; app login `website_tin_tuc_app`, LandPortalRuntime và hai secret hiện hành. Runtime SQL probe đã PASS theo receipt trước. Không chạy lại migration/role hoặc dùng sa cho runtime.

**Developer bàn giao:** code GitHub, một `.env` private đã điền hai giá trị có sẵn (SQL connection và role secret), bao gồm ngoại lệ TLS UAT đúng phạm vi. Không đưa credential lên Git. Trưởng phòng chỉ copy file vào gốc source và chạy:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Gói tại `.local/publish/TTDL-IIS/`, IIS trỏ `web/`. JSON private được tạo tự động ngoài source/webroot, không phải tự điền 12 biến. Chuyển riêng JSON nếu máy build khác IIS. Build không mở SQL và không yêu cầu hạ tầng đã được cấp.

**CNTT TTDL cần cung cấp:** máy IIS/Hosting Bundle 10, site + binding HTTPS/chứng chỉ hợp lệ, dedicated service account ít quyền cho Web/5 Worker, dung lượng/ổ private và hồ sơ phê duyệt triển khai. ATTT cần chính sách Defender on-prem/privacy được duyệt. Không yêu cầu thay chứng chỉ SQL để thử UAT theo ngoại lệ hiện có; Production vẫn phải theo chính sách được duyệt riêng.

**Tự động trên IIS:** `ops/configure-iis-uat.ps1` đọc binding và pool identity, tính URL, đề xuất storage theo ProgramData/site ID, phát hiện Defender thật + SHA/chữ ký/health/privacy. Chỉ khi có phê duyệt thật và site/pool đang dừng mới cho `-Apply` tạo private home/ACL và gắn Web config. Không tự bật site/worker/SMTP. Ba reference phê duyệt được tách trong `ops/iis-approval.example.json` (mặc định PENDING), không coi tự phát hiện là authority.

**Trước khởi động:** CNTT chạy probe Defender và online Preflight bằng đúng service account; kiểm tra quyền kho, SQL/role, HTTPS và scanner E2E. Cả 5 worker dùng cùng JSON. Video chỉ bật khi FFmpeg được cấp/duyệt; Mail giữ Disabled đến khi có SMTP và phê duyệt gửi thật. Cần hồ sơ/người dùng nghiệp vụ và UAT thực tế trước vận hành.

Lệnh cụ thể: [hướng dẫn một file / một lệnh](iis-one-command.md). **Publish không còn bị chặn bởi biến kỹ thuật chưa cấp; Activation/Preflight vẫn bị chặn khi hạ tầng/authority chưa đủ. CHƯA PRODUCTION READY.**
