# Danh mục nguồn và bằng chứng — 12/10/2026

Nguồn nội dung/cấu trúc duy nhất: `https://qldd.mae.gov.vn/`. `http://gdla.gov.vn/` timeout lúc khảo sát; không nhận xét giao diện và không nhập nội dung từ đó. Đây là mẫu Giai đoạn 0–1; tổng corpus chưa xác định.

Raw HTML nằm trong `.local/snapshots/survey/` và `.local/snapshots/html/`. Manifest schema 1 `.local/snapshots/sample-manifest.json` giữ URL gốc/cuối, HTTP status, thời điểm UTC, SHA-256, byte length, nội dung/tài nguyên/lỗi/checkpoint. Không phục vụ snapshot như HTML chủ động. `/robots.txt` thực trả HTTP 404 và HTML “Thông báo từ chối truy cập”; đã giữ bytes gốc, không suy diễn thành robots cho phép toàn bộ. Crawler một kết nối, khoảng nghỉ >=2 giây có jitter, ngân sách hữu hạn, kết thúc mẫu `PausedByBudget`; không đăng nhập, gửi biểu mẫu nguồn, quét ID tuần tự hoặc tuyên bố toàn nguồn hoàn tất.

| Vị trí | Hạng mục | URL/ID nguồn | Adapter/bằng chứng | Phạm vi |
|---|---|---|---|---|
| Menu | Trang chủ | `/` | `survey/home.html`, menu desktop/mobile/khối/footer | Trang mới từ SQL, không tái phục vụ HTML nguồn |
| Giới thiệu | Lãnh đạo cục | `/lanh-dao-cuc-1780.htm` | `h1.tieu-de-tin-tuc`, `article.noi-dung-chi-tiet-tin`, canonical ItemID1780 | Nội dung/ảnh thật |
| Giới thiệu | Chức năng nhiệm vụ | `/chuc-nang-nhiem-vu-3049.htm` | Body khoảng trắng; `#dinhkem` có `115-QĐ-BNNMT.pdf` | PDF gốc, không tự viết pháp lý |
| Giới thiệu | Cơ cấu tổ chức | `href="#"` | Menu gốc không có trang đích | Giữ mục, báo chưa có nguồn xác minh |
| Menu | Tin tức | 14 chuyên mục trong bảng dưới | `#KhuVuc_DanhSachTin`; category từ list và back-link bài | Giữ cả mục chưa có bài tiếp cận được |
| Khối trang chủ | Thông báo | `/chuyen-muc/thong-bao-52.htm` | Article adapter + `#dinhkem` | Announcement riêng, chấp nhận bài chỉ PDF |
| Khối trang chủ | Văn bản quy phạm pháp luật | `/danh-sach-van-ban.htm`, `/van-ban-{ID}.htm` | tbody rỗng, JSON công khai `odata.data`, Kendo render DOM | Metadata có kiểu và PDF, tra cứu riêng |
| Khối trang chủ | Văn bản dự thảo | `/danh-sach-van-ban-du-thao.htm`, `/chi-tiet-van-ban-du-thao-{ID}.htm` | JSON `odata.data`, table `.contentvbdh` | Giữ ngày bắt đầu/hết hạn/PDF, góp ý ở GĐ2 |
| Khối trang chủ | Video | `/video.htm`, `/video-41.htm`, `/video-40.htm` | JSON `lisstData.data` thật | Metadata/poster; không gọi là video đã chuyển |
| Menu | Sổ tay điện tử đất đai | `/chuyen-muc/so-tay-dien-tu-79.htm`, bài3318 | Chuyên mục/bài hướng dẫn thật | Nhập bài/tài nguyên, giữ link ngoài có chủ đích |
| Menu | Hỏi đáp | `/lien-he.htm` | Cùng đích Liên hệ trên menu gốc | Giữ hạng mục, tiếp nhận/kiểm duyệt ở GĐ2 |
| Menu | Liên hệ | `/lien-he.htm` | Trang và footer thật | Thông tin cơ quan đã xác minh, chưa gửi thư |
| Banner bổ sung | Văn bản chỉ đạo điều hành | `/van-ban-chi-dao-dieu-hanh.htm` | URL/nhãn banner nguồn | Mục riêng, adapter chưa có |
| Banner bổ sung | Quy hoạch sử dụng đất | `/chuyen-muc/quy-hoach-su-dung-dat-77.htm` | URL/nhãn banner nguồn | Category77 riêng, không gộp48 |
| Banner bổ sung | Lịch tuần | `/chuyen-muc/site-lich-tuan-76.htm` | URL/nhãn banner nguồn | Category76 riêng |
| Trang chủ | Tin nổi bật/tin đọc nhiều/banner/liên kết/thống kê | `home.html` | Snapshot nhóm liên kết | Không lấy sidebar làm quan hệ category, không seed lượt truy cập giả |

