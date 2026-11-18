# Nhập mục 2.4.2 — Quy hoạch, kế hoạch sử dụng đất qua CMS HTTPS

Công cụ chạy trên máy Developer, không đọc `.env`, kết nối SQL thật hoặc sửa IIS. Dùng lại client CMS, CAPTCHA/CSRF, lệnh lưu nháp, tải tài liệu và đối soát của WeeklyScheduleImport. Chế độ 2.4.1 giữ nguyên; không dùng thư mục nhật ký của 43 lịch tuần cho mục này.

**Cập nhật 18/11/2026:** đã chuẩn bị 32/32 hồ sơ đạt kiểm định tệp nhờ bản chuẩn hóa có mapping/hash rõ ràng và giới hạn PDF EditorialDocument 104 MiB/2.000 trang. Dùng `-PdfOverrides` theo [hướng dẫn mới](planning-pdf-limits-20261118.md); IIS cần bản sửa giới hạn trước khi nhập tệp lớn. Các số 9/23 và giới hạn 10 MiB bên dưới mô tả kiểm kê nguồn gốc ngày 17/11, không phải trạng thái của bộ PDF đã xử lý. Không có upload thật trong lượt hoàn thiện.

## Phạm vi và nguồn

- Gói được cung cấp trên máy hiện tại: `C:\website_upload\website_upload.zip` (479.749.787 byte), SHA256 `18a905ff14fbc66832bfae2a148f332843d35576bb40ee69164de3148fa93878`. Không tìm thấy một gói riêng mang tên `website_upload(1).zip`; dùng đúng tệp đính kèm, không suy đoán gói khác.
- Workbook `Tong hop link website_T11-2026_Fin.xlsx`; sheet thực tế **`2.4.2 QHKHSDD`** (dấu cách). Công cụ chấp nhận đúng tên này hoặc `2.4.2_QHKHSDD`, từ chối nếu có cả hai. Không dùng sheet `2.2 QuyhoachSDD`.
- 32 STT, dòng 7–38. A = STT; B = Ngày đăng; C = Tên file/tiêu đề nguyên văn; D = Link đăng tải; E = số trang. Có công thức E6 và E30, phải giữ nguyên.
- 33 PDF trực tiếp, tổng 6.326 trang; 32/32 STT có PDF khớp tiền tố đầu tên. STT 24 có **hai PDF** và số trang tổng trong công thức E30. Số trang trực tiếp đều khớp Excel, không cần sửa ô số trang của gói hiện tại.
- STT 21 có thêm ZIP chứa 118 mục, gồm nhiều PDF, DOCX và ZIP con; có PDF trùng hồ sơ 21/22. Ngày 17/11/2026 chủ dữ liệu yêu cầu **loại ZIP phụ lục khỏi phạm vi nhập**, giữ PDF chính. Quyết định nằm trong `source-scope.json`, ràng buộc SHA256 ZIP nguồn, tên entry chính xác và SHA256 ZIP phụ lục. Không sửa/xóa ZIP nguồn, không tự loại PDF. PDF chính STT 21 vẫn bị chặn bởi giới hạn cấu trúc độc lập của CMS.
- PDF STT **1, 26, 28, 29, 32** vượt giới hạn upload CMS **10 MiB**. Công cụ không sửa byte, nén/chia PDF, mở rộng giới hạn hoặc gọi endpoint khác để vượt kiểm soát. Các hồ sơ này chờ phương án được duyệt riêng.
- Kiểm định cấu trúc nghiêm ngặt của CMS chặn PDF tại **2, 3, 4, 5, 6, 12, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24, 25, 30**: lỗi tài nguyên `/Gs1`, hành động/biểu mẫu/tệp nhúng hoặc vượt giới hạn cấu trúc. STT 24 giữ cả hai PDF và bị chặn cả hồ sơ khi phụ lục không đạt; không chỉ nhập PDF chính. Đây là kết quả kiểm định kỹ thuật, không kết luận nhiễm virus. Chi tiết nằm trong báo cáo, không tự sửa PDF hoặc bộ kiểm định.
- Tính toàn vẹn/cấu trúc/số trang đọc được không đồng nghĩa đã kiểm định an toàn để công khai. CMS vẫn quyết định quyền và tính khả dụng tệp; không giả `Clean`.

## Ngày đăng

