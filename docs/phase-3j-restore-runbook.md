# Phase3J — diễn tập phục hồi cô lập

> **Cập nhật3L22/10:** đã restore thật bộ Dev mới nhất vào `LandPortal_Restore3J_d9d16f42`, READ_ONLY;250content/261revision/584media/500alias/68snapshot derive từ restored rows, DBCC/hash/allcolumns/schema/relations/Identitypermissions/3PDFread0mismatch. Backup `ll0f39ab66`; không còn trạng thái newest250SQLrestoreNOTRUN của3K. Giữ cả recovery235 cũ, khôngDROP/overwrite. [Báo cáo3L](phase-3l-report.md), [runbook3L](phase-3l-runbook.md). TargetBộ/RPO-RTO vẫnNOTRUN; các hướng dẫn235 phía dưới là lịch sử.

> Cập nhật3K22/10: giữ actualrestore235 PASS/READ_ONLY, khôngDROP. Backup mới250 tại`kk197d961b`đãCHECKSUM/VERIFYONLY/fileintegrityPASS nhưng **SQLrestorebộmớiNOTRUN**. [Bướcdiễntậptiếptheo](phase-3k-runbook.md) dùng DB/report mới và baseline250 riêng; không dùng audit hardcoded235 phía dưới để ép PASS250. ProductiontargetRPO/RTOvẫnchưanghiệmthu.

Người dùng đã cho phép restore SQL thật vào database phục hồi Test **mới**, tách khỏi `LandPortal_Dev` và `LandPortal_Test`. Quyền này không cấp quyền reset hai database đang dùng, deploy hoặc chuyển media đang hoạt động.

`scripts/restore-project-rehearsal.ps1` tái sử dụng complete-backup receipt hiện có, kiểm SHA SQL/file manifest, CHECKSUM header, FILELISTONLY và dung lượng trước RESTORE WITH MOVE/CHECKSUM/RECOVERY. Script từ chối tên DB đang dùng, tên ngoài `LandPortal_Restore3J_<8hex>`, database/thư mục đã tồn tại, backup lỗi và đường dẫn ngoài kho riêng dự án. Không dùng WITH REPLACE, KILL, DROP, sửa common database guard hay cấp quyền mới.

Quy trình `FILELISTONLY`/`MOVE` theo [Microsoft Learn](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/restore-a-database-to-a-new-location-sql-server?view=sql-server-ver17). Timeout120s dành riêng RESTORE/DBCC; không đổi timeout truy vấn hay cấu hình SQL ứng dụng.

```powershell
# Chỉ dùng NEW_UNIQUE report và DB chưa tồn tại; receipt/hash phải đọc từ bằng chứng thật.
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/restore-project-rehearsal.ps1 `
  -BackupReceipt <COMPLETE_RECEIPT_JSON> -BackupReceiptSha256 <EXACT_RECEIPT_SHA256> `
  -RestoreDatabase LandPortal_Restore3J_<8hex> `
  -ReportDirectory .local/evidence/phase3j/<NEW_RUN>/restore
rtk proxy python -X utf8 scripts/phase3j-audit.py restore-readback --run .local/evidence/phase3j/<RUN>
```

Lệnh audit hiện đối chiếu mốc b13 Phase3I được chọn trong Phase3J, không phải trình kiểm chứng chung cho mọi backup. Chọn backup khác phải cập nhật input/baseline được review, không đổi số đếm để ép PASS. Không chạy lại vào report cũ.

Sau RESTORE: DBCC CHECKDB thật; đặt recovery DB READ_ONLY; xuất schema/migrations và content/revision/source/media/alias/relations để đối chiếu; khôi phục tệp vào root riêng; kiểm từng SHA/length, snapshot, published/current pointers và trạng thái scanner. Đọc bài/tài liệu đại diện từ bản phục hồi. Không mở HTTP/worker hoặc gửi outbox từ DB phục hồi.

Backup b13 là **trước packet cuối**, chứa235 nội dung/246revision/569media/470alias, không phải250 hiện tại. SQL/file restore PASS tại mốc đó không chứng minh RPO/RTO Production hoặc phục hồi đủ15 bài cuối. Giữ DB phục hồi chỉ đọc và kho riêng để review; chưa tự DROP/xóa. Muốn dọn sau review cần lệnh/phạm vi riêng. Kho backup chứa keys/config riêng tư; không đưa raw secret vào báo cáo/Git.