## 14 chuyên mục Tin tức

| ID | Tên menu gốc | URL nguồn | Slug tuyến mới `/chuyen-muc/{slug}` |
|---|---|---|---|
|75|Xây dựng, phổ biến chính sách, pháp luật|`/chuyen-muc/site-xay-dung-pho-bien-chinh-sach-phap-luat-75.htm`|xay-dung-pho-bien-chinh-sach-phap-luat|
|83|Đo đạc và Đăng ký đất đai|`/chuyen-muc/site-do-dac-va-dang-ky-dat-dai-83.htm`|do-dac-va-dang-ky-dat-dai|
|82|Dữ liệu và Thông tin đất đai|`/chuyen-muc/site-co-so-du-lieu-quoc-gia-ve-dat-dai-82.htm`|du-lieu-va-thong-tin-dat-dai|
|81|Thống kê, kiểm kê đất đai|`/chuyen-muc/site-thong-ke-kiem-ke-dat-dai-81.htm`|thong-ke-kiem-ke-dat-dai|
|55|Điều tra, đánh giá tài nguyên đất|`/chuyen-muc/dieu-tra-danh-gia-tai-nguyen-dat-55.htm`|dieu-tra-danh-gia-tai-nguyen-dat|
|49|Giao đất, cho thuê đất, chuyển mục đích sử dụng đất|`/chuyen-muc/giao-dat-cho-thue-dat-chuyen-muc-dich-su-dung-dat-49.htm`|giao-dat-cho-thue-dat-chuyen-muc-dich-su-dung-dat|
|48|Quy hoạch, kế hoạch sử dụng đất|`/chuyen-muc/quy-hoach-ke-hoach-su-dung-dat-48.htm`|quy-hoach-ke-hoach-su-dung-dat|
|53|Tin tức hoạt động|`/chuyen-muc/tin-tuc-hoat-dong-53.htm`|tin-tuc-hoat-dong|
|51|Phát triển tài nguyên đất|`/chuyen-muc/phat-trien-tai-nguyen-dat-51.htm`|phat-trien-tai-nguyen-dat|
|47|Tin hoạt động địa phương|`/chuyen-muc/tin-hoat-dong-dia-phuong-47.htm`|tin-hoat-dong-dia-phuong|
|50|Giá đất|`/chuyen-muc/gia-dat-50.htm`|gia-dat|
|57|Bồi thường, hỗ trợ, tái định cư|`/chuyen-muc/boi-thuong-ho-tro-tai-dinh-cu-57.htm`|boi-thuong-ho-tro-tai-dinh-cu|
|74|Thanh tra, kiểm tra đất đai|`/chuyen-muc/thanh-tra-kiem-tra-dat-dai-74.htm`|thanh-tra-kiem-tra-dat-dai|
|56|Công tác hoạt động Đảng, Đoàn thể|`/chuyen-muc/cong-tac-hoat-dong-dang-doan-the-56.htm`|cong-tac-hoat-dong-dang-doan-the|

Category51 có alias homepage `/chuyen-muc/thu-hoi-boi-thuong-ho-tro-tai-dinh-cu-ve---dat-dai-51.htm`; giữ tên menu. SourceRecord unique `(Source,Kind,SourceId)`; cùng ID bài qua nhiều category là một nội dung có nhiều quan hệ. Không gộp vì giống tiêu đề. Category83/81 không có bài trong HTML khảo sát; browser/network kiểm tra riêng, không suy diễn tổng corpus bằng0.

## Phân trang và JavaScript

Category75 source script có totalPages4 và next-path `/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat-75-p{page}.htm`. Đã tải trang1/2/4: trang1 có10ID đầu3678,3677,3673; trang2 có10ID đầu3623,3625,3626; trang4 có4ID đầu2152,3298,3297. Trang cuối tự khai báo4/4, tập khác trang đầu. Không đoán schema trang hoặc thử ID tuần tự.

