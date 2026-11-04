# Tra cứu giấy chứng nhận sai hỏng, hủy

URL: `/cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy`; chi tiết thêm `/{id:guid}`.
Chỉ thay đổi repository website. Không API mới ở nguồn, không EF migration/import, không sao chép database.

## Kết nối và quyền

Giữ nguyên `ConnectionStrings:Portal`/`Runtime:RolePassword`. Bổ sung **riêng** vào private JSON ngoài webroot, không commit giá trị thật:

```json
{
  "ConnectionStrings": {
    "GiayHuyReadOnly": "Server=45.120.229.43,1433;Database=QuanLyDuAnTrungTamDuLieuDatDai;User ID=<SELECT_ONLY_LOGIN>;Password=<PRIVATE>;Encrypt=True;TrustServerCertificate=False"
  },
  "GiayHuy": {
    "ExpectedLogin": "<SELECT_ONLY_LOGIN>",
    "Publication": { "ApprovalReference": "", "ExpiresUtc": "", "Records": [] }
  }
}
```

Đây là phần cần **merge**, không ghi đè private JSON hiện có. PortalConfiguration đọc cấu hình ngoài qua `LANDPORTAL_CONFIG` theo cơ chế triển khai hiện hành. Web và worker không dùng `sa`/tài khoản website để đọc nguồn; tính năng này chỉ đăng ký ở Web.

Chưa có login SELECT riêng được xác nhận. DBA phải duyệt riêng việc provision login/user; developer chưa tạo/cấp quyền. Không cấp role quản trị, quyền ghi, EXECUTE hoặc SELECT toàn database. Danh sách cột tối thiểu để DBA lập script quyền sau phê duyệt:

| Bảng dbo | Cột SELECT |
|---|---|
| GiayChungNhanHuy | giayChungNhanHuyID, namPhatHanh, donViHanhChinh, soQuyetDinh, soHieuGiayChungNhan, noiDungQuyetDinh, nguonDuLieuDoiSoatID, hieuLuc, isDel |
| GiayChungNhanHuyGiaTriChot | giaTriChotID, giayChungNhanHuyID, fieldKey, giaTri, hieuLuc, isDel |
| GiayChungNhanHuyDuLieuDoiSoat | duLieuDoiSoatID, giayChungNhanHuyID, taiLieuID, trangThaiXacNhan, thoiGianXacNhan, hieuLuc, isDel |
| GiayChungNhanHuyTaiLieu | taiLieuID, trangThaiDoiSoat, hieuLuc, isDel |

Mẫu cú pháp để DBA xem xét: `GRANT SELECT (cột_đã_duyệt) ON OBJECT::dbo.bảng_đã_duyệt TO [user_đã_provision];`. Không tự chạy mẫu này. Quyền cột trên `giaTri` không giới hạn từng fieldKey; nếu chính sách nguồn yêu cầu ngăn cả tài khoản đọc các trường khác trong bảng giá trị chốt, cần DBA duyệt một view/projection riêng trước khi bật. Website không tự tạo view hoặc sửa nguồn.

Runtime kiểm tra physical server `WIN-S1IBJB84OOO`, đúng source database/login, cấm sa/site-app, sysadmin/db_owner/db_ddladmin/db_datawriter, quyền ghi/CONTROL/ALTER và SELECT toàn database; kiểm tra SELECT từng cột cần dùng. Kết nối timeout 4s, lệnh 4/6s, tổng ngân sách 12s, tối đa 2 kết nối, không retry. Endpoint giới hạn 30 request/phút/IP theo policy hiện có. SQL chỉ dùng tham số `@login`, `@approved`; không đưa keyword vào SQL động.

## Quyết định công bố độc lập

Chưa có phê duyệt công bố ⇒ 0 hồ sơ thật cho khách, kể cả đã cấu hình SQL. Danh sách cho phép private cần người có thẩm quyền phê duyệt rõ từng ID và các trường được công bố, có tham chiếu văn bản và hạn hiệu lực. Không dùng trạng thái DA_XAC_NHAN làm phê duyệt công khai.

Mỗi entry `Records` gồm `Id`, `Sha256`, `IncludeDecisionContent` (mặc định false). Hash là SHA-256 UTF-8 của `JsonSerializer.Serialize(GiayHuyPublicRow)` theo thứ tự Id/Year/Province/DecisionNumber/CertificateSerial/DecisionContent; giá trị Year/Province đã Trim nhưng **trước** chuẩn hóa tên tỉnh, các trường còn lại giữ nguyên. Dùng `GiayHuyLookup.PublicationHash` khi lập hồ sơ trình duyệt private. Hash phải được duyệt cùng projection, không tự sinh approval từ dữ liệu vừa đọc. Nội dung quyết định có thể chứa PII nên chỉ bật riêng sau review trường này. Không có PDF/download nguồn.

