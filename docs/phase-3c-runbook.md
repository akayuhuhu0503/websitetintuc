# Runbook Phase3C

Đọc [báo cáo](phase-3c-media-recovery-report.md), checkpoint và manifest trước khi chạy. Chỉ Test recovery được phép. Dev53 vẫnNotScanned; không dùng proof Test để tự promote Dev. Không signed approval/trust policy được duyệt thì SafeApplyDev fail-closed. Không sửa.env hoặc tự pin chứng chỉ.

## Bằng chứng hiện tại

Root `.local/evidence/phase3c/phase3c-20261019T033700Z-136775a5/`:

- `inventory/inventory.json`, SHA `5a5ae635bddb6a3de9c7b3fbea2a75441ad3bc375684cd36a47d755bb0a44b0e`:53 protected +7 refresh.
- `scan-resume/proof-set.json`, SHA `a3d8ba03b08dd84abde986ef697e7e8fb2990b3852e79c73d53eb8dd9d38d84a`:60 proof native, SQL receipt và TTL24h.
- `resource-to-identities.csv`:mỗi SHA/URL/identity/engine/policy/expiry, không đếm tham chiếu thành bài mới.
- `test-preview/plan.json`, SHA `fe4ded6337f7cfac9c227b51c19857dcb32bea4b07be438d14e4f719234e6214`; real apply/repeat/verify và readbacks.
- `dev-preview/plan.json`, SHA `27f308ece2e5a762b549ce56f762ba1c3a1e938d98c22ee9135d08b002303dc3`; Dev vẫn30 blocked.
- `dev-apply-manifest.json`, SHA `4d081c4654ded6abb521501c2868165d423571959a61c10c0bf1366a0173a13f`, `proposed-dev-apply-command.txt`; **chưa thực thi**.

Mỗi ReportRoot/log phải mới. Không chạy helperlocal với đường dẫn report đã tồn tại. Không sửa code/scripts khi scan/test frozen. Dùng cùng Inventory/JobId để resume; không tạo job mới để che failed attempts. Proof sai/expired/different definitions phải quét lại scope Test hợp lệ, không sửa nhãn. Sau3receipt/asset/job cần review job mới, không retry mù. Infected giữ quarantine; ScanFailed không suy Clean.

## Recovery Test

`scripts/phase3c.ps1 -Action Inventory|Scan|StageTest|Preview|Commit|Verify|SafeApply|SafeVerify|SafeRollback` dùng SQL mutex và `.local/build/phase3c/current`. `Inventory` cần checkpointSHA/source-preservation proofSHA; scope53 sourcepublic được kiểm lại, thêm7ảnh công khai có sẵn. `Scan -Inventory <path> -InventorySha <sha> -StopAfter N` tạo partial proofset; resume cùnginventory với ReportRoot mới. `Register` chỉ tái kiểm manifest với checkpoint/source-proof rồi đăng ký binding cho job cũ, không tạo scan conclusion.

`StageTest` cần proofset exactSHA hiện còn hiệu lực, kiểm lại mọi media byte trước transaction flags/audit. Backup trước và đọc lại hash theo runbook backup dự án. Chuyển trạng thái mirror Test là thay đổi có chủ đích được prompt3C cho phép, không đổi originalDev hoặc sealed3A. `Preview/Commit/Verify` truyền thêm `-Inventory/-InventorySha/-ProofSet/-ProofSetSha` để dùng overlayRAM, giữ `-OptionalImages/-OptionalImagesSha` cho ảnh3681 đã review. Không sửa plan/hash đã có audit.