Legal list dùng `?Page={page}`, filters trên URL, totalPages10 và JSON recordsTotal136/15bản đầu. Đã tải trang2; parser lấy ID từ JSON `odata.data`, không lấy tbody rỗng. Chrome render15ID đầu251,250,231,236,233,237,230,229,232,226,225,224,238,239,240. Không quan sát thấy XHR nội dung riêng; dữ liệu đã có trong HTML. Dự thảo source khai báo1trang; video2metadata/1trang. Báo cáo không coi recordsTotal của riêng một loại là tổng corpus. Browser evidence tại `docs/evidence/source-browser.json`.

## Host tài nguyên và ngoại lệ

Page crawl chỉ `qldd.mae.gov.vn`. Media allowlist `qldd.mae.gov.vn`, `media-qldd.mae.gov.vn`; mở thêm `media.mae.gov.vn`, `i.ex-cdn.com` sau xác minh body snapshot nguồn. Ví dụ bài3528 dùng `media.mae.gov.vn/.../pho-cuc-truong-cuc-quan-ly-dat-dai-mai-van-phan...jpg`; bài3683 dùng `i.ex-cdn.com/nongnghiepmoitruong.vn/files/content/2026/10/03/...jpg`. Không crawl bài/trang ngoài. Mọi redirect được kiểm tra host/scheme/port, DNS/IP lúc kết nối được pin socket địa chỉ công khai; chặn private/loopback/link-local/metadata, giới hạn response sau giải nén và thời gian. File sniff chữ ký; HTML lỗi/SVG từ chối. Không hotlink: body mới dùng `/media/{id}`, lỗi giữ nguyên báo cáo/bytes riêng.

Video41 chỉ có `http://gdla.gov.vn/uploads/videoclips/video/90_NGAY_DAT_DAI_V5_full.mp4` ngoài phạm vi; không tải/hotlink. Video40 `ContentNews` rỗng. Poster không được gọi là tệp video đã chuyển. Uploadvideo mới từ máy biên tập viên thuộc GĐ2, không có player giả.

Banner xác minh: `/noidung/Lists/Banner/Attachments/12/Banner_header_cuc_QLdatdai.png`; generic image `/Content/publishing_home/img_moj/logo.png`. Footer logo `https://qhpttnd.vn/assets/images/monre-logo.png` ngoài allowlist; không hotlink/tự tạo biểu trưng. Liên kết cơ quan giữ có chủ đích: `nongnghiepmoitruong.vn`, `ppd.gov.vn`, `vnforest.gov.vn`, `dinre.mae.gov.vn`, `dwrm.monre.gov.vn`; không nhập corpus host ngoài.

Footer nguồn: CỤC QUẢN LÝ ĐẤT ĐAI; Số10 Tôn Thất Thuyết, Phường Cầu Giấy, Hà Nội; điện thoại024.3629.0211; đường dây nóng024.3629.0196; vanphongcqldd@mae.gov.vn. Chưa gửi email thật; thống kê nguồn là snapshot lịch sử riêng.

## Báo cáo/tiếp tục

`docs/evidence/source-report.json`, `source-content-report.csv` tách phát hiện URL/nội dung mẫu/tài nguyên. `import-report.json`, `verify-report.json` ghi SQL/hash/reference thật. Toàn corpus, retry dài hạn, alias file trực tiếp, durable jobs toàn bộ, backup/restore, cutover vận hành là việc tiếp theo; mẫu không phải chuyển giao toàn dữ liệu.

Checkpoint kiểm chứng thực tế: 203 URL nội dung đã phát hiện từ 24 trang danh sách khảo sát, 192 cặp loại/mã URL ứng viên (chưa coi mọi ứng viên là mã nguồn đã xác minh). Đã nhập và xác minh 37 nội dung: 26 bài, 2 thông báo, 2 trang giới thiệu, 3 văn bản, 2 dự thảo, 2 metadata video. Chuyên mục 83/81 được giữ nhưng trang nguồn khảo sát không trả bài ngay cả sau JavaScript; không nói toàn lịch sử bằng 0.

