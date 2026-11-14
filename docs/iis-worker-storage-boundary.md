# Video Worker: đọc metadata IIS bằng tài khoản dịch vụ

## Nguyên nhân

Web chạy trong IIS nhận `APP_POOL_CONFIG`, trỏ đến bản cấu hình riêng do WAS tạo cho pool. Worker chạy từ Task Scheduler không tự nhận biến này. Code cũ chuyển sang `appcmd list vdir /xml`, thao tác đọc cấu hình IIS toàn máy mà tài khoản dịch vụ thông thường có thể không được phép thực hiện; lỗi sau đó bị gộp thành `IIS-CONFIG-003-STORAGE_BOUNDARY` như lỗi manifest hoặc đường dẫn.

Đây là nhánh lỗi đã xác định trong source. Chưa có stderr hoặc mã truy cập từ lần chạy thực tế của `GDLAWorker`, nên chưa kết luận mọi lỗi `STORAGE_BOUNDARY` trên máy IIS đều là thiếu quyền. Bản sửa phân biệt các trường hợp:

| Mã khởi động | Ý nghĩa |
| --- | --- |
| `IIS-CONFIG-003-IIS_METADATA_ACCESS_DENIED` | Tài khoản Worker không đọc được metadata IIS. |
| `IIS-CONFIG-003-IIS_METADATA_MISSING` | Chưa tìm thấy metadata của pool đã chỉ định; kiểm tra pool đã khởi động và tên pool. |
| `IIS-CONFIG-003-IIS_METADATA_INVALID` | Metadata không hợp lệ hoặc không thể dùng làm nguồn xác minh. |
| `IIS-CONFIG-003-IIS_METADATA_UNAVAILABLE` | Nguồn metadata hoặc công cụ đọc chưa khả dụng; không được coi đây là bằng chứng đường dẫn kho sai. |
| `IIS-CONFIG-003-IIS_MAPPING` | Mapping IIS không trùng webroot hoặc ánh xạ vào vùng private/runtime không được phép. |
| `IIS-CONFIG-003-STORAGE_PATH` | Các kho dữ liệu nằm sai phạm vi, lồng nhau hoặc có reparse point. |
| `IIS-CONFIG-003-PRIVATE_HASH` / `IIS-CONFIG-003-STORAGE_MANIFEST` | Không đọc/xác minh được gói/configuration hoặc hash không khớp manifest; mã tùy bước kiểm tra. |

