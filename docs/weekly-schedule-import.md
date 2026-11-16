# Nhập lịch công tác từ Excel/PDF

Công cụ CLI độc lập, không cần cập nhật website để chạy. Không có lệnh xuất bản, migration, cấp quyền hay thay cấu hình. Dữ liệu được ghi qua `WorkflowService` và `EditorialDocumentService`, tài khoản CMS được xác thực tại console và kiểm tra lại quyền theo từng thao tác.

## Đầu vào và kết quả kiểm kê

ZIP chứa đúng `Tong hop link website_T11-2026_Fin.xlsx` và `Lich Tuan/<STT>_*.pdf`. Sheet `2.4.1_LichCT`, STT 1–43, dòng 7–49. Dùng **số đầu tên PDF**, không lấy các số tuần/số trong ngoặc phía sau.

Gói đã kiểm kê: 43 PDF, 120 trang. Chỉ E48 (STT 42) sai: **2 → 3**. Giữ nguyên 17 ngày dạng số Excel, mọi giá trị/kiểu/định dạng A/B/C. Không dùng ngày cột B làm ngày phát hành bài.

| STT | Tuần trong tiêu đề | Khoảng ngày Excel | Khoảng ngày PDF |
|---|---|---|---|
| 39 | 41 | 12–18/10/2026 | 05–11/10/2026 |
| 40 | 42 | 19–25/10/2026 | 12–18/10/2026 |
| 41 | 43 | 26/10–01/11/2026 | 19–25/10/2026 |
| 42 | 44 | 02–08/11/2026 | 26/10–01/11/2026 |

Bốn hồ sơ vẫn có thể lưu nháp đúng tiêu đề nguồn; cán bộ phải đối soát trước khi gửi duyệt. Công cụ không sửa tiêu đề hoặc suy diễn tuần đúng. Các ngày dạng số chỉ được cảnh báo để rà soát, không chuyển đổi.

## Chạy công cụ

Build một lần trên máy Developer có .NET 10:

```powershell
dotnet publish tools/WeeklyScheduleImport/WeeklyScheduleImport.csproj -c Release -o .local/tools/weekly-import
```

Kiểm kê offline, chưa mở SQL (thư mục kết quả phải mới):

```powershell
dotnet .local/tools/weekly-import/WeeklyScheduleImport.dll inspect --zip C:\website_upload\website_upload.zip --out C:\GDLA-Private\weekly-inspect
```

Kết quả gồm `source-backup.zip`, `original.xlsx`, `inventory.json` và `LichCT-pages-reviewed.xlsx` chỉ sửa E48. ZIP/workbook sao lưu được kiểm tra SHA256. Không đưa thư mục kết quả vào Git/webroot.

### Dry-run và Import tại môi trường được cấp quyền

Chỉ thực hiện sau khi chủ hệ thống cấp quyền thực thi trên đích. Cần cấu hình JSON runtime hiện hành hợp lệ, có đường dẫn storage thực sự và cùng Application Role, TLS, danh tính SQL của ứng dụng. CLI **không nhận cấu hình portable chưa xác định storage**, không tự sửa JSON/manifest hay đoán kho IIS. Chạy tại nơi có quyền truy cập đúng kho tệp của môi trường đích; không dùng một kho trên Developer để giả làm kho IIS.

Tài khoản CMS cần quyền soạn/sửa bài, tải tài liệu và phạm vi chuyên mục 76. Dùng tài khoản hiện có; nhập mật khẩu trực tiếp ở console, không đặt vào lệnh/file. Tài khoản khóa, chưa đổi mật khẩu hoặc yêu cầu MFA sẽ bị chặn; CLI không vượt các điều kiện này.

```powershell
dotnet .local/tools/weekly-import/WeeklyScheduleImport.dll dry-run --zip C:\website_upload\website_upload.zip --config C:\GDLA-Private\runtime.json --user TEN_CAN_BO --out C:\GDLA-Private\weekly-import
```

Đọc `inventory.json` và `plan.json`: mỗi STT có Create/Reuse/Resume/AlreadyImported/Conflict. Plan ràng buộc ZIP, workbook, SQL server/database/schema 18, Application Role, kho media và người nhập. Không tạo chuyên mục 76 nếu thiếu; không ghi đè hồ sơ trùng tiêu đề hoặc PDF chưa đối soát được. Nếu có Conflict, dừng trước khi ghi.

Sau khi kiểm tra đúng plan và được phép nhập, dùng SHA256 **thực tế do dry-run in ra**:

```powershell
dotnet .local/tools/weekly-import/WeeklyScheduleImport.dll import --zip C:\website_upload\website_upload.zip --config C:\GDLA-Private\runtime.json --user TEN_CAN_BO --out C:\GDLA-Private\weekly-import --confirm-plan SHA256_CUA_PLAN
```

Chỉ tạo **StaticPage / chuyên mục 76 / bản nháp**, giữ nguyên PDF/tên/hash, gắn Attachment. Không thiết lập `WeeklyYear`, `WeeklyWeek`, bảng lịch suy diễn hoặc `WeeklyScheduleRelease`. Năm/tuần đã có lịch phát hành nhưng không khớp hồ sơ sẽ được báo Conflict để xử lý từ hồ sơ hiện hữu.

Chính sách PDF hiện hành vẫn áp dụng: kiểm định cấu trúc thật, không giả Clean. Nếu no-AV hợp lệ đã bật, trạng thái là `NotScanned` / `OfficeValidatedNoAv`. Nếu môi trường yêu cầu scanner, vẫn kiểm tra scanner và giữ PDF cách ly đến khi đủ điều kiện; công cụ không tự bật ngoại lệ.