Kiểm chứng độc lập định danh đã nhập tại `docs/evidence/source-identity-report.json`: lấy lại `<link rel="canonical">` trực tiếp từ từng raw snapshot, giải mã query ItemID rồi so với SourceRecord của manifest. Cả 37 mã trùng bằng chứng canonical. Mã đuôi URL của nội dung mới phát hiện vẫn là ứng viên; ví dụ URL kết thúc `nam-2024.htm` thiếu tiêu đề/canonical nội dung hợp lệ đã bị ghi lỗi và không nhập thành một bài ID2024.

Đã tải 55 tệp duy nhất, 36.267.438 byte: 53 tệp gắn nội dung (41 ảnh và 12 PDF) trong SQL, 2 banner nhận diện riêng. Mẫu có 55 URL tài nguyên nội dung dự kiến, 53 đã chuyển; 1 ảnh và 1 PDF còn phản hồi không đạt kiểm tra chữ ký. Hai video chưa có tệp chuyển được. 120 request crawler được ghi trong checkpoint, ngân sách toàn CrawlRun 350, 70 snapshot gồm phản hồi lỗi riêng; trạng thái PausedByBudget của mẫu, tổng nguồn chưa biết.

RequestsUsed120 là bộ đếm HTTP của MigrationCli trong CrawlRun, không phải tổng mọi truy cập khảo sát. Snapshot khảo sát ban đầu được tái sử dụng thay vì tải lại; danh mục các request khảo sát riêng nằm trong `.local/snapshots/survey/survey-manifest.json`. Các lần đọc DOM/network bằng trình duyệt được ghi tại `source-browser.json` và không cộng giả vào bộ đếm crawler. Không chạy thêm HTTP khi chuẩn hóa lại và nhập/xác minh SQL.

33 dòng lịch sử ngoại lệ gồm 23 đã giải quyết và 10 còn mở, tương ứng 8 ngoại lệ riêng biệt: 2 URL nội dung thiếu tiêu đề xác minh, 2 tài nguyên chưa đạt kiểm tra, 2 video thiếu/ngoài phạm vi, robots404 và generic logo404. Mỗi lỗi vẫn nằm trong manifest/SQL, không biến URL lỗi ra khỏi mẫu số. Banner nguồn đã tải thành công sau 404 tạm thời; generic logo riêng chưa có.

Đối soát Unicode chỉ chuẩn hóa NFC và khoảng trắng để so sánh: toàn văn 37/37 bản nhập khớp raw snapshot, không sửa nội dung hiển thị. Công cụ phát hiện 8 thân bài từng bị sanitizer loại wrapper kèm children; đã sửa bằng giữ children an toàn sau loại phần tử chủ động, chuẩn hóa lại từ snapshot không HTTP và lưu 8 revision nguồn sửa cùng 2 revision metadata ngày video. Revision cũ được giữ. Bản cuối có 37 nội dung, 47 revision, 74 alias, 53 MediaAsset; nhập lại 0 nội dung/revision mới và 37 bản không thay đổi. `import-first-report.json`, `import-normalization-report.json`, `import-rerun-report.json` lưu bằng chứng riêng; verify bản cuối bao gồm so sánh toàn văn, hash file/snapshot và quan hệ, không chỉ hash metadata.

Ngày chỉ có phần ngày không được thêm giờ công bố. Legal giữ ngày ban hành có kiểu riêng, không giả ngày đăng. Video giữ CreatedDate nguồn trong metadata và dùng ngày hiển thị thật trên trang thay vì coi timestamp tạo record là ngày công bố. Các timestamp nguồn có giờ được giả định múi giờ Việt Nam (+07:00) khi không có offset; chuỗi gốc/độ chính xác giữ riêng. Hạn góp ý nguồn chỉ có ngày được chuyển sang ranh giới ngày Việt Nam trong DraftDocument; quy tắc tiếp nhận và hạn vẫn chưa nghiệm thu Giai đoạn 2.

Lệnh đã chạy thực tế: `.local/dotnet/dotnet.exe run --project src/MigrationCli -- migrate`; `sample --limit 40 --request-budget 350`; `repair-assets --request-budget 350`; `normalize-offline`; `import` (lần đầu, cập nhật revision sửa, chạy lại); `verify`. Tất cả shell được chạy qua rtk. `normalize-offline` cấm HTTP fallback và chỉ dùng byte nguồn/tệp đã lưu. Kiểm tra malware chưa cấu hình, asset giữ NotScanned; đây là giới hạn an ninh cho production được ghi rõ, không gọi tệp đã sạch.