Hash không khớp sau thay đổi nguồn ⇒ loại hồ sơ khỏi mọi kết quả/chi tiết/bộ lọc cho đến khi duyệt lại. ID không công khai và ID không tồn tại cùng trả 404. Không ghi SQL exception message, credential, hash dữ liệu thật hoặc nội dung hồ sơ vào log.

## Quy tắc truy vấn

Đã đối chiếu local checkout đúng remote `akayuhuhu0503/doisoatgiayhuy`: GiayChungNhanHuyRepository, TruongConThieu, KetQuaCuoiCung, ChiTiet và service phân nhóm. Chỉ đọc g còn hiệu lực/chưa xóa; lấy đúng bản d DA_XAC_NHAN còn hiệu lực/chưa xóa và liên kết nguồn, tài liệu t còn hiệu lực/chưa xóa/không LOAI_BO. Có giá trị chốt thì dùng giá trị chốt **kể cả rỗng**, không fallback dữ liệu cũ.

Năm dùng `NamPhatHanh` hiệu lực; không suy đoán lại từ ngày khi cán bộ đã chốt rỗng. Năm lọc được chỉ gồm giá trị nguyên thực tế; bản năm chưa xác định còn xem được ở Tất cả. Nguồn nội bộ có bộ lọc riêng theo YEAR(ngày quyết định); website không trộn hai khái niệm. DVHC dùng tên/alias chính xác của service nguồn, bổ sung tiền tố Tỉnh/Thành phố để trình bày, không nhập các tỉnh lịch sử sau sáp nhập. Giá trị lạ vào Chưa xác định. Khi đổi Năm thì cập nhật DVHC và về trang 1.

Đọc tối đa 1000 ID được duyệt, TOP 1001 để phát hiện sai lệch/duplicate lớp chốt; không tải cả bảng nguồn. Giới hạn kích thước projection: năm10, tỉnh200, số quyết định/GCN500, nội dung16000 ký tự UTF-16; vượt giới hạn thì loại, không cắt sửa dữ liệu. Lọc/tìm kiếm/chuẩn hóa và chia trang 10 dòng chạy phía server trên tập public giới hạn này, không phân trang trong browser. Chưa benchmark SQL nguồn; nếu phạm vi duyệt lớn hơn 1000, giữ chặn và thiết kế paging/index theo plan thực trước khi mở rộng, không tự tăng giới hạn.

## Khảo sát và kiểm tra

`scripts/inspect-giay-huy-source.ps1 -OutputPath <tệp-private-mới.json>` nhập DBA bằng Get-Credential, chỉ đọc đích/metadata/count, không lưu credential. Hoặc DBA chạy `scripts/inspect-giay-huy-source.sql` trong SSMS đúng database. Không tạo login/GRANT hoặc thao tác ghi trong hai script này.

Ngày 04/11: phiên đầu dừng ở PS5 dictionary-property adapter trước kết nối; đã sửa sang canonical SQL keywords và tái hiện offline. Phiên thứ hai bị chặn khi mở kết nối TLS. Probe PRELOGIN/TLS không credential xác nhận `RemoteCertificateNameMismatch`, `UntrustedRoot`; không LOGIN7/SQL, không bypass. Ngoại lệ TLS website UAT cũ **không** tự áp dụng cho source. Cần cấu hình trust/name hợp lệ hoặc quyết định riêng cho kết nối nguồn; chưa có số lượng chính thức được kiểm chứng qua SQL.

Build/check kỹ thuật (không SQL):

```powershell
rtk proxy .local\dotnet\dotnet.exe build tools\GiayHuyLookupCheck\GiayHuyLookupCheck.csproj -c Release --artifacts-path .local\build\giay-huy-20261104 -p:IisHandoffPublish=true -p:DebugType=None -p:DebugSymbols=false
rtk proxy .local\dotnet\dotnet.exe .local\build\giay-huy-20261104\bin\GiayHuyLookupCheck\release\GiayHuyLookupCheck.dll --check
rtk proxy .local\dotnet\dotnet.exe .local\build\giay-huy-20261104\bin\GiayHuyLookupCheck\release\GiayHuyLookupCheck.dll
```

Lệnh cuối mở **loopback** `http://127.0.0.1:5192/`, ghi nhãn fixture, không truy cập database/Identity/SMTP và không phải link LAN dùng chung. Stop bằng Ctrl+C ở terminal đã khởi động. 13/13 focused checks; browser kiểm tra bảng/bộ lọc/chi tiết/404/400/trạng thái lỗi/rỗng, desktop và viewport mobile390px; không chụp ảnh/full regression.

LAN 5188 giữ binary schema15 hiện có và dữ liệu/tài khoản. Full source Web mới có mapping FR06 schema17, nên chưa thay DLL LAN hoặc áp migration chỉ để đưa tra cứu lên LAN. Bản tra cứu mới kiểm tra trên 5192; chưa tuyên bố PASS tích hợp SQL thật/PASS LAN cho chức năng này.
