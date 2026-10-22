# Phiên bản, giấy phép và kiểm tra phụ thuộc

Ngày kiểm tra: 12/10/2026. Phiên bản thực được khóa ở `global.json`, `Directory.Packages.props` và `packages.lock.json`; package chưa restore hoặc scan chưa chạy không được coi là an toàn production.

| Phụ thuộc | Phiên bản chọn | Giấy phép | Sử dụng / bằng chứng |
|---|---|---|---|
| .NET / ASP.NET Core | SDK 10.0.112; runtime 10.0.12 | MIT | [Microsoft .NET10 download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0); isolated .local/dotnet, không đổi SDK máy |
| EF Core SQL Server / Identity EF | 10.0.12 | MIT | Provider chính thức; runtime/pin tương thích, SQL Server thật |
| AngleSharp | 1.8.4 | MIT | DOM parser crawler; [project license](https://github.com/AngleSharp/AngleSharp/blob/devel/LICENSE) |
| HtmlSanitizer | 9.2.1039 | MIT | Sanitization server; [project license](https://github.com/mganss/HtmlSanitizer/blob/master/LICENSE.md) |
| Playwright (browser QA only) | 1.64.0 | Apache-2.0 | Locked tests/browser/package-lock.json; headless installed Edge in an isolated context, no system browser installation |
| Quill (Web editor) | 2.0.3 | BSD-3-Clause | Self-host; license/notices tại src/Web/THIRD_PARTY_NOTICES.md. npm advisory GHSA-v3m3-f69x-jf25 low: phải xử lý/duyệt ngoại lệ trước production |

Microsoft xác định .NET10 là LTS; trang tải hiện liệt kê 10.0.12 phát hành 08/09/2026 là bản vá ổn định. SDK10.0.112 giữ cùng feature band 10.0.1xx với SDK10.0.103 ban đầu; archive tải từ metadata chính thức và phải khớp SHA512. Không dùng preview.

Đã chạy NuGet vulnerable scan (bao gồm transitive) trên solution với feed api.nuget.org: không báo package dễ tổn thương tại thời điểm scan. npm browser QA (3 package) audit không báo lỗ hổng. Đây là kết quả feed tại thời điểm chạy, không chứng nhận an toàn production. Quill của Web có báo cáo npm riêng; một advisory low ở hàm export cần ghi ở checkpoint và xử lý trước production, dù server vẫn sanitize HTML.

Bảng trên đã kiểm tra giấy phép các phụ thuộc trực tiếp được sử dụng. Inventory giấy phép toàn bộ phụ thuộc bắc cầu/SBOM và điều kiện phân phối gói vận hành chưa hoàn thành; giữ là việc trước nghiệm thu production.

Kiểm tra lỗ hổng thực chạy bằng `dotnet list LandPortal.sln package --vulnerable --include-transitive` (lệnh thực do scripts/test.ps1 gọi qua rtk). Kết quả được cập nhật sau khi chạy; thiếu feed/audit thì ghi BLOCKED thay vì coi bằng zero. Web không gọi API getSemanticHTML được advisory nêu; HTML lưu phải sanitize ở server và public CSP vẫn áp dụng. Editor không dùng phần trả phí. FFmpeg chỉ thêm sau kiểm chứng build/license thực; không cam kết phân phối binary chưa kiểm tra.
