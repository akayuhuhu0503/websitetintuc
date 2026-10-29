# UI v0.5 — kiểm tra thực trong SQL Test

Run cuối được liên kết trong [báo cáo delta](phase-v05-delta-report.md). Browser Edge headless,390/1440:18pagechecks,2thao tác mới riêng; không cộng vào fullrunner. Các trang: Contact/Inquiry/lookupgate/groupindex/groupfilter/lịchtuần/editorlịch/editorFR17/historyrelease. Có kiểm tra HTTPstatus, nhãn form, overflow,JS vàoff-origin. Đã xem ảnh thực mobileContact,desktoplịch vàpreviewlịch riêng tư; tất cả dữ liệu QA chỉ trongTest.

Hai thao tác: nhập một dòng lịch từ browser→HTTPsave→SQLdraft→privatepreview, publicarticle404; focusbànphímCAPTCHA/làmmớicâu hỏi giữ nội dung đã nhập. Bộ CMS40/typed14/accounts18/site34/scheduler10 vàFinish/Scan/Video là cácreport riêng trongfullrun, không tổng hợp thành một “fullUI suite” mới.

Giới hạn quan sát: previewlịch trong khung CMS hẹp còn ngắt dòng ngày/buổi; form14dòng có thể dài, chưa có nút thêm/xóa dòng động. Người biên tập cần cập nhật tiêu đề khi đổi tuần của bản chưa phát hành. Đây chưa là nghiệm thu thiết kế/WCAG/physicaliOS/Safari hoặc workload dữ liệu đầy đủ. MenuFR17typedsitecatalog vàWorduploadpreview chưa có; không dùng ảnhfixture để gọi các phần nàyPASS.
