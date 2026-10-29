# Vận hành batch nhập và đối soát Phase 3B

**Checkpoint mới:** [Phase3C](phase-3c-media-recovery-report.md)/[runbook3C](phase-3c-runbook.md) đã giải phóng27/30 trênTest bằng scanthật.0bàimới/2comparison;Dev53NotScanned và30blockgiữnguyên. SafeApplyđã cóTest nhưngDevđòiexternalapproval/trustpolicy,khôngtựApply. Các dòng cũ “chưa có entrypoint” bên dưới là lịch sử3B,không phải trạng tháicode3C.

## Checkpoint Unblock / FAST DELIVERY

Đọc [báo cáo mới](phase-3b-unblock-report.md) và [bảng30 mục](phase-3b-unblock-analysis.md). Build mới ở `.local/build/phase3b/unblock-current`, không ghi đè binary preview cũ. Dùng `scripts/test-phase3b.ps1 -Region Unblock -SkipAudit` cho delta importer; không chạy All137/video/CMS không liên quan. Full137 bên dưới là lịch sử trước Unblock, không phải regression code hiện tại.

Plan schema2 thêm `Findings` và `OptionalImages`; vẫn nhận schema1 cũ không có omission. Mọi lỗi scanner/hash/nonempty-format/PDF/video/alias/concurrency vẫn chặn. `NEEDS_REVIEW` cũng không được Commit; chưa đủ bằng chứng không thành NON-BLOCKING.

Chỉ ảnh minh họa Article có đầy đủ văn bản, không phải attachment/ảnh liên kết, và có quyết định theo **identity+URL+reason+evidence** mới được xem xét bỏ khi thực sự thiếu hoặc exact capture0byte. Không tự coi mọi ảnh là tùy chọn. Không sửa `AssetUrls` hoặc checkpoint. Tệp không rỗng có mặt mà NotScanned/Infected hoặc sai hash không thể bỏ bằng review này. Giữ caption/text; không tạo ảnh/hotlink. Metadata tùy chọn để trống như nguồn và lưu warning. Audit `Phase3B.ImportWarning` gắn đúngrevision/batch, xem trên `/admin/history/{contentId}` qua quyền hiện có.

Review file JSON riêng trong `.local/evidence/phase3b/<run>/`, dạng dictionary `Article:<sourceId>` → danh sách `{ "Url": "...", "Reason": "...", "Evidence": "..." }`. Không copy review fixture sang bài nguồn. Preview thêm `-OptionalImages <path> -OptionalImagesSha <exactSHA> -BuildRoot .local/build/phase3b/unblock-current`; plan chứa review và fingerprint để Commit kiểm lại. Không sửa plan cũ để đổi quyết định đã có audit. Mỗi `-ReportRoot` phải mới, không tái dùng helper có đường dẫn evidence cố định đã tồn tại.

Hiện30 mục đều BLOCKING.29 mục tham chiếu chính xác53 tài nguyên gốc NotScanned; Article3681 chỉ bỏ được1 ảnh phụ rỗng nhưng ảnh khác vẫn chưaquét. Không nhập thêm nguồn/không thay public để demo. Repeat plan gốc chỉ xác nhận10 AlreadyCommitted/30Blocked, không gọi là10 bài mới nhập.

Dev manifest `unblock-20261019T030741Z-26e14fd7/dev-batch-manifest.json` chuẩn bị3new/7ảnh/1comparison/6Keep/20URL; **NOT_READY_FOR_APPROVAL / NEEDS_APPROVAL** vì wrapper/service chưa mở ghi và rollbackDev được duyệt. Không có lệnh Apply hợp lệ ở checkpoint này. Dev chỉ `-Action Preview -Database LandPortal_Dev` với build mới. Khi có thẩm quyền phải chốt entrypoint và rollback, freshpreview/hash/stamp, backup/target/17-group expected-delta trước Apply. Không dùng legacyimport để lách. 53gốc vẫnNotScanned; không dùng proofcopied staging để promote chúng hoặc bỏ policyProduction.

Preview codeUnblock `http://127.0.0.1:5302/`, lệnh `rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3b-unblock-preview-web.ps1`; SQLTest, MailDisabled, không scannerbypass.3GETsmoke200 riêng trong task, không cộng7/7. Chỉ khởi động khi port rảnh; giữ mọi preview cũ. Link bài3666 `/bai-viet/4f6daf9f-8013-4f6d-aaff-fdbc8c061032`; Test có fixture QA khác, không coi tổngTestDB là số bài nguồn.

