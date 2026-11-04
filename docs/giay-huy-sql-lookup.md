# Tra cứu Giấy chứng nhận hủy: SQL View riêng

Yêu cầu chủ dự án ngày 04/11/2026, attachment `ffd9e929-bbee-49e5-8d86-297e4ec9eefe`, thay thế thiết kế allowlist/hash từng ID tại commit279d9c1. Phạm vi được xác nhận: mọi hồ sơ chính thức đã xác nhận/còn hiệu lực hiện tại và phát sinh sau này, chỉ tám trường hiển thị đã thống nhất. Đây là phê duyệt của chủ dự án, không tạo số văn bản pháp lý giả; thủ tục xác nhận dữ liệu/ATTT khi công khai chính thức do đơn vị vận hành thực hiện nếu được yêu cầu.

## SQL và quyền

- Endpoint `45.120.229.43,1433`, physical `WIN-S1IBJB84OOO`, database nguồn duy nhất `QuanLyDuAnTrungTamDuLieuDatDai`.
- View duy nhất `dbo.vw_GiayChungNhanHuyCongKhai`; login/user `landportal_giayhuy_reader`, chỉ GRANT SELECT View. Không quyền bảng, role SQL quản trị, DML, DDL, EXECUTE, IMPERSONATE hoặc mapping database khác. `website_tin_tuc_app` và role website giữ nguyên.
- View lấy đúng bản DA_XAC_NHAN/hieuLuc/isDel và tài liệu không LOAI_BO theo `/KetQuaCuoiCung`; giá trị chốt ưu tiên kể cả chốt rỗng. Unique index nguồn `UX_GCNH_GTChot_Field` được kiểm tra trước DDL. Không gộp hồ sơ cùng số quyết định; không xuất CCCD/địa chỉ/OCR/audit/PDF.
- Phép chuẩn hóa tỉnh được sinh từ catalog34 trong `GiayHuyProvince`; chỉ tên/alias chính xác, không sửa giá trị gốc. Căn cứ [Nghị quyết202/2025/QH15](https://baochinhphu.vn/nghi-quyet-cua-quoc-hoi-ve-sap-xep-don-vi-hanh-chinh-cap-tinh-102250612191145158.htm), [Nghị quyết175/2024/QH15 về Huế](https://vanban.chinhphu.vn/?classid=0&docid=211916&pageid=27160). Giá trị không ánh xạ chắc chắn vào `chua-xac-dinh`; không suy luận tên huyện/xã thành tỉnh.

Sinh View offline từ source đã build:

```powershell
rtk proxy .local\dotnet\dotnet.exe tools-build-path\GiayHuyLookupCheck.dll --write-view C:\PrivateOps\giayhuy-view.sql
Get-FileHash C:\PrivateOps\giayhuy-view.sql -Algorithm SHA256
```

Không chạy SQL sinh ra riêng trong database bất kỳ. DBA dùng wrapper có guard:

```powershell
rtk proxy powershell.exe -NoProfile -File scripts\invoke-giay-huy-provision.ps1 -ViewPath C:\PrivateOps\giayhuy-view.sql -ViewSha256 <SHA256_DA_KIEM_TRA> -PrivateRoot C:\PrivateOps\new-empty-private-run -ApprovedLanUatSourceTls
```

PrivateRoot phải tồn tại, rỗng, ngoài repository/webroot, inheritance disabled, chỉ operator (và SYSTEM nếu cần). Credential DBA nhập qua Get-Credential, chỉ giữ trong bộ nhớ. Tool xác minh physical/source/authority/no transaction, không có đối tượng trùng, fingerprint cấu trúc, compile projection trước DDL; chỉ tạo View/login/user/grant trong transaction. Secret SQL ngẫu nhiên riêng52ký tự lưu `source-runtime.private.json`, không DBA password. Không backup/migration/import.

Sau commit: đọc View/count/duplicate, kiểm tra quyền và bốn SELECT TOP(0) bảng gốc bị từ chối, không quyền DML qua metadata (không thực thi DML), không access/mapping user database khác, fingerprint bảng nguồn không đổi. Actual execution plan lưu private. `provision-receipt.json` là kết quả kỹ thuật, không thay phê duyệt nghiệp vụ. Nếu lỗi sau COMMITTED_VERIFY, reconcile; **không chạy lại CREATE/không reset login**. Existing View/login/user luôn chặn tool tạo mới.

## Private config LAN/UAT

Merge đúng hai section của `source-runtime.private.json` vào private JSON Web; giữ nguyên `ConnectionStrings:Portal`, role secret, storage, Identity, SMTP và các cấu hình khác. Không ghi đè toàn file bằng mảnh cấu hình nguồn.
Chỉ merge/probe khi receipt đã ghi `VIEW_LOGIN_VERIFIED`; sự tồn tại của file private chưa chứng minh login đã commit hoặc đã kiểm chứng. Không tái sử dụng secret pending của một lần rollback.

```ini
ConnectionStrings__GiayHuyReadOnly=Server=45.120.229.43,1433;Database=QuanLyDuAnTrungTamDuLieuDatDai;User ID=landportal_giayhuy_reader;Password=<PRIVATE>;Encrypt=True;TrustServerCertificate=True
GiayHuy__PublicationScope=OfficialConfirmedEightFields-OwnerApproved-20261104
GiayHuy__LanUatTlsException=OwnerApproved-20261104
```

Một .env/một lệnh Publish hiện có nhận các field tùy chọn này qua template `uat-config.example.json`; không cần kết nối SQL khi build. Scanner output Publish kiểm tra cả secret kết nối nguồn. Không đưa private config vào Git hoặc webroot.

Ngoại lệ TLS chỉ hợp lệ trên LAN_DEMO/Development đúng databaseLAN, hoặc UAT/Staging theo profile đã khai báo; Production từ chối TrustServerCertificate=True. Runtime luôn xác minh physical/source/login và quyền View-only; không application-role website trên nguồn. Giới hạn2connection, connect4s, lệnh10s, tổngbudget15s, no retry; rate30/IP/phút.

## UI và paging

URL `/cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy`. Mặc định2026/ha-noi; giữ mặc định khi rỗng. Năm lấy nguồn đã chốt, không suy từ ngày. Đổi dropdown không gửi request; nút Tìm kiếm gửi form và về trang1. `applied=1` giữ tùy chọn Tất cả qua link phân trang.

Đúng8cột: STT, Ngày phát hành, Số quyết định, Quyết định, Số hiệu giấy chứng nhận, Số vào sổ, Tên chủ sử dụng, Năm. Hai tên nối bằng `; `, rỗng là `—`. Nội dung encode, clamp3dòng/Xem thêm/Thu gọn tại ô. Không Chi tiết/PDF.

Lọc/tìm kiếm/paging nằm trong SQL View query, OFFSET/FETCH đúng20; COUNT_BIG OVER và ORDER BY IssueDate DESC/RecordId, NULLdate cuối. Keyword parameter200ký tự, escape LIKE, Latin1_General_100_CI_AI. Không C# Skip/Take tải toàn bảng, không N+1/NOLOCK/index mới. Trang vượt cuối dùng count có giới hạn và chuyển về trang hợp lệ. Kết quả mỗi trang có total từ cùng statement; dữ liệu nguồn có thể thay đổi giữa các request, không hứa snapshot bất biến. Năm/dropdown là truy vấn độc lập.

## LAN schema15

Build chuẩn vẫn có đầy đủ FR06/schema17. Tùy chọn build riêng `-p:LandPortalLanSchema15=true` chỉ dành cho LandPortal_LanDemo/local Windows auth/Development; mọi runtime khác từ chối. Bản này không map ConsultationEmail, không hiện ô email và từ chối payload email khác rỗng; không giả vờ FR06 hoạt động trên schema cũ. Web startup bắt buộc history15, không tự migration.

```powershell
rtk proxy .local\dotnet\dotnet.exe build tools\GiayHuyLookupCheck\GiayHuyLookupCheck.csproj -c Release --artifacts-path .local\build\giay-huy-lan15 -p:LandPortalLanSchema15=true -p:IisHandoffPublish=true -p:DebugType=None -p:DebugSymbols=false
rtk proxy .local\dotnet\dotnet.exe .local\build\giay-huy-lan15\bin\GiayHuyLookupCheck\release\GiayHuyLookupCheck.dll --check
rtk proxy .local\dotnet\dotnet.exe .local\build\giay-huy-lan15\bin\GiayHuyLookupCheck\release\GiayHuyLookupCheck.dll --check-lan-model
$env:DOTNET_ENVIRONMENT='Development' # explicit local LAN diagnostic context; Staging for UAT
rtk proxy .local\dotnet\dotnet.exe .local\build\giay-huy-lan15\bin\GiayHuyLookupCheck\release\GiayHuyLookupCheck.dll --probe-private <source-runtime.private.json>
```

Probe chỉ in counts/timing/booleans, không record/PII/password. Kiểm tra cổng riêng trước khi chuyển pointerLAN5188; giữ nguyên config website/data/accounts/keys, SMTPDisabled; dừng đúng process bằng launcher hiện có, giữ đường dẫn binary/config cũ để rollback. Không reset/import/reseed/schema upgrade.

Checkpoint: khảo sát DBA đúng nguồn có6948hồ sơ đủ điều kiện, View/login/user chưa tồn tại lúc khảo sát. Lần precheck đầu dừng trước DDL do alias fingerprint JSON trùng; đã sửa, không coi là đã tạo View. Trạng thái triển khai thực tế lấy từ private receipt và PROJECT_STATE mới nhất, không suy từ build hoặc fixture cũ. Chỉ kiểm tra tập trung; chưa phải nghiệm thu UAT/Production.