Chỉ đọc cột B. **32/32 ngày đã chuẩn hóa**: 20 ngày text đọc theo `dd/mm/yyyy`; chủ dữ liệu chọn **giữ ngày lịch Excel** cho 11 ô số (xác nhận STT 1 = 01/06/2026), và xác nhận STT 31 `7//12/2026` là **07/12/2026**. Không đảo ngày/tháng, không suy từ tiêu đề/PDF và không dùng ngày hiện tại. Báo cáo vẫn giữ raw value, kiểu ô, định dạng locale và cả hai ứng viên để đối soát.

| STT | Ngày đã xác nhận (dd/mm/yyyy) | Giá trị CMS (ISO) |
|---|---|---|
| 1 | 01/06/2026 | 2026-06-01 |
| 3 | 02/04/2026 | 2026-04-02 |
| 5 | 03/04/2026 | 2026-04-03 |
| 8 | 04/06/2026 | 2026-06-04 |
| 11 | 05/04/2026 | 2026-04-05 |
| 14 | 06/03/2026 | 2026-03-06 |
| 19 | 08/05/2026 | 2026-05-08 |
| 21 | 09/07/2026 | 2026-07-09 |
| 24 | 10/08/2026 | 2026-08-10 |
| 27 | 11/05/2026 | 2026-05-11 |
| 30 | 12/02/2026 | 2026-02-12 |
| 31 | 07/12/2026 | 2026-12-07 |

Xác nhận thực tế đã được ghi trong `date-decisions.json` đi kèm gói bàn giao riêng; `ConfirmedDate` dùng ISO `yyyy-MM-dd`. Tệp ràng buộc **hash ZIP + hash workbook + tên sheet**; không dùng xác nhận cũ của 2.4.1 dù workbook giống nhau. Inspect/Report tự đọc hai tệp xác nhận đi kèm CLI, kiểm tra hash, rồi giữ bản sao trong thư mục kết quả mới. Không hardcode quyết định vào binary hoặc coi nguồn mới là đã duyệt. Khi tiếp tục Import/Reconcile/SyncLinks, chỉ đọc quyết định đã giữ trong thư mục kết quả, không thay bằng tệp bên cạnh CLI. Dry-run gắn ngày và hash phạm vi nguồn vào plan; thay nguồn/phạm vi hoặc ngày của dòng nhập sau plan sẽ bị chặn. Ngày được lưu ngay khi tạo nháp vào `Metadata.PublicationDate`; không sửa thời điểm công bố/audit.

## Menu có sẵn, không tạo trùng

Trang `/quy-hoach-su-dung-dat`, route CMS **LandPlanning**, nhóm nội dung **77** đã tồn tại. Nhóm Tin tức **48** độc lập và giữ nguyên. Không tạo category/page mới.

Với website dùng cấu hình CMS, cán bộ có quyền cấu hình mở **`/admin/site?section=menus`**, tìm mục hệ thống LandPlanning hiện có (có thể ở dải phụ), **sửa chính mục đó**: tên “Quy hoạch, kế hoạch sử dụng đất”, vị trí menu chính, nhóm cha “Văn bản”, sau Văn bản dự thảo. Chỉ thêm mục route này nếu chưa có; không chọn chuyên mục Tin tức 48. Lưu nháp → xem trước → công bố cấu hình theo quyền. Không tự áp dụng cấu hình lúc startup hoặc trong importer. Menu fallback trong source đã có LandPlanning dưới Văn bản; nhãn trang đầy đủ được bổ sung, không đổi URL.

## Cách chạy trên Developer

Publish công cụ độc lập (không Publish/Deploy website):

```powershell
dotnet publish tools/WeeklyScheduleImport/WeeklyScheduleImport.csproj -c Release -r win-x64 --self-contained true -o .local/handoff/GDLA-QHKHSDD-WEB
```

Trong thư mục công cụ, dùng **một thư mục kết quả riêng**, không phải `weekly43-web`:

```powershell
$zip = 'C:\website_upload\website_upload.zip'
$out = Join-Path $env:USERPROFILE 'LandPortal-Private\qhkhsdd32-web'
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action Inspect -Zip $zip -Output $out
# Gói ngày 17/11 đã kèm xác nhận thật; đọc BAO-CAO-QHKHSDD.md trước DryRun.
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action Preflight -Zip $zip -Output $out
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action DryRun -Zip $zip -Output $out
# Chỉ sau khi được phép thực thi: STT 7 có ngày text rõ và PDF đạt tiền kiểm.
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action Import -Zip $zip -Output $out -Stt 7
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action Reconcile -Zip $zip -Output $out
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action Import -Zip $zip -Output $out -All
# Cán bộ tự gửi duyệt/duyệt/xuất bản trên CMS. Sau đó:
powershell -NoProfile -File .\NHAP-QHKHSDD-WEB.ps1 -Action SyncLinks -Zip $zip -Output $out
```