Đây là công cụ offline cho Development, SQL Server local của dự án. Chỉ `LandPortal_Test` được Commit/Verify/Rollback; `LandPortal_Dev` chỉ Preview đọc. Không tự tạo quyền Apply Dev, không dùng batch này để phê duyệt crawl hoặc cutover. Đọc [báo cáo](phase-3b-import-report.md) và [manifest](phase-3b-import-manifest.md) trước khi chạy.

## Đầu vào và backup

Checkpoint thật: `.local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3.bin`. SHA là tên file, schema checkpoint3. Công cụ kiểm SHA, đường dẫn đúng session, reparse point, kích thước nén/giải nén; đối chiếu lại raw detail, metadata và danh sách tài nguyên trước nhập. Không sửa checkpoint để bỏ file thiếu.

Backup trước Test import phải gồm SQL COPY_ONLY/CHECKSUM/VERIFYONLY, media/snapshots/keys và **kho inventory của session**. `scripts/backup-project.ps1` có tham số `-InventorySession` và `-EvidencePhase phase3b -EvidenceDirectory <run-riêng>`. Phải chạy với profile Test đã khởi tạo; mặc định common.ps1 là Dev nên không gọi lệnh backup tùy tiện rồi hiểu nhầm là Test. Có thể cấp quyền ghi của SQL service vào đúng thư mục backup riêng qua cờ có sẵn; đây không phải grant Identity/SQL Dev. Kiểm manifest byte/hash độc lập. VERIFYONLY không phải restore drill.

Đợi mutex Test rảnh; không chạy manual import song song regression. Giữ mọi backup lỗi hoặc dở dang và log tương ứng; tạo run mới khi thử lại. Không reset SQL hay xóa bài nguồn để thử.

## Preview

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3b.ps1 -Action Preview -Checkpoint .local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3.bin -CheckpointSha d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3
```

Sinh thư mục `batch-{UTC}-{id}` mới, gồm plan.json, mapping.csv và summary.json. CSV chống công thức. Xem từng SourceIdentity/SourceURL/type/ExistingCMSId/action/validation và Differences, không chỉ tổng số. `ExistingChanged` nghĩa là sai khác cần đối chiếu với revision nguồn hiện có, không tự chứng minh nguồn vừa sửa. Có thể là metadata/adapter lịch sử hoặc fixture cũ của Test. Không gộp theo tiêu đề.

Mọi plan chứa database, session, checkpoint SHA, BatchId, fingerprint nguồn, stamp rowversion/revisions/aliases hiện có. Lưu SHA plan để người vận hành duyệt đúng bytes. Plan tạo trước thay đổi thuật toán chuẩn hóa phải preview lại; không sửa plan cũ để ép Commit. Khi CMS đã thay đổi từ lúc preview, Commit trả lỗi và yêu cầu preview mới.

## Commit và tiếp tục

Sau khi kiểm backup và preview thực tế, thay PLAN/SHA bằng đúng đường dẫn/hash đã xem, không dùng dữ liệu mẫu dưới đây như phê duyệt:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3b.ps1 -Action Commit -NoBuild -Checkpoint <CHECKPOINT> -CheckpointSha <CHECKPOINT_SHA> -Plan <PLAN_JSON> -PlanSha <PLAN_SHA>
```

Không có tùy chọn Apply Dev. Wrapper và service đều chặn Dev writes. `-NoBuild` chỉ dùng khi build source hiện tại khớp manifest code; không fallback binary của mốc cũ.

Mỗi nội dung dùng transaction Serializable và SQL applock chung, unique SourceIdentity/alias/hash của schema hiện có. Khóa phải lấy thành công trước materialize. File được ghi CreateNew/hash/fsync ngoài static root, SQL commit sau. Rollback transaction không xóa file orphan; giữ private, không tự dọn. Nguồn trùng nhưng alias khác giữ mapping đã xác minh; alias đang thuộc nội dung khác chặn thay vì ghi đè.

`ImportRuns.Id = BatchId` liên kết crawl session, bộ đếm theo transaction. Audit `Phase3B.Import` giữ BatchId + plan SHA + identity và revision đích; đây là checkpoint từng mục. Chạy lại **cùng plan/hash** nhận AlreadyCommitted, không thêm revision/asset/audit. `-StopAfter N` tạm dừng sau N mục đủ điều kiện đã commit; mục Blocked không tính N. Resume là gọi Commit lại cùng plan. Exception trước commit rollback mục đó và dừng batch; các mục đã commit còn nguyên. Process-kill đúng giữa materialize/SQL chưa được nghiệm thu bằng thao tác thực, không thay bằng fault fixture.

