# Lọc danh mục trong Tài liệu

`/tai-lieu?categoryId=<id>` thu hẹp danh sách trong phạm vi menu Materials đang công khai. Không thay phạm vi bằng danh mục được yêu cầu. Truy vấn vẫn kiểm tra published revision, quyền công khai và tài liệu đính kèm đủ điều kiện.

Danh sách chọn lấy tên/thứ tự từ cấu hình CMS công khai: danh mục chính/phụ có hồ sơ hợp lệ trong Tài liệu, cùng các danh mục con được cấu hình dưới phạm vi Tài liệu. Danh mục ẩn, danh mục chỉ có trong cấu hình nháp và danh mục không liên quan không được đưa vào bộ lọc. Danh mục không hợp lệ trả 404. Tìm kiếm và phân trang giữ bộ lọc hiện tại.

Để bổ sung Hủy phôi GCN qua CMS sau khi bản sửa được duyệt:

1. Đối soát cấu hình nháp với bản công khai trước khi áp dụng, tránh công bố thay đổi không liên quan.
2. Tạo danh mục Hủy phôi GCN dưới danh mục Tài liệu đang cấu hình; không tự suy đoán ID mới.
3. Hồ sơ dùng Hủy phôi GCN làm chuyên mục chính, đồng thời gắn chuyên mục phụ là phạm vi Materials hiện hành. Quan hệ cha/con không tự mở rộng truy vấn công khai.
4. Giữ ngày hồ sơ trong Metadata.PublicationDate; thời điểm công bố thật và audit không đổi. Giao diện ghi “Ngày hồ sơ” để phân biệt hai khái niệm.
5. Chỉ duyệt/công bố/hẹn giờ revision có đủ tài liệu đã kiểm định. PDF/ZIP chưa hợp lệ không được đưa vào danh sách công khai.

Thay đổi này không tạo dữ liệu CMS hoặc tự khởi tạo danh mục. Không đổi SQL schema, cấu hình runtime, workflow hoặc chính sách tệp. Hỗ trợ ZIP là phạm vi PR #24 riêng; bộ lọc này không thay thế việc kiểm định/quét ZIP và không cho ZIP dùng ngoại lệ no-AV PDF/Office.

## Kiểm thử tập trung

Chạy từ checkout với SDK của dự án:

```powershell
dotnet run --project tests/LandPortal.PublicFunctionsTests -c Release -- materials-filter $PWD.Path "$($PWD.Path)\.local\tests\materials-$([Guid]::NewGuid().ToString('N'))"
```

Chỉ tạo một SQL LocalDB mới có tên ngẫu nhiên và database fixture riêng. Thư mục kết quả phải mới và nằm dưới `.local/tests/materials-*`. Schema 18 chỉ được tạo trong fixture; không đọc `.env`/cấu hình SQL vận hành. LocalDB dừng khi hoàn tất; dữ liệu fixture được giữ trong thư mục kết quả. Nhãn Clean trong fixture chỉ phục vụ kiểm tra truy vấn, không phải kết quả quét tệp thật.

Kết quả: 12 kiểm tra PASS, gồm cô lập database/schema, giữ phạm vi Tài liệu/QHKHSDD, lọc chính/phụ, tìm kiếm, danh mục rỗng, từ chối danh mục ẩn/không liên quan và giữ ngày metadata. Fixture CSS trên Chrome tại 1366/768/390/320px không tràn ngang, chọn danh mục hoạt động; không thay cho nghiệm thu HTTP/Razor trên IIS. Release Web/Worker PASS, 0 lỗi/cảnh báo. Chưa triển khai hoặc nghiệm thu HTTP trên IIS.