Microsoft mô tả WAS tạo bản cấu hình riêng và chỉ cho SID của pool tương ứng đọc; tài khoản chạy task riêng không tự có SID đó. Không cấp quyền đọc `ApplicationHost.config` toàn máy để khắc phục. [Tài liệu Microsoft về cách ly cấu hình IIS](https://learn.microsoft.com/en-us/troubleshoot/developer/webapps/iis/was-service-svchost-process-operation/understanding-identities).

## Cập nhật một lần

Sau khi bản sửa được duyệt và có trong nhánh triển khai, giữ nguyên `.env` và dùng quy trình hiện hành:

```powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Dừng pool `WWW_GDLA` và các Worker đang chạy, giữ bản `web` trước cập nhật để rollback, rồi thay nguyên thư mục `web` gồm `.runtime`. Giữ nguyên sibling `private`, `tools`, SQL, tài khoản và media. Không chỉnh `uat.json` hoặc manifest sau build.

Trong Action của task Video hiện có, dùng:

```powershell
powershell -NoProfile -NonInteractive -File "D:\WWW_GDLA\publish\TTDL-IIS\web\.runtime\run-worker.ps1" -Mode Video -IisAppPool WWW_GDLA
```

Task tiếp tục chạy bằng `GDLAWorker`, không bật quyền Administrator và không tạo task/process trùng. Tham số `-IisAppPool` chỉ chọn metadata của pool để kiểm tra; không tự cấp quyền, không thay đổi cấu hình IIS và không bỏ qua kiểm tra webroot.

Nếu báo `IIS_METADATA_ACCESS_DENIED`, quản trị viên máy chủ cấp **Read** cho đúng tài khoản `GDLAWorker` trên file cấu hình riêng của **pool WWW_GDLA** do WAS tạo, và quyền đi qua thư mục cha nếu cần. Ở cấu trúc có thư mục riêng cho pool, chỉ áp dụng quyền đọc kế thừa trong thư mục pool đó; không cấp cho toàn bộ `appPools`, `IIS_IUSRS`, Users, `ApplicationHost.config` hoặc quyền Administrator. Vị trí do WAS xác định bằng `ConfigIsolationPath`; launcher tìm tên pool chính xác, không quét ổ đĩa. [Tài liệu Microsoft về ConfigIsolationPath](https://learn.microsoft.com/en-us/troubleshoot/developer/webapps/iis/www-administration-management/use-registry-keys).

Với cấu hình WAS mặc định, tệp thường là `C:\inetpub\temp\appPools\WWW_GDLA\WWW_GDLA.config`; bản IIS cũ có thể dùng `C:\inetpub\temp\appPools\WWW_GDLA.config`. Chỉ thao tác quyền trên tệp/thư mục riêng đang tồn tại của pool này; không tạo XML hay cấp quyền cho đường dẫn đoán trước. Nếu WAS dùng ổ/thư mục khác, registry là nguồn xác định. Công cụ chỉ đọc registry và metadata, không sửa ACL.

WAS có thể tạo lại metadata khi thay đổi cấu hình hoặc khởi động pool. Nếu quyền đọc bị thay đổi sau đó, lần khởi động hoặc kiểm tra lại của Worker phải bị chặn với lỗi quyền tương ứng; không dùng bản XML chép ra nơi khác hoặc snapshot cũ để vượt kiểm tra. Đây là kiểm tra lúc khởi động, không phải cơ chế theo dõi thay đổi mapping IIS liên tục. Cấp quyền phải do quản trị viên thực hiện đúng phạm vi, công cụ không tự chỉnh ACL.

Khởi động lại pool và task hiện có. Trong `/admin/videos`, xác nhận heartbeat Worker mới, bộ xử lý sẵn sàng và hàng đợi Pending được xử lý. Quyền đọc metadata không thay thế quyền storage: pool và Worker vẫn cần dùng chung `temp`/`keys`, đọc tool và quyền thao tác kho dữ liệu hiện có. Không xóa keys để xử lý lỗi heartbeat.

Nếu cập nhật lỗi, dừng các tiến trình, trả nguyên `web` đã giữ lại và Action task trước cập nhật nếu đã đổi. Không rollback SQL hoặc kho media.

## Phạm vi bảo vệ

Worker vẫn phải kiểm tra binding manifest, đúng IIS webroot, private nằm ngoài vùng phục vụ HTTP, bốn kho tách biệt, không lồng nhau, không reparse point và môi trường Staging/UAT hợp lệ. Không có quyền đọc metadata xác thực thì dừng với nguyên nhân rõ ràng. Không giả định gói Publish tự chứng minh mapping IIS đang chạy.

## Kết quả kiểm thử bản sửa

- Release Web/Worker và harness build: 0 lỗi, 0 cảnh báo.
- `LandPortal.WebBundleTests --storage-boundary`: 55 assertion PASS trên Windows filesystem cô lập, gồm NTFS từ chối ReadData thật rồi khôi phục đúng DACL fixture, sai pool/webroot, private/runtime bị ánh xạ, đường dẫn kho sai, hash sai, XML/DTD/quá dung lượng và hợp đồng lỗi media 503. Các assertion thông báo an toàn được tính trong 55, không phải 55 E2E.
- `worker-package.ps1 -VerifyIisPoolSelector`: 3 kiểm tra PASS. Launcher và Worker thật chạy `-VerifyPackage` với cấu hình giả lập; selector đúng được truyền, selector cũ bị thay và tên pool traversal bị chặn trước khi chạy Worker. `sqlOpened=false`, `workerStarted=false`.
- Không chạy lại bộ E2E upload/video, không kiểm thử trên IIS vận hành hoặc truy cập SQL. Chưa xác nhận quyền `GDLAWorker` thực tế, heartbeat hay xử lý queue trên IIS. Không tạo tài khoản Windows/SQL hoặc thay quyền vận hành.