SafeApplyTest cần `-ConfirmTest <planSHA> -BackupReceipt <actualTestBackupReceipt>`; Dev không chấp nhận confirmTest thay approval. SafeVerify chỉ đọc; sau rollback bình thường Verify-publication báo bài đã gỡ, nên kiểm riêng unpublished/pointers/files/retained source như test fixture. Không gọi đó là lỗi dữ liệu mất.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3c.ps1
```

Chỉ6 nhóm liên quan, buildCLI/Web/Test, không full/video/Worker. Các fixture có TECHNICAL TEST, giữ trong SQLTest/khoTest, không tính vào40 nguồn. Orphan sau SQLrollback ở private storage là trạng thái phục hồi có kiểm soát; không tự xóa. Lần chạy lại xác minh hash và dùng lại tệp rồi commitSQL; download chỉ có grant sau publication.

## Dev Apply dự kiến — KHÔNG CHẠY TRƯỚC KHI DUYỆT

1. Người có thẩm quyền duyệt signer/trust policy và Windows operator. Cấu hình `SafeImport__ApprovedSignerThumbprints` qua cấu hình vận hành được duyệt, **không tự sửa.env/trust store**. Approval phải được ký Authenticode bởi cert có chain Windows hợp lệ và thumbprint nằm trong policy. Không có lệnh của dự án tự tạo approval hoặc tin cậy cert.
2. Approval file private `.local/approvals/phase3c/*.ps1` chỉ là tài liệu ký, **không execute**. Một dòng metadata ký có tiền tố `# LANDPORTAL_PHASE3C_APPROVAL ` và JSON gồm `ApprovalId`, `PlanSha256`, `TargetDatabase`, `OperatorSid`, `ExpiresUtc`, `Actions` (`Apply`/`Rollback`). Đây là mô tả định dạng, không phải approval thực. Tối đa7 ngày, bind exactplan/target/operator; proof native còn hạn và cùngdefinitions là điều kiện riêng.
3. Fresh read-only preview/target/schema/17group expected-delta. Backup SQL/file/inventory đúngLandPortal_Dev theo `backup-project.ps1`, CHECKSUM/VERIFYONLY/independent hash readback; receipt≤12h, `.bak` SHA và SQL HEADER được kiểm lại. Không grant SQL/Identity để vượt kiểm quyền; operator phải có quyền sẵn. Backupfolder ACL theo quy trình đơn vị, không mở global permissions.
   Bảo toàn thêm các dependency recovery: plan/review/inventory/proofset/evidence và thư mục private `.local/tests/phase3c/{JobId}/proofs`, cùng SQLTest receipts nếu cần khôi phục proof; helperbackup hiện tại không tự enlist thư mục job này. Sao chép/manifest riêng theo allowlist có thẩm quyền, không đưa tệp/quarantine lên dịch vụ ngoài. Proof file mất/không có SQLreceipt thì fail-closed và phải scan lại, không tái tạo receipt giả. VERIFYONLY không phải restore drill.
4. Dùng exact command đã xuất trong task `proposed-dev-apply-command.txt`, thay **hai đường dẫn bên ngoài** Approval/DevBackupReceipt và tên reportduynhất. Không gọi fileđó là lệnh đã chạy. Proof/DBstamps hết hạn hoặc thay đổi phải preview/duyệt lại, không sửa plan cũ. Lệnh dùng `scripts/phase3c.ps1 -Action SafeApply -Database LandPortal_Dev`;3 new/7assets/1comparison/6Keep, tuyệt đối không promote53.
5. SafeVerify cùngplan/proofs với ReportRoot mới; đối chiếu expected-delta3content/4revision/7asset/6alias, existing public/editor payload0đổi và53NotScanned. Không DNS/redirect thật.

Rollback dùng cùng tham số/planSHA, đổi `-Action SafeRollback`, report mới và approval có actionRollback + backup fresh. Chỉ withdraw publication mới còn nguyên batch/current/published/generation; nội dung đã editor sửa bị từ chối. Source/revisions/media/audit/alias được giữ; không xóa blob, không tự restore/resetDB. Khôi phục publication sau rollback phải là quyết định mới, không gọi Commit cùngplan để lách audit idempotent.

**Positive external signature/Dev execution vẫn NOT RUN.** Chưa cấu hình authority thì batch giữ BLOCKED_EXTERNAL_APPROVAL_POLICY; không đưa công cụ Test thành quyền tự Apply Dev. MediaDev53 cần policy/approval riêng cho việc quét hoặc áp dụng, không nằm trong contentbatch ở trên.

## Còn chặn

PDF3233:ứng viênprivate3trang/hash/render giống nhưng cóByteRange, signature chưa verified;khôngpublic/nhập/nhãnClean. Video40 phải đúngID40, không lấyYouTube củaID41 từtemplate nhầm; video41directMP4gdla cần quyền.0videođã chuyển. Bảo toàn incidentraw lịch sử và SQLtimeoutUNKNOWN. Production/private scanner, MFA/SMTP/restore/UAT/cutover còn riêng.