New tạo public source revision trên Test, giữ ngày và thiếu trường nguồn như nguồn. ExistingChanged chỉ thêm revision so sánh, không đổi current/published/approved/draft/live categories, kể cả chưa có editor. Keep chỉ bổ sung alias hợp lệ/audit, không tạo revision mới. Không cần nhập tay từng bài. Không có quyền tự publish bản comparison.

## Tài nguyên và ACL

Chỉ file có định dạng hợp lệ, Clean + engine/time/scanned SHA bằng exact bytes mới đủ điều kiện. Không gắn Clean từ mock. Không dùng proof staging để nâng trạng thái MediaAsset cũ hoặc 53 original. Mẫu 3A có7 ảnh mới đủ điều kiện; các PDF cache chưa đủ và video vẫn metadata. Trạng thái bị chặn phải giữ trong mapping.

Asset batch mới lưu vào media Test dưới `phase3b/{SHA}.{extension}`, ValidationStatus Phase3BValidated. Raw header nguồn được lưu riêng, MIME đích nhận diện theo bytes. Public GET/HEAD kiểm current grant, Clean, byte length/SHA, mở stream read-only và Range. Nháp/quarantine/tệp không public không có grant. Tệp không tự phục vụ qua static root. Các ảnh source cũ giữ policy Development cũ và NotScanned, không dùng chúng để tuyên bố đã quét.

Proof Phase3B này chỉ được dùng ở Development loopback; Staging/Production từ chối dù nhãn Clean. Workflow biên tập kiểm lại byte trước lưu/phát hành. Card chỉ chọn thumbnail đủ policy hiện tại và có liên kết revision, không bật bypass để che ảnh lỗi. Khi Apply có thẩm quyền ở môi trường khác phải có policy scanner thực được duyệt, không sao chép nhãn Test sang Production.

## Verify và rollback

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3b.ps1 -Action Verify -NoBuild -Checkpoint <CHECKPOINT> -CheckpointSha <CHECKPOINT_SHA> -Plan <PLAN_JSON> -PlanSha <PLAN_SHA>
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3b.ps1 -Action Rollback -NoBuild -Checkpoint <CHECKPOINT> -CheckpointSha <CHECKPOINT_SHA> -Plan <PLAN_JSON> -PlanSha <PLAN_SHA>
```

Verify kiểm audit/revision/text/file/hash/alias và public revision của new; không biến Blocked thành đã nhập. Rollback là **gỡ công khai các nội dung mới của batch**, giữ source/revision/file/audit, không xóa toàn database hay xóa bản comparison. Bài đã được biên tập/current khác batch bị từ chối để xử lý có thẩm quyền. Rollback idempotent; không tự publish lại bằng Commit cùng plan đã có audit. Muốn xuất bản lại dùng workflow/duyệt phù hợp. Backup restore là thao tác khác và chưa làm trong 3B.

## Kiểm thử và Dev

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3b.ps1 -Region Batch
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3b.ps1 -Region All
```

Build riêng `.local/build/phase3b/current`, evidence `phase3b-tests-*`, không sửa code/scripts khi frozen. HTTP vùng3B yêu cầu ba bài thật đã materialize trên Test; không tạo bài giả thay bằng chứng. Các technical fixture test transaction có tên rõ, không cộng vào dữ liệu nguồn. Full không crawl nguồn; scanner Clean/EICAR thật vẫn theo policy Test công khai/ẩn danh.

Dev Preview dùng `-Database LandPortal_Dev`, không commit. `CreateNeedsApproval` chỉ là đề xuất. Sau Test, kiểm backup/target và17 nhóm source/Identity/env trước gửi duyệt. Apply Dev hiện NEEDS_APPROVAL và entrypoint ghi Dev chưa mở; không dùng import legacy để lách. Không DNS/redirect trên domain thật; mapping gdla là đề xuất cutover, kiểm 301 trên loopback. Tổng nguồn UNKNOWN, crawl rộng NEEDS_APPROVAL, SQL timeout ROOT CAUSE UNKNOWN và incident raw lịch sử giữ nguyên.

Preview local trên workspace này, bằng binary code cuối và SQL Test (không build lại, không ghi Dev):

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3b-preview.ps1
```

URL `http://127.0.0.1:5298/`; Test chứa fixture QA có nhãn rõ, không cộng chúng vào40 nguồn. Xem link ba bài thật trong manifest để review đúng dữ liệu. HTTP tự động dùng5299 và dừng web của riêng run; không dừng preview cũ5198/5208/5288. Helper `.local` là cấu hình vận hành local trong evidence, không phải dịch vụ Windows hoặc triển khai public.
