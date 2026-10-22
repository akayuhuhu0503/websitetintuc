# Vận hành video local/Test

Chỉ chạy từ workspace dự án. Không service install, firewall/AV policy, .env write, Dev grant, deployment hoặc email. Không tự scan source/citizen private files. Video source còn2metadata/0file và53originalsNotScanned; không tự enqueue chúng.

## Tool và cấu hình

Người dùng cài FFmpeg Essentials. Agent tìm bằng Get-Command/where sau refresh **process PATH**, chạy executable thật và lưu raw `-version`, `-L`, `-encoders`, `-formats`. Đã xác minh **9.0.1-essentials_build-www.gyan.dev**, GCC16.1.0, `--enable-gpl --enable-version3 --enable-libx264`, nativeAAC, MP4/JPEG. Binary tự khai build origin www.gyan.dev; không có hóa đơn/checksum tải ban đầu để chứng nhận chuỗi cung ứng. Hash executable đang chạy có ghi nhận riêng. GPLv3+ và libx264 cần pháp chế rà điều kiện sử dụng/phân phối trước triển khai; không gói binary vào repo/public zip. [FFmpeg legal](https://ffmpeg.org/legal.html), [faststart](https://ffmpeg.org/ffmpeg-formats.html).

Đường dẫn, SHA256 của hai executable và `licenseReference` đặt trong file local ignored `.local/video-tools.json` bởi quy trình phát hiện đã chạy; `scripts/video-test-profile.ps1` nạp vào environment của **process**, không hardcode source hay sửa .env. Schema: `ffmpegPath`, `ffprobePath`, `ffmpegSha256`, `ffprobeSha256`, `licenseReference`. Production approval không được suy ra từ file Test này. `.env.example` có biến mẫu; Video.Enabled=false mặc định.

Giới hạn mặc định:1GiBfile,8MiBchunk,3active sessions/user,24hTTL,16GiBquota bảo thủ,512MiBfree reserve,300s mỗi encode,2threads, concurrency1; tối đa4K/60FPS/1giờ input, output giữ aspect/no upscale khi encode. Video không audio giữ nguyên không audio. Chỉ H264yuv420p/profileBaseline-ConstrainedBaseline-Main-High, AAC-LC hoặc không audio mới remux/copy+faststart; H264444/10bit hoặc audio profile khác phải encode lại bằng libx264/yuv420p/AAC-LC. WebM cóavgFPS0/0 chỉ được fallback sangr_frame_rate ffprobe hữu hạn, dương và≤60; cả hai không hợp lệ vẫn fail-closed, không tự đoán10/30FPS. Encode cófpsmax60. CPU native thực cộng dồn theo job được ghi audit; không coi fixture nhẹ là capacity benchmark. Không đổi đuôi để báo Ready. Hai mode inquiry/mail/scheduler cũ chạy riêng, không tự kích hoạt từ video worker.

## Lệnh đã có trong repo

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2d.ps1 -Region Video
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2d.ps1 -Region All
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-video-test.ps1 -Component Web -NoBuild -Port 5259
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-video-test.ps1 -Component Health -NoBuild -Provider DefenderTest -ApprovedAnonymousHashes <SHA256-fixture-vô-danh-được-duyệt>
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-video-test.ps1 -Component Worker -NoBuild -Once -AssetId <GUID-video-Test> -Provider DefenderTest -ApprovedAnonymousHashes <SHA-video>,<SHA-VTT-nếu-có>
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-video-test.ps1 -Component GC -NoBuild
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -Phase2D -NoBuild -Port 5258
```

Thay placeholder bằng GUID/hash đã duyệt; hash không tự sinh từ tệp công dân chỉ để lách policy. Worker không có `-Once` poll5s. Default providerClamd cần engine loopback/port đã được phép; thiếu engine→ScanFailed/Blocked, không Clean. Health chỉ đọc config/pinned tools/scanner/SQLqueue/expiredleases, không claim job. GC là thao tác dọn TTL bounded, giữ committed originals/revisions/shared output. Kết quả health không thay E2E. `.local/build/phase2d/current` tách khỏi preview5238 cũ. Không rebuild khi process đang giữ DLL; chỉ dừng PID/start-time thuộc lượt hiện tại. Không tắt dotnet/SQL/IIS/AV toàn máy.

Web Test mặc định5259; automated E2E5249 tạo tài khoản/password ngẫu nhiên riêng Test, ngắt network, restart web/browser/worker, rồi dừng đúng process. Dev5258 chỉ preview dữ liệu cũ, **upload bị khóa**; không có quyền mới hoặc cấu hình scan Dev. Chỉ dùng `-NoBuild` sau build thành công tương ứng.

## Chẩn đoán và phục hồi

| Tình trạng | Hành động |
|---|---|
| VIDEO_PROCESSOR_NOT_AVAILABLE / NOT_APPROVED / HASH_MISMATCH | Kiểm tra binary/path/hash/license local; không tải cài hoặc bỏ pin để ép Ready. Sau phê duyệt sửa cấu hình process, retry nếu còn attempts. |
| NotScanned/Scanning backlog | Health scanner thật; kiểm explicit anonymous SHA/SQLTest/storageTest. Không tắt AV/cloud hoặc dùng mockClean. |
| Lease quá hạn sau crash | Worker mới lấy lại lease SQL, tăng attempts, scan lại; output receipt hợp lệ được kiểm byte/probe và reuse. Không cập nhật Ready thủ công. |
| ScanFailed / processor timeout / corrupt codec | Xem mã lỗi/audit, tối đa3attempts/backoff; file vẫn quarantine. Native output chưa đủ không có player. |
| Infected / hash mismatch | Cách ly, không preview/download public; điều tra đúng hash. Không retry tự động hoặc đổi Clean bằng SQL. |
| Disk budget/quota / inventory limit | Kiểm dung lượng thật, reservation, orphan inventory; chạy GC TTL được phép. Backup rồi đối soát thủ công original mồ côi; không xóa source/shared/history. |
| Cancelled | Token ghi kết quả đã bị thu hồi; process native có thể kết thúc theo timeout. Không hard-delete original để hủy. |
| Ready nhưng thiếu MP4/poster/VTT | Public/publish gate fail-closed. Đối soát file/hash, không đổi bản khác vào path cũ. Giữ audit/backup trước repair. |
| 206/416 bất thường hoặc worker offline | Kiểm route/head/range/ETag và queue/lease bằng health/log; cần operator monitoring/alerts sau triển khai. Chưa cài alerting/service tự động. |

Ước tính đĩa theo original + output/poster + upload chunks + output attempt tạm, cộng buffer và retention. Không chốt cấu hình máy chủ từ fixture vàiKiB. Chưa load test1GiB/IIS/reverseproxy/HA; request chunk Kestrel≤8MiB đã kiểm413 thật, không nới limit PDF/hỏi đáp. IIS/reverseproxy cần giới hạn riêng có phép, theo [ASP.NET Core10 upload](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0).

Giữ SQL timeout **MONITORING / ROOT CAUSE UNKNOWN**, không tăng globaltimeout hoặc stressDev. V04 artifact incident **PARTIAL** không tạo lại raw giả. Scanner Production/privatePII, SMTP/MFA/HTTPS/restore/UAT/quyềnDev/2E/2F/Phase3 vẫn riêng. COPY_ONLY CHECKSUM + RESTORE VERIFYONLY là kiểm backup, **chưa phải thực hiện RESTORE**.