## Gián đoạn, sao lưu và đối soát

- Công cụ sao lưu **nguồn**, không tự backup SQL vận hành. Trước đợt ghi được cấp phép, vận hành giữ điểm khôi phục SQL/storage theo quy trình sẵn có; không dùng backup Test thay cho backup đích.
- Mỗi bước lưu nháp/gắn PDF sử dụng transaction và command receipt của CMS. Session lock ngăn hai importer chạy đồng thời trên cùng database.
- `receipt.json` ghi STT → ContentId → MediaId → trạng thái sau từng bước. Nếu process dừng ngay sau commit, mapping có thể phục hồi từ `ContentCommands` và `EditorialDocuments` thay vì tạo lại bài.
- Chạy lại cùng Import/plan chỉ tiếp tục bản nháp dở dang còn nguyên vẹn. Nếu cán bộ đã sửa/gỡ tệp, dừng đối soát; không ghi đè hoặc tự gắn lại.
- Không xóa dữ liệu hay media khi lỗi; không rollback bằng DELETE hàng loạt. Hồ sơ mới có thể xử lý qua CMS. File mới lưu vào khóa riêng, không ghi đè tệp cũ.

Đối soát chỉ đọc, có thể dựng lại receipt bị mất:

```powershell
dotnet .local/tools/weekly-import/WeeklyScheduleImport.dll reconcile --zip C:\website_upload\website_upload.zip --config C:\GDLA-Private\runtime.json --user TEN_CAN_BO --out C:\GDLA-Private\weekly-import
```

## Sync Links sau khi cán bộ công bố

Cán bộ xem từng ContentId trong CMS, đối chiếu PDF/cảnh báo rồi dùng luồng **gửi duyệt → duyệt → xuất bản** hiện hành. Công cụ không có lệnh thực hiện thay các thao tác này.

```powershell
dotnet .local/tools/weekly-import/WeeklyScheduleImport.dll sync-links --zip C:\website_upload\website_upload.zip --config C:\GDLA-Private\runtime.json --user TEN_CAN_BO --out C:\GDLA-Private\weekly-import
```

Sync đọc PublishedRevisionId hiện hành, tiêu đề/chuyên mục/attachment, chính sách file; GET bài thật và PDF không đăng nhập, yêu cầu HTTP 200/MIME đúng/hash khớp, kiểm tra lại trạng thái SQL rồi mới điền D. Không chấp nhận redirect, trang đăng nhập, bài nháp, tệp khác hoặc bài đã thu hồi. Link dùng `/bai-viet/{ContentId}` trên host runtime thật.

Mỗi lần tạo `LichCT-links-<thời điểm>.xlsx` mới từ **workbook gốc**, D7:D49 chỉ chứa link xác minh được, các dòng chưa công khai để trống. Có hyperlink bấm được. Chỉ E sai được sửa. Mọi entry khác trong XLSX (các sheet khác, styles, ảnh, công thức, cấu trúc workbook) được sao chép nguyên byte; trên sheet đích giữ nguyên tất cả ô ngoài D/E cần thay. Không dùng thao tác Save toàn workbook của thư viện làm đổi ngày/định dạng.

Lệnh có thể chạy lại sau mỗi đợt công bố. Đây là đồng bộ **khi chạy CLI**, không phải một dịch vụ tự chạy trên IIS; không tạo Task Scheduler hoặc yêu cầu deploy website mới.

## Kiểm thử tập trung

```powershell
powershell -NoProfile -File scripts/test-weekly-import.ps1 -SourceZip C:\website_upload\website_upload.zip
```

Harness tạo instance LocalDB tên ngẫu nhiên và database Test **mới**, kiểm tra đúng máy/local trước DDL, tạo schema 18 chỉ ở Test, tài khoản giả và Application Role riêng. DI chính sách no-AV chỉ trong harness. Không nạp `.env` hay credential thật.

Kiểm tra 43 PDF thực, backup Test COPY_ONLY/CHECKSUM/HEADERONLY/VERIFYONLY/SHA, trùng/thiếu/sai tên/PDF giả, gián đoạn và phục hồi, tái sử dụng hồ sơ, bảo toàn workbook, phân quyền, xuất bản bằng các vai trò giả, HTTP bài/PDF, link lặp và thu hồi. Dừng HTTP/LocalDB khi kết thúc. Không chạy full regression.

Kết quả ngày 16/11/2026: Release build 0 lỗi/cảnh báo; **48 kiểm tra tập trung PASS**. Trường hợp cuối gồm 42 bản nháp mới + 1 hồ sơ có sẵn được tái sử dụng, tổng 43 PDF; kiểm thử xuất bản 2 bài trên HTTP/SQL Test rồi thu hồi 1 bài. Một lượt trước đó cũng đã kiểm tra nhập mới đủ 43 hồ sơ. Bộ đọc Excel độc lập xác nhận 17 sheet được giữ nguyên, hai hyperlink thử nghiệm bấm được và chỉ E48 sửa số trang. File kiểm thử có URL localhost không phải bản bàn giao có link vận hành.

SQL vận hành/IIS chưa được truy cập hoặc thay đổi. Bản Excel kiểm kê bàn giao chỉ sửa số trang, chưa có link GDLA mới. Cần quyền thực thi, cấu hình/storage đích hợp lệ và cán bộ duyệt/xuất bản trước khi xuất Excel chứa các link thật.
