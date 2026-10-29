# Nguồn nội dung và domain đích v0.5

| Hệ | Vai trò đã xác định | Inventory/bằng chứng | Việc chưa được phép |
|---|---|---|---|
| qldd.mae.gov.vn SharePoint | Nguồn cấu trúc/nội dung chính, cần chuyển toàn bộ sau | Local70capture/68SQLsnapshot,37objects,53assets,74aliases; sourceID táchhost+kind+ID;8ngoạilệ | Chưa fullcoverage/quyềnexport; lượt này khôngfullcrawl, không đổiDNS/HTTP nguồn |
| gdla.gov.vn NukeViet4.5 | Domain đích mới thay website hiện hữu; đồng thời tham khảo bố cục | Nội dung backup/export vàURLinventory chưa có; nhập nội dungNukeViet là[GĐ] | NEEDS_PO + owner export/backup/inventory trước cutover; không tự lấy tất cả hoặc gộp với qldd bằngpath/ID |
| gdla.gov.vn web mới | Target publicdomain đã chốt | PublicUrlPolicy xác minh trustedconfig; canonical/OG/sitemap dùng Public:BaseUrl; local127.0.0.1 | Khôngdeploy/DNS/TLS/IIS; Web vẫn khóa startup loopback, productionhost wiring còn releasegate |
| Website hợp nhất khác | Danh sách còn[GĐ] | Chưa PO cung cấp | Khôngcrawl/import/redirect ngoài phạm vi |

Aliases hiện hữu đã lưu fullURL+SHA vàforeignkeyđúngContent, không cần migrate lại nguồn. Cùngpath ởhaihost không phải mộtalias. LocallegacyOrigin mặcđịnhqldd; `Public__LocalLegacySourceHost=gdla.gov.vn` chỉ configTest/local cho mapNukeViet khi đã có dữliệuđượcduyệt, không nhậnsourcehost từform/query tùyý. Chỉ2sourcehostsallowlist; absoluteURL chỉ trustedbase, rejectprotocol-relative/backslash/authoritykhác. UnknownoldURL404, mappedobjectphảipublished; khôngredirecttrangchủ/openredirect.

Mẫu37nội dung không đủ AC-05.1 chọn50đối tượngđộc lập hayAC-05.2 toànnguồn. Ngàyđầu11/2026không tự bỏ gatecoverage, sourceauthority,backupNukeViet,restore và rollback.