Mỗi lệnh CMS nhập tài khoản, mật khẩu ẩn và CAPTCHA ở console. Không lưu password/cookie/CSRF. Không nhận credential qua tham số. Preflight chỉ kiểm tra quyền/form hiện có; upload thử mới chứng minh kho tệp/chính sách hoạt động.

`-All` chỉ xử lý các dòng Create/Reuse trong plan; không tự cho qua dòng Blocked. Cần một pilot được đối soát trước batch. Với ngày đã được xác nhận, **9 hồ sơ đạt tiền kiểm nguồn: 7, 8, 9, 10, 11, 13, 22, 27, 31**; **23 hồ sơ còn chặn tệp**. Khả năng nhập thật còn phụ thuộc tài khoản, CMS và kiểm định tại đích. Không được coi cả 32 hồ sơ đã sẵn sàng.

Gián đoạn: giữ nguyên ZIP, plan, nhật ký, chạy Reconcile rồi Import cùng STT hoặc `-All`. Lệnh lưu dùng command ID, rowversion gốc; upload tra receipt theo SHA trước gửi lại. Nháp đã bị cán bộ sửa, tệp bị gỡ, ngày/host/user/nguồn khác đều dừng, không ghi đè. Kế hoạch không bị sửa sau Import; khi giải quyết các dòng bị chặn, tạo đợt Inspect/DryRun mới và công cụ đối soát tái sử dụng bài đã có, không tạo bản sao.

SyncLinks chỉ điền D sau khi xác minh **published revision, tiêu đề, ngày công khai đúng B, đủ từng attachment và byte/hash tải công khai**; kiểm lại revision trước xuất Excel. Link nháp/thu hồi/sai ngày để trống. Bản sao giữ A/B/C, công thức và tất cả sheet khác. Importer không tự duyệt/xuất bản, không tạo WeeklyScheduleRelease, không sửa hồ sơ đã công khai để khớp plan.

## Kiểm thử tập trung

Sau xác nhận ngày và loại ZIP phụ lục: **Release Build PASS (0 lỗi/cảnh báo), 18/18 kiểm tra offline bổ sung PASS**. Đã kiểm tra đủ 32 ngày, giữ nguyên 20 ô text, 11 serial dùng ngày lịch đã lưu, STT31 dùng ngày xác nhận; loại đúng một ZIP theo tên/hash; vẫn chặn PDF không hợp lệ; plan/nhật ký không được đổi phạm vi nguồn; nguồn ZIP/Excel giữ nguyên. Không chạy lại 42 kiểm tra HTTP/SQL trước đó, không đăng nhập CMS thật. Có thể chạy riêng bằng `--planning-confirmations` với đường dẫn ZIP và thư mục CLI chứa hai tệp xác nhận thật; chế độ này không gửi HTTP hay mở SQL.

Kết quả ngày 17/11/2026: **Release Build PASS, 42/42 kiểm tra tập trung PASS** trong lần chạy cuối `weekly-web-planning-20261117-04`. Bản CLI win-x64 đã publish và chạy kiểm kê offline trên đúng ZIP nguồn. Chưa đăng nhập hoặc nhập dữ liệu lên gdla.gov.vn. Kiểm thử có bước duyệt/công bố bằng service với tài khoản giả trong SQL Test để xác minh trang công khai và SyncLinks; không phải thử workflow trên IIS thật.

Chế độ `--planning` dùng một LocalDB/SQL Test mới, schema18, tài khoản giả và HTTP loopback. Chỉ thực hiện **một hồ sơ thử có nhãn TEST ONLY**, ghép hai PDF nguồn đã đạt kiểm định (7/9) trong fixture riêng để kiểm tra nhiều attachment. Đây không phải STT24 nhập thành công; STT24 thật vẫn bị chặn. Kiểm tra ngày, upload/attach, mất phản hồi/resume, không trùng, thiếu quyền, chống ghi đè, SyncLinks sau quy trình duyệt của fixture, bảo toàn workbook và route/menu. Không chạy lại suite 2.4.1, không truy cập CMS/SQL vận hành.

```powershell
dotnet build tests/LandPortal.WeeklyWebTests/LandPortal.WeeklyWebTests.csproj -c Release
dotnet tests/LandPortal.WeeklyWebTests/bin/Release/net10.0/LandPortal.WeeklyWebTests.dll C:\website_upload\website_upload.zip .local/tests/weekly-web-planning-NEW --planning
```
